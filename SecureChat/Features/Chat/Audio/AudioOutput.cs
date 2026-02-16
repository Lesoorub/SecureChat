using System.Runtime.InteropServices;
using Concentus.Structs;
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

    private readonly OpusDecoder _decoder;
    private readonly short[] _decodeBuffer; // Буфер для PCM16
    private readonly byte[] _pcmByteArray;  // Буфер для байтового представления PCM16
    private readonly int _frameSize;

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

        // 1. Инициализация декодера (частота и каналы должны совпадать с энкодером)
        _decoder = new OpusDecoder(networkFormat.SampleRate, networkFormat.Channels);

        // 2. Размер фрейма (20мс для 48кГц = 960 семплов)
        _frameSize = networkFormat.SampleRate / 50;

        // 3. Подготовка буферов для декодирования
        _decodeBuffer = new short[_frameSize * networkFormat.Channels];
        _pcmByteArray = new byte[_decodeBuffer.Length * 2];

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

    public bool AddAudioData(ArraySegment<byte> encodedBuffer)
    {
        try
        {
            // 4. Декодирование Opus -> PCM16 (short[])
            // Используем Span для исключения лишних копирований, если библиотека позволяет
            int decodedSamples = _decoder.Decode(
                in_data: encodedBuffer.AsSpan(),
                out_pcm: _decodeBuffer,
                frame_size: _frameSize, 
                decode_fec: false
            );

            if (decodedSamples > 0)
            {
                // 5. Быстрое копирование short[] в byte[] для BufferedWaveProvider
                // Используем MemoryMarshal для zero-allocation трансформации
                var sourceBytes = MemoryMarshal.AsBytes(_decodeBuffer.AsSpan(0, decodedSamples));
                sourceBytes.CopyTo(_pcmByteArray);

                _waveProvider.AddSamples(_pcmByteArray, 0, sourceBytes.Length);
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Decode error: {ex.Message}");
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