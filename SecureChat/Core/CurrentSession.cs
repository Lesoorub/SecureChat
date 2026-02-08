namespace SecureChat.Core;

[Singeltone]
internal class CurrentSession
{
    public string Username { get; set; } = string.Empty;
    public ChatSession? Session { get; set; }
}