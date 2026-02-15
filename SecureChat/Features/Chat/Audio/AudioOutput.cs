using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SecureChat.Features.Chat.Components;

internal class AudioOutput : IDisposable
{
    private float _volume = 1;
    public float Volume
    {
        get => _volume;
        set => _outVolumeControl.Volume = _volume = value;
    }

    private WasapiOut _waveOut;
    private BufferedWaveProvider _waveProvider;
    private VolumeSampleProvider _outVolumeControl;

    public AudioOutput(string? deviceId, WaveFormat networkFormat)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            _waveOut = new WasapiOut();
        }
        else
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(deviceId);
            _waveOut = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
        }

        _waveProvider = new BufferedWaveProvider(networkFormat)
        {
            DiscardOnBufferOverflow = true,
            ReadFully = true,
        };

        // 1. Превращаем байты в Float Sample Provider
        var sampleProvider = _waveProvider.ToSampleProvider();

        // 2. Ресемплируем и меняем количество каналов под целевое устройство (OutputWaveFormat)
        // Это магия, которая адаптирует 48000/1 в то, что хочет Windows (например, 44100/2)
        var resampler = new WdlResamplingSampleProvider(sampleProvider, _waveOut.OutputWaveFormat.SampleRate);

        // 3. Если устройство стерео, а входящий звук моно — приводим к стерео
        ISampleProvider finalProvider = resampler;
        if (_waveOut.OutputWaveFormat.Channels > 1 && networkFormat.Channels == 1)
        {
            finalProvider = new MonoToStereoSampleProvider(resampler);
        }

        _outVolumeControl = new VolumeSampleProvider(finalProvider) { Volume = _volume };
        _waveOut.PlaybackStopped += (s, e) =>
        {
            Console.WriteLine($"Playback Stopped! Reason: {e.Exception?.Message}");
        };
        _waveOut.Init(_outVolumeControl.ToWaveProvider()); // Инициализируем уже адаптированным провайдером
    }

    public bool AddAudioData(ArraySegment<byte> buffer)
    {
        if (_waveProvider is not null)
        {
            _waveProvider.AddSamples(buffer.Array, buffer.Offset, buffer.Count);
            Console.WriteLine($"Buffered bytes: {_waveProvider.BufferedBytes}");
            return true;
        }
        return false;
    }

    public void Enable()
    {
        _waveOut.Play();
    }

    public void Disable()
    {
        _waveOut.Pause();
    }

    public void Dispose()
    {
        if (_waveOut is not null)
        {
            _waveOut.Stop();
            _waveOut.Dispose();
        }
    }
}