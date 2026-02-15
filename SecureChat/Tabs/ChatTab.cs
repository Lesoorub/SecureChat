using System.Buffers;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IO;
using Microsoft.Web.WebView2.WinForms;
using SecureChat.Core;
using SecureChat.Protocols.WebSockets.ServiceMessage;
using SecureChat.Tabs.Chat;

namespace SecureChat.Tabs;
[Tab("/chat/index.html", typeof(Factory))]
internal partial class ChatTab : AbstractTab, IDisposable
{
    public class Factory : ITabFactory
    {
        public AbstractTab Create(WebView2 webView, ServiceProvider serviceProvider)
        {
            return new ChatTab(webView, serviceProvider.GetRequiredService<CurrentSession>());
        }
    }

    private readonly WebView2 _webView;
    private readonly CurrentSession _currentSession;

    private readonly ChatPanel _chatPanel;
    private readonly CallPanel _callPanel;

    public delegate void MsgReceivedCallback(MemoryStream message);
    private readonly Dictionary<string, MsgReceivedCallback> _receiveMesgCallbacks = new();

    public delegate void PostMsgCallback(JsonElement message);
    private readonly Dictionary<string, PostMsgCallback> _postMsgCallbacks = new();

    public delegate void ServiceMsgCallback(JsonElement message);
    private readonly Dictionary<string, ServiceMsgCallback> _serviceMsgCallbacks = new();

    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public event Action<int>? MembersCount;

    public ChatTab(WebView2 webView, CurrentSession currentSession)
    {
        _webView = webView;
        _currentSession = currentSession;
        _chatPanel = new ChatPanel(this, currentSession);
        _callPanel = new CallPanel(this, currentSession, _cancellationTokenSource.Token);

        RegisterServiceCallback(MembersCountResponse.ACTION, x => MembersCount?.Invoke(x.Deserialize<MembersCountResponse>()?.Count ?? 0));
    }

    public void RegisterNetCallback(string action, MsgReceivedCallback callback)
    {
        _receiveMesgCallbacks[action] = callback;
    }

    public void RegisterNetCallback<T>(string action, Action<T> callback)
    {
        _receiveMesgCallbacks[action] = stream =>
        {
            callback?.Invoke(JsonSerializer.Deserialize<T>(stream) ?? throw new Exception("Cannot deserialize net message"));
        };
    }

    public void RegisterUiCallback(string action, PostMsgCallback callback)
    {
        _postMsgCallbacks[action] = callback;
    }

    public void RegisterUiCallback<T>(string action, Action<T> callback)
    {
        _postMsgCallbacks[action] = stream =>
        {
            callback?.Invoke(JsonSerializer.Deserialize<T>(stream) ?? throw new Exception("Cannot deserialize ui message"));
        };
    }

    public void RegisterServiceCallback(string action, ServiceMsgCallback callback)
    {
        _serviceMsgCallbacks[action] = callback;
    }

    public override void PageLoaded()
    {
        _chatPanel.PageLoaded();
        _callPanel.OnPageLoaded();
        Task.Run(ReceiveMessages);
    }

    public void RequestRoomMembersCount()
    {
        Send(new MembersCountRequest());
    }

    private async Task ReceiveMessages()
    {
        var session = _currentSession.Session;
        if (session is null)
        {
            return;
        }
        try
        {
            while (session.IsActive)
            {
                try
                {
                    using var rawMsgStream = await session.ReceiveFullMessageAsStreamAsync();
                    if (TryReadServiceMessage(rawMsgStream))
                    {
                        continue;
                    }
                    ReadEncryptedMessage(session, rawMsgStream);
                }
                catch
                {
                    await Task.Delay(100);
                }
            }
            _webView.CoreWebView2.Navigate("https://app.localhost/main/index.html");
        }
        finally
        {
            session.Dispose();
        }
    }

    private void ReadEncryptedMessage(ChatSession session, RecyclableMemoryStream rawMsgStream)
    {
        using var decryptedMsgStream = session.DecryptOptimized(rawMsgStream);
        if (!IsJson(decryptedMsgStream))
        {
            return;
        }
        using var doc = JsonDocument.Parse(decryptedMsgStream.GetReadOnlySequence());

        if (doc.RootElement.TryGetProperty("action"u8, out var actionProp) &&
            actionProp.ValueKind == JsonValueKind.String)
        {
            var action = actionProp.GetString();
            if (action is not null && _receiveMesgCallbacks.TryGetValue(action, out var callback))
            {
                callback?.Invoke(decryptedMsgStream);
            }
        }
    }

    private bool TryReadServiceMessage(RecyclableMemoryStream ms)
    {
        if (ms.Length > 256 || ms.Length <= 2)
        {
            return false;
        }

        try
        {
            if (!IsJson(ms))
            {
                return false;
            }

            // Parse принимает ReadOnlySequence напрямую — это максимально быстро
            using var doc = JsonDocument.Parse(ms.GetReadOnlySequence());

            if (doc.RootElement.TryGetProperty("action"u8, out var actionProp) &&
                actionProp.ValueKind == JsonValueKind.String)
            {
                var action = actionProp.GetString();
                if (action != null && _serviceMsgCallbacks.TryGetValue(action, out var callback))
                {
                    callback?.Invoke(doc.RootElement.Clone()); // Клонируем, если callback использует данные после Dispose doc
                    return true;
                }
            }
        }
        catch
        {
            // Игнорируем ошибки парсинга, возвращаем false
        }

        return false;
    }

    public bool IsJson(RecyclableMemoryStream ms)
    {
        var sequence = ms.GetReadOnlySequence();
        if (sequence.IsSingleSegment)
        {
            var span = sequence.FirstSpan;
            return span.StartsWith("{"u8) && span.EndsWith("}"u8);
        }
        else
        {
            return IsJson(sequence);
        }
    }

    public bool IsJson(ReadOnlySequence<byte> sequence)
    {
        if (sequence.IsEmpty) return false;

        // Первый байт первого сегмента
        byte first = sequence.First.Span[0];

        // Универсальный способ для последнего байта:
        byte universalLast = sequence.Slice(sequence.GetPosition(sequence.Length - 1)).First.Span[0];

        return first == (byte)'{' && universalLast == (byte)'}';
    }

    internal void ExecuteScript(string script)
    {
        _webView.Invoke(() =>
        {
            _webView.ExecuteScriptAsync(script);
        });
    }

    public override void ProcessPostMessage(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("action", out var actionProp) || actionProp.ValueKind != JsonValueKind.String)
            {
                return;
            }

            var action = actionProp.GetString();
            if (action is not null && _postMsgCallbacks.TryGetValue(action, out var callback))
            {
                callback?.Invoke(doc.RootElement);
            }
            else
            {
                throw new Exception($"Unexpected action: {action}");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString());
        }
    }

    internal Task Send<T>(T value) where T : notnull
    {
        var session = _currentSession.Session;
        if (session is null || !session.IsActive)
        {
            return Task.CompletedTask;
        }

        return session.SendJsonEncodedAsync(value);
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
    }
}
