namespace SecureChat;

internal abstract class AbstractTab
{
    public abstract void PageLoaded();
    public abstract void ProcessPostMessage(string json);
}
