using NAudio.Wave;

namespace SecureChat.Features.Chat.Components;

internal static class NetworkConstants
{
    public static readonly WaveFormat AudioFormat = new(16000, 16, 1);
}
