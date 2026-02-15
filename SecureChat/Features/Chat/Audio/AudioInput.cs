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

    private WasapiCapture _waveIn;
    private VolumeSampleProvider _volumeControl;

    private BufferedWaveProvider _captureBuffer;
    private MediaFoundationResampler _captureResampler;

    public delegate void AudioDataArgs(ArraySegment<byte> buffer);
    public event AudioDataArgs? AudioData;

    public AudioInput(string? deviceId, WaveFormat networkFormat)
    {
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

        // 1. Буфер для сырых данных с микрофона (в его родном формате)
        _captureBuffer = new BufferedWaveProvider(_waveIn.WaveFormat) { ReadFully = false };

        _volumeControl = new VolumeSampleProvider(_captureBuffer.ToSampleProvider()) { Volume = _volume };

        // 2. Ресемплер: из формата микрофона -> в ваш сетевой 48кГц/1канал
        _captureResampler = new MediaFoundationResampler(_volumeControl.ToWaveProvider(), networkFormat);
        _captureResampler.ResamplerQuality = 60; // Хорошее качество

        _waveIn.DataAvailable += HandleAudioData;
    }

    private void HandleAudioData(object? obj, WaveInEventArgs args)
    {
        // Сначала кладем данные в промежуточный буфер
        _captureBuffer.AddSamples(args.Buffer, 0, args.BytesRecorded);

        // Читаем из ресемплера уже преобразованные данные
        byte[] outBuffer = new byte[args.BytesRecorded]; // Размер с запасом
        int read = _captureResampler.Read(outBuffer, 0, outBuffer.Length);
        if (read > 0)
        {
            AudioData?.Invoke(new ArraySegment<byte>(outBuffer, 0, read));
        }
    }

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
