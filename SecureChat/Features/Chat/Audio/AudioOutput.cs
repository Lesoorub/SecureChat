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

    private WasapiOut _waveOut;
    private BufferedWaveProvider _waveProvider;
    private VolumeSampleProvider _outVolumeControl;

    private readonly IOpusDecoder _decoder;
    private readonly short[] _decodeBuffer; // Буфер для PCM16
    private readonly byte[] _pcmByteArray;  // Буфер для байтового представления PCM16
    private readonly int _frameSize;
    public EchoCancellationWaveProvider EchoProvider { get; private set; }

    public AudioOutput(string? deviceId, WaveFormat networkFormat)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            _waveOut = new WasapiOut(AudioClientShareMode.Shared, true, 100);
        }
        else
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(deviceId);
            _waveOut = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
        }

        // 1. Инициализация декодера (частота и каналы должны совпадать с энкодером)
        _decoder = OpusCodecFactory.CreateDecoder(networkFormat.SampleRate, networkFormat.Channels);

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

        // Инициализируем AEC (20мс кадр, 200мс фильтр)
        EchoProvider = new EchoCancellationWaveProvider(20, 200, _waveProvider);

        // Цепочка: _waveProvider -> EchoProvider -> Resampler -> Volume -> WasapiOut
        var sampleProvider = EchoProvider.ToSampleProvider();

        var resampler = new WdlResamplingSampleProvider(sampleProvider, _waveOut.OutputWaveFormat.SampleRate);

        ISampleProvider finalProvider = resampler;
        if (_waveOut.OutputWaveFormat.Channels > 1 && networkFormat.Channels == 1)
        {
            finalProvider = new MonoToStereoSampleProvider(resampler);
        }

        _outVolumeControl = new VolumeSampleProvider(finalProvider) { Volume = _volume };
        _waveOut.Init(_outVolumeControl.ToWaveProvider());
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