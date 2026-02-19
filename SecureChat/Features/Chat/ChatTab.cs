using System.Buffers;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using SecureChat.Core;
using SecureChat.Core.Attributes;
using SecureChat.Core.Interfaces;
using SecureChat.Features.Chat.Components;
using SecureChat.Protocols.WebSockets.ServiceMessage;
using SecureChat.UI.Base;

namespace SecureChat.Features.Chat;
[Page("/pages/chat/index.html")]
internal partial class ChatTab : AbstractPage, IDisposable
{
    public readonly IWebView WebView;
    private readonly CurrentSession _currentSession;

    [SubHandler] private readonly ChatPanel _chatPanel;
    [SubHandler] private readonly CallPanel _callPanel;

    public delegate ValueTask MsgReceivedCallback(JsonElement message, RecyclableMemoryStream? payload);
    private readonly Dictionary<string, MsgReceivedCallback> _receiveMsgCallbacks = new();

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly RecyclableMemoryStreamManager _streamManager;

    public ChatTab(ILogger<ChatTab> logger, IWebView webView, CurrentSession currentSession, RecyclableMemoryStreamManager manager)
        : base(logger)
    {
        WebView = webView;
        _currentSession = currentSession;
        _streamManager = manager;

        _chatPanel = new ChatPanel(this, currentSession, manager);
        _callPanel = new CallPanel(this, currentSession, _cancellationTokenSource.Token);

        InitializeActions();
    }

    public void RegisterNetCallback<T>(string action, Func<T, Task> callback)
    {
        _receiveMsgCallbacks[action] = (jsonElement, payload) =>
        {
            var value = jsonElement.Deserialize<T>() ?? throw new Exception("Cannot deserialize net message");
            if (payload is not null && value is IHasPayload hasPayload)
            {
                hasPayload.Payload = payload;
            }
            return new ValueTask(callback.Invoke(value));
        };
    }

    public void RegisterNetCallback<T>(string action, Action<T> callback)
    {
        _receiveMsgCallbacks[action] = (jsonElement, payload) =>
        {
            var value = jsonElement.Deserialize<T>() ?? throw new Exception("Cannot deserialize net message");
            if (payload is not null && value is IHasPayload hasPayload)
            {
                hasPayload.Payload = payload;
            }
            callback.Invoke(value);
            return ValueTask.CompletedTask;
        };
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
                    var (json, payload) = await session.ReceiveJsonAsync();
                    if (json is null)
                    {
                        continue;
                    }
                    _ = Task.Run(async () =>
                    {
                        using var doc = json;
                        using var b = payload;
                        //Console.WriteLine($"[Receive]{doc.RootElement}");
                        if (json.RootElement.TryGetProperty("action"u8, out var actionProp) &&
                            actionProp.ValueKind == JsonValueKind.String)
                        {
                            var action = actionProp.GetString();
                            if (action is not null && _receiveMsgCallbacks.TryGetValue(action, out var callback) && callback is not null)
                            {
                                await callback.Invoke(doc.RootElement, payload);
                            }
                        }
                    });
                }
                catch
                {
                    await Task.Delay(100);
                }
            }
            WebView.NavigateAsync("https://app.localhost/pages/main/index.html");
        }
        finally
        {
            session.Dispose();
        }
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
        WebView.ExecuteScriptAsync(script);
    }

    internal void PostMessage(object data)
    {
        WebView.PostMessage(data);
    }

    internal Task Send<T>(T value) where T : notnull
    {
        var session = _currentSession.Session;
        if (session is null || !session.IsActive)
        {
            return Task.CompletedTask;
        }

        if (value is IHasPayload hasPayload)
        {
            return session.SendEncodedAsync(value, hasPayload.Payload);
        }

        //Console.WriteLine($"[Send]{JsonSerializer.Serialize(value)}");
        return session.SendEncodedAsync(value, null);
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();

        (_callPanel as IDisposable)?.Dispose();
        (_chatPanel as IDisposable)?.Dispose();
    }
}
