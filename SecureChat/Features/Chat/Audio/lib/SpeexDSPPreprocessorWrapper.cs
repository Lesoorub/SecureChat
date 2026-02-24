using SpeexDSPSharp.Core;

namespace SecureChat;

public class SpeexDSPPreprocessorWrapper : SpeexDSPPreprocessor
{
    public IntPtr Handle => _handler.DangerousGetHandle();

    /// <summary>
    /// Preprocessor denoiser state. Default: 1
    /// <para>Состояние шумоподавления. По умолчанию: 1 (включено)</para>
    /// </summary>
    public int Denoise { get { int value = 0; Ctl(PreprocessorCtl.SPEEX_PREPROCESS_GET_DENOISE, ref value); return value; } set => Ctl(PreprocessorCtl.SPEEX_PREPROCESS_SET_DENOISE, ref value); }

    /// <summary>
    /// Preprocessor Automatic Gain Control state. Default: 0
    /// <para>Состояние автоматической регулировки усиления (АРУ/AGC). По умолчанию: 0</para>
    /// </summary>
    public int AgcEnable { get { int value = 0; Ctl(PreprocessorCtl.SPEEX_PREPROCESS_GET_AGC, ref value); return value; } set => Ctl(PreprocessorCtl.SPEEX_PREPROCESS_SET_AGC, ref value); }

    /// <summary>
    /// Preprocessor Voice Activity Detection state. Default: 0
    /// <para>Состояние детектора голосовой активности (VAD). По умолчанию: 0</para>
    /// </summary>
    public int VadEnable { get { int value = 0; Ctl(PreprocessorCtl.SPEEX_PREPROCESS_GET_VAD, ref value); return value; } set => Ctl(PreprocessorCtl.SPEEX_PREPROCESS_SET_VAD, ref value); }

    /// <summary>
    /// Preprocessor Automatic Gain Control level (float). Default: 8000
    /// <para>Уровень АРУ (float). По умолчанию: 8000</para>
    /// </summary>
    public float AgcLevel { get { float value = 0; Ctl(PreprocessorCtl.SPEEX_PREPROCESS_GET_AGC_LEVEL, ref value); return value; } set => Ctl(PreprocessorCtl.SPEEX_PREPROCESS_SET_AGC_LEVEL, ref value); }

    /// <summary>
    /// Preprocessor de-reverb state. Default: 0
    /// <para>Состояние устранения реверберации. По умолчанию: 0 (выключено)</para>
    /// </summary>
    public int DeReverb { get { int value = 0; Ctl(PreprocessorCtl.SPEEX_PREPROCESS_GET_DEREVERB, ref value); return value; } set => Ctl(PreprocessorCtl.SPEEX_PREPROCESS_SET_DEREVERB, ref value); }

    /// <summary>
    /// Preprocessor de-reverb level. Default: 0
    /// <para>Уровень устранения реверберации. По умолчанию: 0</para>
    /// </summary>
    public float DeReverbLevel { get { float value = 0; Ctl(PreprocessorCtl.SPEEX_PREPROCESS_GET_DEREVERB_LEVEL, ref value); return value; } set => Ctl(PreprocessorCtl.SPEEX_PREPROCESS_SET_DEREVERB_LEVEL, ref value); }

    /// <summary>
    /// Preprocessor de-reverb decay. Default: 0
    /// <para>Затухание при устранении реверберации. По умолчанию: 0</para>
    /// </summary>
    public int DeReverbDecay { get { int value = 0; Ctl(PreprocessorCtl.SPEEX_PREPROCESS_GET_DEREVERB_DECAY, ref value); return value; } set => Ctl(PreprocessorCtl.SPEEX_PREPROCESS_SET_DEREVERB_DECAY, ref value); }

    /// <summary>
    /// Probability required for the VAD to go from silence to voice. Default: 35
    /// <para>Вероятность, необходимая для перехода VAD из тишины в голос. По умолчанию: 35</para>
    /// </summary>
    public int ProbStart { get { int value = 0; Ctl(PreprocessorCtl.SPEEX_PREPROCESS_GET_PROB_START, ref value); return value; } set => Ctl(PreprocessorCtl.SPEEX_PREPROCESS_SET_PROB_START, ref value); }

    /// <summary>
    /// Probability required for the VAD to stay in the voice state (integer percent). Default: 20
    /// <para>Вероятность, необходимая для удержания VAD в состоянии голоса. По умолчанию: 20</para>
    /// </summary>
    public int ProbContinue { get { int value = 0; Ctl(PreprocessorCtl.SPEEX_PREPROCESS_GET_PROB_CONTINUE, ref value); return value; } set => Ctl(PreprocessorCtl.SPEEX_PREPROCESS_SET_PROB_CONTINUE, ref value); }

    /// <summary>
    /// Maximum attenuation of the noise in dB (negative number). Default: -15
    /// <para>Максимальное подавление шума в дБ (отрицательное число). По умолчанию: -15</para>
    /// </summary>
    public int NoiseSuppress { get { int value = 0; Ctl(PreprocessorCtl.SPEEX_PREPROCESS_GET_NOISE_SUPPRESS, ref value); return value; } set => Ctl(PreprocessorCtl.SPEEX_PREPROCESS_SET_NOISE_SUPPRESS, ref value); }

    /// <summary>
    /// Maximum attenuation of the residual echo in dB (negative number). Default: -40
    /// <para>Максимальное подавление остаточного эха в дБ (отрицательное число). По умолчанию: -40</para>
    /// </summary>
    public int EchoSuppress { get { int value = 0; Ctl(PreprocessorCtl.SPEEX_PREPROCESS_GET_ECHO_SUPPRESS, ref value); return value; } set => Ctl(PreprocessorCtl.SPEEX_PREPROCESS_SET_ECHO_SUPPRESS, ref value); }

    /// <summary>
    /// Maximum attenuation of the residual echo in dB when near end is active (negative number). Default: -15
    /// <para>Максимальное подавление остаточного эха в дБ при активном ближнем конце. По умолчанию: -15</para>
    /// </summary>
    public int EchoSuppressActive { get { int value = 0; Ctl(PreprocessorCtl.SPEEX_PREPROCESS_GET_ECHO_SUPPRESS_ACTIVE, ref value); return value; } set => Ctl(PreprocessorCtl.SPEEX_PREPROCESS_SET_ECHO_SUPPRESS_ACTIVE, ref value); }

    /// <summary>
    /// The corresponding echo canceler state so that residual echo suppression can be performed (NULL for no residual echo suppression). Default: 0
    /// <para>Состояние эхоподавителя для подавления остаточного эха. По умолчанию: 0</para>
    /// </summary>
    public unsafe IntPtr EchoState
    {
        get
        {
            IntPtr value = IntPtr.Zero;
            Ctl(PreprocessorCtl.SPEEX_PREPROCESS_GET_ECHO_STATE, ref value);
            return value;
        }
        set => Ctl(PreprocessorCtl.SPEEX_PREPROCESS_SET_ECHO_STATE, value); }

    /// <summary>
    /// Maximal gain increase in dB/second (int). Default: 12
    /// <para>Максимальный прирост усиления в дБ/сек. По умолчанию: 12</para>
    /// </summary>
    public int AgcIncrementDbSec { get { int value = 0; Ctl(PreprocessorCtl.SPEEX_PREPROCESS_GET_AGC_INCREMENT, ref value); return value; } set => Ctl(PreprocessorCtl.SPEEX_PREPROCESS_SET_AGC_INCREMENT, ref value); }

    /// <summary>
    /// Maximal gain decrease in dB/second (int). Default: -40
    /// <para>Максимальное снижение усиления в дБ/сек. По умолчанию: -40</para>
    /// </summary>
    public int AgcDecrementDbSec { get { int value = 0; Ctl(PreprocessorCtl.SPEEX_PREPROCESS_GET_AGC_DECREMENT, ref value); return value; } set => Ctl(PreprocessorCtl.SPEEX_PREPROCESS_SET_AGC_DECREMENT, ref value); }

    /// <summary>
    /// Maximal gain in dB (int32). Default: 30
    /// <para>Максимальное усиление в дБ. По умолчанию: 30</para>
    /// </summary>
    public int AgcMaxGain { get { int value = 0; Ctl(PreprocessorCtl.SPEEX_PREPROCESS_GET_AGC_MAX_GAIN, ref value); return value; } set => Ctl(PreprocessorCtl.SPEEX_PREPROCESS_SET_AGC_MAX_GAIN, ref value); }

    /// <summary>
    /// Loudness. Default: 0
    /// <para>Громкость. По умолчанию: 0</para>
    /// </summary>
    public int AgcLoudness { get { int value = 0; Ctl(PreprocessorCtl.SPEEX_PREPROCESS_GET_AGC_LOUDNESS, ref value); return value; } }

    /// <summary>
    /// Current gain (int percent). Default: 0
    /// <para>Текущее усиление (в целых процентах). По умолчанию: 0</para>
    /// </summary>
    public int AgcGainPercent { get { int value = 0; Ctl(PreprocessorCtl.SPEEX_PREPROCESS_GET_AGC_GAIN, ref value); return value; } }

    /// <summary>
    /// Speech probability in last frame (int). Default: 0
    /// <para>Вероятность речи в последнем фрейме. По умолчанию: 0</para>
    /// </summary>
    public int Prob { get { int value = 0; Ctl(PreprocessorCtl.SPEEX_PREPROCESS_GET_PROB, ref value); return value; } }

    /// <summary>
    /// Preprocessor Automatic Gain Control level (int). Default: 8000
    /// <para>Целевой уровень АРУ (int). По умолчанию: 8000</para>
    /// </summary>
    public int AgcTarget { get { int value = 0; Ctl(PreprocessorCtl.SPEEX_PREPROCESS_GET_AGC_TARGET, ref value); return value; } set => Ctl(PreprocessorCtl.SPEEX_PREPROCESS_SET_AGC_TARGET, ref value); }

    public SpeexDSPPreprocessorWrapper(int frame_size, int sample_rate) : base(frame_size, sample_rate)
    {
    }

    public unsafe int Ctl(PreprocessorCtl request, void* pointer)
    {
        return NativeSpeexDSP.speex_preprocess_ctl(_handler, (int)request, pointer);
    }

    public unsafe int Ctl(PreprocessorCtl request, IntPtr pointer)
    {
        return NativeSpeexDSP.speex_preprocess_ctl(_handler, (int)request, pointer.ToPointer());
    }
}
