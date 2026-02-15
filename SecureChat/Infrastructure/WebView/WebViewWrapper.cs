using System.Security.Policy;
using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;
using SecureChat.Core.Interfaces;

namespace SecureChat.Infrastructure.WebView;

public class WebViewWrapper : IWebView
{
    private readonly WebView2 _webView;

    public WebViewWrapper(WebView2 webView)
    {
        _webView = webView;
    }

    private Task<string> ExecuteScriptAsyncInternal(string jsCode)
    {
        if (_webView.InvokeRequired)
        {
            var tcs = new TaskCompletionSource<string>();
            _webView.Invoke(() =>
            {
                _webView.ExecuteScriptAsync(jsCode).ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        tcs.TrySetException(task.Exception);
                    }
                    else
                    {
                        tcs.TrySetResult(task.Result);
                    }
                });
            });
            return tcs.Task;
        }
        else
        {
            return _webView.ExecuteScriptAsync(jsCode);
        }
    }

    public Task ExecuteScriptAsync(string jsCode)
    {
        return ExecuteScriptAsyncInternal(jsCode);
    }

    public async Task<T?> ExecuteScriptAsync<T>(string jsCode)
    {
        // WebView2 возвращает результат в виде JSON-строки
        string jsonResult = await ExecuteScriptAsyncInternal(jsCode);

        if (string.IsNullOrEmpty(jsonResult) || jsonResult == "null")
        {
            return default;
        }

        // Важно: WebView2 возвращает строки в двойных кавычках (напр. "\"текст\"")
        return JsonSerializer.Deserialize<T>(jsonResult);
    }

    public void NavigateAsync(string url)
    {
        if (_webView.InvokeRequired)
        {
            _webView.Invoke(() =>
            {
                _webView.CoreWebView2.Navigate(url);
            });
        }
        else
        {
            _webView.CoreWebView2.Navigate(url);
        }
    }
    public void PostMessage(object data)
    {
        // Сериализуем и отправляем объект в JS
        var json = JsonSerializer.Serialize(data);
        if (_webView.InvokeRequired)
        {
            _webView.Invoke(() =>
            {
                _webView.CoreWebView2.PostWebMessageAsJson(json);
            });
        }
        else
        {
            _webView.CoreWebView2.PostWebMessageAsJson(json);
        }
    }
}