using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.WinForms;
using SecureChat.Core;
using SecureChat.Tabs.Chat;

namespace SecureChat.Tabs;
[Tab("/chat/index.html", typeof(Factory))]
internal partial class ChatTab : AbstractTab
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

    public delegate void MsgReceivedCallback(DecryptedMessage message);
    private readonly Dictionary<string, MsgReceivedCallback> _receiveMesgCallbacks = new();

    public delegate void PostMsgCallback(JsonElement message);
    private readonly Dictionary<string, PostMsgCallback> _postMsgCallbacks = new();

    public ChatTab(WebView2 webView, CurrentSession currentSession)
    {
        _webView = webView;
        _currentSession = currentSession;
        _chatPanel = new ChatPanel(this, currentSession);
        _callPanel = new CallPanel(this, currentSession);
    }

    public void RegisterMessageReceivedCallback(string action, MsgReceivedCallback callback)
    {
        _receiveMesgCallbacks[action] = callback;
    }

    public void RegisterPostMsgCallback(string action, PostMsgCallback callback)
    {
        _postMsgCallbacks[action] = callback;
    }

    public override void PageLoaded()
    {
        _chatPanel.PageLoaded();
        _callPanel.OnPageLoaded();
        Task.Run(ReceiveMessages);
    }

    private async Task ReceiveMessages()
    {
        var session = _currentSession.Session;
        if (session is null)
        {
            return;
        }
        while (session.IsActive)
        {
            using var packet = await session.ReceiveEncodedAsync();
            var msgBase = packet.Deserialize<MessageBase>();
            if (_receiveMesgCallbacks.TryGetValue(msgBase.Action, out var callback))
            {
                callback?.Invoke(packet);
            }
        }
        _webView.CoreWebView2.Navigate("https://app.localhost/main/index.html");
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

    internal void Send<T>(T value)
    {
        var session = _currentSession.Session;
        if (session is null || !session.IsActive)
        {
            return;
        }
        _ = session.SendEncodedAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));
    }

    public class MessageBase
    {
        [JsonPropertyName("action")]
        public string Action { get; set; } = string.Empty;
    }
}
