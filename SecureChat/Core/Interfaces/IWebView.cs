namespace SecureChat.Core.Interfaces;

public interface IWebView
{
    void NavigateAsync(string url);
    Task ExecuteScriptAsync(string jsCode);
    Task<T?> ExecuteScriptAsync<T>(string jsCode);
    void PostMessage(object data);
}