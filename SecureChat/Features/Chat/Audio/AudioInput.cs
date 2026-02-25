using System.Runtime.InteropServices;
using Concentus;
using Concentus.Enums;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SecureChat.Features.Chat.Components;

internal class AudioInput
{
    private float _volume = 1;
    public float Volume
    {
        get => _volume;
        set => _volumeControl.Volume = _volume = value;
    }

    private readonly WaveFormat _networkFormat;

    private WasapiCapture _waveIn;
    private AudioOutput _audioOutput;

    private readonly EchoCancellationWaveProvider _aec;
    private VolumeSampleProvider _volumeControl;
    private BufferedWaveProvider _captureBuffer;
    private MediaFoundationResampler _captureResampler;

    private readonly IOpusEncoder _encoder;
    private readonly int _frameSize; // Кол-во семплов на 20мс
    private readonly byte[] _conversionBuffer; // Для PCM16
    private readonly byte[] _encodedBuffer;    // Для сжатого Opus
    private double _resampleRatio;
    private readonly int _bytesPerFrame;    // Размер 20мс фрейма в байтах
    private bool _isRecording;

    public float CurrentGain { get; private set; }

    public delegate void AudioDataArgs(ArraySegment<byte> buffer);
    public event AudioDataArgs? AudioData;

    public AudioInput(AudioOutput audioOutput)
    {
        _networkFormat = NetworkConstants.AudioFormat;
        _audioOutput = audioOutput;
        _aec = audioOutput.EchoProvider; // Получаем доступ к общему AEC

        (_waveIn, _captureBuffer, _volumeControl, _captureResampler) = CreateDevice(_networkFormat, Volume);
        _waveIn.DataAvailable += HandleAudioData;
        _resampleRatio = (double)_networkFormat.AverageBytesPerSecond / _waveIn.WaveFormat.AverageBytesPerSecond;

        // Настройка Opus (48кГц, 1 канал, низкая задержка)
        _encoder = OpusCodecFactory.CreateEncoder(_networkFormat.SampleRate, _networkFormat.Channels, OpusApplication.OPUS_APPLICATION_VOIP);
        _encoder.Bitrate = 32000; // 32 kbps — отлично для голоса
        _frameSize = _networkFormat.SampleRate / 50; //Рассчитываем размер фрейма (20мс — стандарт)

        // Точный расчет размеров буферов
        _bytesPerFrame = _networkFormat.AverageBytesPerSecond / 50; // Рассчитываем точное количество байт для 20мс фрейма один раз
        _conversionBuffer = new byte[_bytesPerFrame]; // Инициализируем буфер ровно под этот размер. Буфер для PCM16 данных (2 байта на семпл)
        _encodedBuffer = new byte[1024]; // 1КБ хватит для любого сжатого кадра
    }

    public void UpdateDeviceId(string? deviceId = null)
    {
        // 1. Очищаем
        if (_waveIn is not null)
        {
            _waveIn.StopRecording();
            _waveIn.DataAvailable -= HandleAudioData;
            _waveIn.Dispose();
            _captureResampler.Dispose();
        }

        // 2. Обновляем
        (_waveIn, _captureBuffer, _volumeControl, _captureResampler) = CreateDevice(_networkFormat, Volume, deviceId);
        _waveIn.DataAvailable += HandleAudioData;
        _resampleRatio = (double)_networkFormat.AverageBytesPerSecond / _waveIn.WaveFormat.AverageBytesPerSecond;

        // 3. Восстанавливаем
        if (_isRecording)
        {
            _waveIn.StartRecording();
        }
    }

    private static (WasapiCapture, BufferedWaveProvider, VolumeSampleProvider, MediaFoundationResampler) CreateDevice(WaveFormat _networkFormat, float volume, string? deviceId = null)
    {
        WasapiCapture waveIn;

        using var enumerator = new MMDeviceEnumerator();
        if (string.IsNullOrEmpty(deviceId))
        {
            waveIn = new WasapiCapture(WasapiCapture.GetDefaultCaptureDevice(), true, 20);
            
        }
        else
        {
            using var device = enumerator.GetDevice(deviceId);
            waveIn = new WasapiCapture(device, true, 20);
        }

        // Буфер для сырых данных с микрофона (в его родном формате)
        var captureBuffer = new BufferedWaveProvider(waveIn.WaveFormat) { ReadFully = false };

        var volumeControl = new VolumeSampleProvider(captureBuffer.ToSampleProvider()) { Volume = volume };

        // Ресемплер: из формата микрофона -> в ваш сетевой 48кГц/1канал
        var captureResampler = new MediaFoundationResampler(volumeControl.ToWaveProvider(), _networkFormat);
        captureResampler.ResamplerQuality = 60; // Хорошее качество

        return (waveIn, captureBuffer, volumeControl, captureResampler);
    }

    private void HandleAudioData(object? obj, WaveInEventArgs args)
    {
        try
        {
            _captureBuffer.AddSamples(args.Buffer, 0, args.BytesRecorded);

            Span<byte> cleanBuffer = stackalloc byte[_bytesPerFrame];
            while (CanReadFullFrame(_bytesPerFrame))
            {
                int read = _captureResampler.Read(_conversionBuffer, 0, _bytesPerFrame);

                if (read == _bytesPerFrame)
                {
                    // --- AEC PROCESS START ---
                    // Создаем временный буфер для очищенного сигнала
                    cleanBuffer.Clear();

                    // Прогоняем через AEC: 
                    // _conversionBuffer (микрофон) -> cleanBuffer (без эха)
                    _aec.ProcessCapture(_conversionBuffer, cleanBuffer);

                    // Дальше работаем с cleanBuffer вместо _conversionBuffer
                    ReadOnlySpan<short> pcmSpan = MemoryMarshal.Cast<byte, short>(cleanBuffer);
                    // --- AEC PROCESS END ---

                    var gain = GetGain(pcmSpan, _aec.AgcEnable);
                    CurrentGain = (CurrentGain * 0.8f) + (gain * 0.2f);
                    int encodedLength = _encoder.Encode(pcmSpan, _frameSize, _encodedBuffer.AsSpan(), _encodedBuffer.Length);
                    AudioData?.Invoke(new ArraySegment<byte>(_encodedBuffer, 0, encodedLength));
                }
                else break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Encode error: {ex}");
        }
    }

    private float GetGain(ReadOnlySpan<short> inputSamples, bool hasAgc)
    {
        if (inputSamples.Length == 0) return 0f;

        double sum = 0;
        for (int i = 0; i < inputSamples.Length; i++)
        {
            float sample = inputSamples[i] / 32768.0f;
            sum += sample * sample;
        }

        double rms = Math.Sqrt(sum / inputSamples.Length);

        // Порог абсолютной тишины
        if (rms < 0.00001) return 0f;

        // Переводим в дБ. 0 dB = максимальная амплитуда 32768.
        float db = (float)(20 * Math.Log10(rms));

        // Параметры шкалы
        float minDb = -45f; // Порог видимости (тишина)
        float maxDb = 0f;   // Порог максимума (1.0 на индикаторе)

        if (hasAgc)
        {
            // Speex AGC по умолчанию стремится к уровню 8000.
            // 20 * log10(8000 / 32768) ≈ -12 dB.
            // Если AGC включен, мы считаем -12 dB за "полную шкалу",
            // иначе индикатор никогда не поднимется выше 0.7-0.8.
            maxDb = -12f;
            minDb = -57f; // Смещаем и нижний порог, чтобы сохранить диапазон 45 dB
        }

        // Нормализация в 0.0 ... 1.0
        float normalized = (db - minDb) / (maxDb - minDb);

        return Math.Clamp(normalized, 0f, 1f);
    }

    // Вспомогательный метод для оценки доступности данных
    private bool CanReadFullFrame(int bytesNeeded) => _captureBuffer.BufferedBytes * _resampleRatio >= bytesNeeded;

    public void Enable()
    {
        _isRecording = true;
        _waveIn.StartRecording();
    }

    public void Disable()
    {
        _isRecording = false;
        _waveIn.StopRecording();
    }

    public void Dispose()
    {
        AudioData = null;
        _waveIn.StopRecording();
        _waveIn.DataAvailable -= HandleAudioData;
        _waveIn.Dispose();
        _isRecording = false;
        _captureResampler.Dispose();
    }
}