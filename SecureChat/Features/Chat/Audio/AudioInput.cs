using System.Buffers;
using System.Runtime.InteropServices;
using Concentus;
using Concentus.Enums;
using Concentus.Structs;
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
    private VolumeSampleProvider _volumeControl;

    private BufferedWaveProvider _captureBuffer;
    private MediaFoundationResampler _captureResampler;

    private readonly IOpusEncoder _encoder;
    private readonly int _frameSize; // Кол-во семплов на 20мс
    private readonly byte[] _conversionBuffer; // Для PCM16
    private readonly byte[] _encodedBuffer;    // Для сжатого Opus
    private readonly double _resampleRatio;

    public delegate void AudioDataArgs(ArraySegment<byte> buffer);
    public event AudioDataArgs? AudioData;

    public AudioInput(string? deviceId, WaveFormat networkFormat)
    {
        _networkFormat = networkFormat;

        if (string.IsNullOrEmpty(deviceId))
        {
            _waveIn = new WasapiCapture();
        }
        else
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(deviceId);
            _waveIn = new WasapiCapture(device);
        }

        // Буфер для сырых данных с микрофона (в его родном формате)
        _captureBuffer = new BufferedWaveProvider(_waveIn.WaveFormat) { ReadFully = false };

        _volumeControl = new VolumeSampleProvider(_captureBuffer.ToSampleProvider()) { Volume = _volume };

        // Ресемплер: из формата микрофона -> в ваш сетевой 48кГц/1канал
        _captureResampler = new MediaFoundationResampler(_volumeControl.ToWaveProvider(), networkFormat);
        _captureResampler.ResamplerQuality = 60; // Хорошее качество

        _waveIn.DataAvailable += HandleAudioData;

        // Настройка Opus (48кГц, 1 канал, низкая задержка)
        _encoder = OpusCodecFactory.CreateEncoder(networkFormat.SampleRate, networkFormat.Channels, OpusApplication.OPUS_APPLICATION_VOIP);
        _encoder.Bitrate = 32000; // 32 kbps — отлично для голоса

        //Рассчитываем размер фрейма (20мс — стандарт)
        _frameSize = networkFormat.SampleRate / 50;

        // Рассчитываем точное количество байт для 20мс фрейма один раз
        int bytesNeededForFrame = networkFormat.AverageBytesPerSecond / 50;

        // Инициализируем буфер ровно под этот размер
        // Буфер для PCM16 данных (2 байта на семпл)
        _conversionBuffer = new byte[bytesNeededForFrame];
        _encodedBuffer = new byte[1024]; // 1КБ хватит для любого сжатого кадра
        _resampleRatio = (double)_networkFormat.AverageBytesPerSecond / _waveIn.WaveFormat.AverageBytesPerSecond;
    }

    private void HandleAudioData(object? obj, WaveInEventArgs args)
    {
        try
        {
            // 1. Сначала кладем сырые данные из WASAPI в буфер
            _captureBuffer.AddSamples(args.Buffer, 0, args.BytesRecorded);

            // 2. Рассчитываем, сколько байт в результирующем формате (PCM16) нам нужно вычитать
            // Формула: Частота * Каналы * 2 (байта на sample) * 0.02 (20мс)
            int bytesNeededForFrame = _networkFormat.AverageBytesPerSecond / 50;

            // 3. Пока в буфере достаточно данных, чтобы после ресемплирования получить полный фрейм
            // Важно: проверяем BufferedBytes именно у источника (_captureBuffer)
            // Но так как частоты могут отличаться, лучше ориентироваться на возможности Read
            while (CanReadFullFrame(bytesNeededForFrame))
            {
                // Читаем из ресемплера (он сам заберет из _captureBuffer и сконвертирует)
                int read = _captureResampler.Read(_conversionBuffer, 0, bytesNeededForFrame);

                if (read == bytesNeededForFrame)
                {
                    // Конвертируем byte[] в Span<short> без аллокаций для энкодера
                    ReadOnlySpan<short> pcmSpan = MemoryMarshal.Cast<byte, short>(_conversionBuffer);

                    // Сжатие (используя Concentus или аналоги)
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
        _captureResampler.Dispose();
    }
}
