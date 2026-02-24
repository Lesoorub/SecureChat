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
    private readonly double _resampleRatio;
    private readonly int _bytesPerFrame;    // Размер 20мс фрейма в байтах

    public delegate void AudioDataArgs(ArraySegment<byte> buffer);
    public event AudioDataArgs? AudioData;

    public AudioInput(string? deviceId, WaveFormat networkFormat, AudioOutput audioOutput)
    {
        _networkFormat = networkFormat;
        _audioOutput = audioOutput;
        _aec = audioOutput.EchoProvider; // Получаем доступ к общему AEC

        using var enumerator = new MMDeviceEnumerator();
        if (string.IsNullOrEmpty(deviceId))
        {
            using var device = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).First();
            _waveIn = new WasapiCapture(device, true, 20);
        }
        else
        {
            using var device = enumerator.GetDevice(deviceId);
            _waveIn = new WasapiCapture(device, true, 20);
        }
        _waveIn.DataAvailable += HandleAudioData;

        // Буфер для сырых данных с микрофона (в его родном формате)
        _captureBuffer = new BufferedWaveProvider(_waveIn.WaveFormat) { ReadFully = false };

        _volumeControl = new VolumeSampleProvider(_captureBuffer.ToSampleProvider()) { Volume = _volume };

        // Ресемплер: из формата микрофона -> в ваш сетевой 48кГц/1канал
        _captureResampler = new MediaFoundationResampler(_volumeControl.ToWaveProvider(), networkFormat);
        _captureResampler.ResamplerQuality = 60; // Хорошее качество

        // Настройка Opus (48кГц, 1 канал, низкая задержка)
        _encoder = OpusCodecFactory.CreateEncoder(networkFormat.SampleRate, networkFormat.Channels, OpusApplication.OPUS_APPLICATION_VOIP);
        _encoder.Bitrate = 32000; // 32 kbps — отлично для голоса
        _frameSize = networkFormat.SampleRate / 50; //Рассчитываем размер фрейма (20мс — стандарт)

        // Точный расчет размеров буферов
        _bytesPerFrame = _networkFormat.AverageBytesPerSecond / 50; // Рассчитываем точное количество байт для 20мс фрейма один раз
        _conversionBuffer = new byte[_bytesPerFrame]; // Инициализируем буфер ровно под этот размер. Буфер для PCM16 данных (2 байта на семпл)
        _encodedBuffer = new byte[1024]; // 1КБ хватит для любого сжатого кадра

        _resampleRatio = (double)_networkFormat.AverageBytesPerSecond / _waveIn.WaveFormat.AverageBytesPerSecond;
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

    // Вспомогательный метод для оценки доступности данных
    private bool CanReadFullFrame(int bytesNeeded) => _captureBuffer.BufferedBytes * _resampleRatio >= bytesNeeded;

    public void Enable()
    {
        _waveIn.StartRecording();
    }

    public void Disable()
    {
        _waveIn.StopRecording();
    }

    public void Dispose()
    {
        AudioData = null;
        _waveIn.StopRecording();
        _waveIn.DataAvailable -= HandleAudioData;
        _waveIn.Dispose();
        //_captureResampler.Dispose();
    }
}