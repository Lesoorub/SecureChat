using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;
using SecureChat.Core.Interfaces;

namespace SecureChat.Infrastructure.WebView;

public class WebViewWrapper : IWebView
{
    private readonly WebView2 _webView;
    private readonly OpenFileDialog _openFileDialog;
    private readonly SaveFileDialog _saveFileDialog;

    public WebViewWrapper(WebView2 webView, OpenFileDialog openFileDialog, SaveFileDialog saveFileDialog)
    {
        _webView = webView;
        _openFileDialog = openFileDialog;
        _saveFileDialog = saveFileDialog;

        _openFileDialog.Filter = "All files (*.*)|*.*";
        _openFileDialog.InitialDirectory = Directory.GetCurrentDirectory();

        _saveFileDialog.InitialDirectory = Directory.GetCurrentDirectory();
    }

    public Task<FileInfo?> SaveFileDialog()
    {
        var tcs = new TaskCompletionSource<FileInfo?>();
        _webView.Invoke(() =>
        {
            if (_saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                var fi = new FileInfo(_saveFileDialog.FileName);
                if (fi.Directory is not null)
                {
                    _saveFileDialog.InitialDirectory = fi.Directory.FullName;
                }
                tcs.TrySetResult(fi);
            }
            else
            {
                tcs.TrySetResult(null);
            }
        });
        return tcs.Task;
    }

    public Task<FileInfo?> OpenFileDialog()
    {
        var tcs = new TaskCompletionSource<FileInfo?>();
        _webView.Invoke(() =>
        {
            if (_openFileDialog.ShowDialog() == DialogResult.OK)
            {
                var fi = new FileInfo(_openFileDialog.FileName);
                if (fi.Directory is not null)
                {
                    _openFileDialog.InitialDirectory = fi.Directory.FullName;
                }
                tcs.TrySetResult(fi);
            }
            else
            {
                tcs.TrySetResult(null);
            }
        });
        return tcs.Task;
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