using System.Runtime.InteropServices;
using Concentus;
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

    private WaveFormat _networkFormat;
    private WasapiOut _waveOut;
    private BufferedWaveProvider _waveProvider;
    private VolumeSampleProvider _outVolumeControl;
    public EchoCancellationWaveProvider EchoProvider { get; private set; }

    private readonly IOpusDecoder _decoder;
    private readonly short[] _decodeBuffer; // Буфер для PCM16
    private readonly byte[] _pcmByteArray;  // Буфер для байтового представления PCM16
    private readonly int _frameSize;
    private bool _isPlaying;

    public AudioOutput()
    {
        _networkFormat = NetworkConstants.AudioFormat;
        _waveOut = new WasapiOut(shareMode: AudioClientShareMode.Shared, useEventSync: true, latency: 100);

        // 1. Инициализация декодера (частота и каналы должны совпадать с энкодером)
        _decoder = OpusCodecFactory.CreateDecoder(_networkFormat.SampleRate, _networkFormat.Channels);

        // 2. Размер фрейма (20мс для 48кГц = 960 семплов)
        _frameSize = _networkFormat.SampleRate / 50;

        // 3. Подготовка буферов для декодирования
        _decodeBuffer = new short[_frameSize * _networkFormat.Channels];
        _pcmByteArray = new byte[_decodeBuffer.Length * 2];
        (_waveOut, _waveProvider, EchoProvider, _outVolumeControl) = CreateDevices(_networkFormat, Volume);
    }

    public void UpdateDeviceId(string? deviceId = null)
    {
        if (_waveOut is not null)
        {
            _waveOut.Stop();
            _waveOut.Dispose();
        }
        (_waveOut, _waveProvider, EchoProvider, _outVolumeControl) = CreateDevices(_networkFormat, Volume, deviceId);
        if (_isPlaying)
        {
            _waveOut.Play();
        }
    }

    private static (WasapiOut, BufferedWaveProvider, EchoCancellationWaveProvider, VolumeSampleProvider) CreateDevices(WaveFormat networkFormat, float volume, string? deviceId = null)
    {
        WasapiOut waveOut;
        if (string.IsNullOrEmpty(deviceId))
        {
            waveOut = new WasapiOut(AudioClientShareMode.Shared, true, 100);
        }
        else
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(deviceId);
            waveOut = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
        }

        var waveProvider = new BufferedWaveProvider(networkFormat)
        {
            DiscardOnBufferOverflow = true,
            ReadFully = true,
        };
        // Инициализируем AEC (20мс кадр, 200мс фильтр)
        var echoProvider = new EchoCancellationWaveProvider(20, 200, waveProvider);

        // Цепочка: _waveProvider -> EchoProvider -> Resampler -> Volume -> WasapiOut
        var sampleProvider = echoProvider.ToSampleProvider();

        var resampler = new WdlResamplingSampleProvider(sampleProvider, waveOut.OutputWaveFormat.SampleRate);

        ISampleProvider finalProvider = resampler;
        if (waveOut.OutputWaveFormat.Channels > 1 && networkFormat.Channels == 1)
        {
            finalProvider = new MonoToStereoSampleProvider(resampler);
        }

        var outVolumeControl = new VolumeSampleProvider(finalProvider) { Volume = volume };
        waveOut.Init(outVolumeControl.ToWaveProvider());

        return (waveOut, waveProvider, echoProvider, outVolumeControl);
    }

    public bool AddAudioData(ReadOnlySpan<byte> encodedBuffer)
    {
        if (encodedBuffer.Length == 0)
        {
            return false;
        }

        try
        {
            // 4. Декодирование Opus -> PCM16 (short[])
            // Используем Span для исключения лишних копирований, если библиотека позволяет
            int decodedSamples = _decoder.Decode(
                in_data: encodedBuffer,
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
            Console.WriteLine($"Decode error: {ex}");
        }
        return false;
    }

    public void Enable()
    {
        _isPlaying = true;
        _waveOut.Play();
    }

    public void Disable()
    {
        _isPlaying = false;
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