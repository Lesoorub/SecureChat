using System.IO;

namespace SecureChat.Core.Interfaces;

public interface IWebView
{
    Task<FileInfo?> SaveFileDialog();
    Task<FileInfo?> OpenFileDialog();
    void NavigateAsync(string url);
    Task ExecuteScriptAsync(string jsCode);
    Task<T?> ExecuteScriptAsync<T>(string jsCode);
    void PostMessage(object data);
}