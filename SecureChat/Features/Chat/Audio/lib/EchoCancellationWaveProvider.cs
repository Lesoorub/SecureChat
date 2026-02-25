using NAudio.Wave;

namespace SecureChat;

public class EchoCancellationWaveProvider : IWaveProvider
{
    private readonly BufferedWaveProvider _source;
    private readonly SpeexDSPEchoCancelerWrapper _canceller;
    private readonly SpeexDSPPreprocessorWrapper _preprocessor;
    public WaveFormat WaveFormat => _source.WaveFormat;
    private readonly object _syncLock = new object();
    private readonly byte[] _playbackBuffer;
    private int _playbackBufferOffset;
    private readonly int _frameSizeBytes; // 640 для 20мс 16кГц 16бит

    public bool AgcEnable
    {
        get => _preprocessor.AgcEnable != 0;
        set => _preprocessor.AgcEnable = value ? 1 : 0;
    }

    public bool NoiseSuppression { get; set; } = true;

    public int GainPercent => _preprocessor.AgcGainPercent;

    public EchoCancellationWaveProvider(int frame_size_ms, int filter_length_ms, BufferedWaveProvider source)
    {
        _source = source;
        int sampleRate = WaveFormat.SampleRate;
        // Speex работает с количеством СЭМПЛОВ, а не байт
        int frameSizeSamples = frame_size_ms * sampleRate / 1000;
        int filterLengthSamples = filter_length_ms * sampleRate / 1000;

        _canceller = new SpeexDSPEchoCancelerWrapper(frameSizeSamples, filterLengthSamples);
        _canceller.SamplingRate = sampleRate;

        // Инициализация препроцессора
        _preprocessor = new SpeexDSPPreprocessorWrapper(frameSizeSamples, sampleRate);
        _preprocessor.EchoState = _canceller.Handle;
        AgcEnable = true;

        _frameSizeBytes = frameSizeSamples * 2; // PCM16 = 2 байта на сэмпл
        _playbackBuffer = new byte[_frameSizeBytes];
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        // 1. Читаем из источника
        int read = _source.Read(buffer, offset, count);

        // 2. Копируем во внутренний накопитель для AEC
        int bytesToCopy = Math.Min(read, _frameSizeBytes - _playbackBufferOffset);
        Buffer.BlockCopy(buffer, offset, _playbackBuffer, _playbackBufferOffset, bytesToCopy);
        _playbackBufferOffset += bytesToCopy;

        // 3. Если накопили на целый фрейм — отдаем в Speex
        if (_playbackBufferOffset >= _frameSizeBytes)
        {
            lock (_syncLock)
            {
                _canceller.EchoPlayback(_playbackBuffer);
            }
            //Console.WriteLine($"speaker: {_playbackBufferOffset}");
            _playbackBufferOffset = 0;
            // Если пришло больше (редко), остаток в данном примере игнорируется для простоты
        }

        return read;
    }

    public void ProcessCapture(Span<byte> input, Span<byte> output)
    {
        //Console.WriteLine($"mic: {input.Length}");

        lock (_syncLock) // Синхронизируем доступ к эхоподавителю
        {
            _canceller.EchoCapture(input, output);

            if (NoiseSuppression)
            {
                _preprocessor.Run(output);
            }
        }
    }
}
