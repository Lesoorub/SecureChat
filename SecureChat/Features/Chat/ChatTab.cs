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
    private readonly IWebView _webView;
    private readonly CurrentSession _currentSession;

    [SubHandler] private readonly ChatPanel _chatPanel;
    [SubHandler] private readonly CallPanel _callPanel;

    public delegate void MsgReceivedCallback(MemoryStream message);
    private readonly Dictionary<string, MsgReceivedCallback> _receiveMesgCallbacks = new();

    public delegate void ServiceMsgCallback(JsonElement message);
    private readonly Dictionary<string, ServiceMsgCallback> _serviceMsgCallbacks = new();

    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public event Action<int>? MembersCount;

    public ChatTab(ILogger<ChatTab> logger, IWebView webView, CurrentSession currentSession)
        : base(logger)
    {
        _webView = webView;
        _currentSession = currentSession;
        _chatPanel = new ChatPanel(this, currentSession);
        _callPanel = new CallPanel(this, currentSession, _cancellationTokenSource.Token);

        RegisterServiceCallback(MembersCountResponse.ACTION, x => MembersCount?.Invoke(x.Deserialize<MembersCountResponse>()?.Count ?? 0));

        InitializeActions();
    }

    public void RegisterNetCallback<T>(string action, Action<T> callback)
    {
        _receiveMesgCallbacks[action] = stream =>
        {
            callback?.Invoke(JsonSerializer.Deserialize<T>(stream) ?? throw new Exception("Cannot deserialize net message"));
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
            _webView.NavigateAsync("https://app.localhost/pages/main/index.html");
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
        //Console.WriteLine($"[Receive]{doc.RootElement}");
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
        _webView.ExecuteScriptAsync(script);
    }

    internal void PostMessage(object data)
    {
        _webView.PostMessage(data);
    }

    internal Task Send<T>(T value) where T : notnull
    {
        var session = _currentSession.Session;
        if (session is null || !session.IsActive)
        {
            return Task.CompletedTask;
        }

        //Console.WriteLine($"[Send]{JsonSerializer.Serialize(value)}");
        return session.SendJsonEncodedAsync(value);
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();

        (_callPanel as IDisposable)?.Dispose();
        (_chatPanel as IDisposable)?.Dispose();
    }
}
