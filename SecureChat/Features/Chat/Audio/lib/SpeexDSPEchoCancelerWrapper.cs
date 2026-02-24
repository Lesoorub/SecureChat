using SpeexDSPSharp.Core;

namespace SecureChat;

public class SpeexDSPEchoCancelerWrapper : SpeexDSPEchoCanceler
{
    public IntPtr Handle => _handler.DangerousGetHandle();

    public int FrameSize { get { int value = 0; Ctl(EchoCancellationCtl.SPEEX_ECHO_GET_FRAME_SIZE, ref value); return value; } }
    public int SamplingRate { get { int value = 0; Ctl(EchoCancellationCtl.SPEEX_ECHO_GET_SAMPLING_RATE, ref value); return value; } set => Ctl(EchoCancellationCtl.SPEEX_ECHO_SET_SAMPLING_RATE, ref value); }
    public int ImpulseResponseSize { get { int value = 0; Ctl(EchoCancellationCtl.SPEEX_ECHO_GET_IMPULSE_RESPONSE_SIZE, ref value); return value; } }

    public SpeexDSPEchoCancelerWrapper(int frame_size, int filter_length) : base(frame_size, filter_length)
    {

    }

    public SpeexDSPEchoCancelerWrapper(int frame_size, int filter_length, int nb_mic, int nb_speaker) : base(frame_size, filter_length, nb_mic, nb_speaker)
    {
    }
}
