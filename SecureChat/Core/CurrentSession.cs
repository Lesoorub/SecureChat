using SecureChat.Core.Attributes;

namespace SecureChat.Core;

[Singeltone]
internal class CurrentSession
{
    public string Username { get; private set; } = string.Empty;
    public ChatSession? Session { get; private set; }

    public void Set(string username, ChatSession? session)
    {
        Username = username ?? throw new ArgumentNullException(nameof(username));
        Session = session ?? throw new ArgumentNullException();
    }

    public void Reset()
    {
        Session = null;
        Username = string.Empty;
    }
}