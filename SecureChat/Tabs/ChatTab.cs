using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.WinForms;
using Org.BouncyCastle.Asn1.Ocsp;
using SecureChat.Core;
using static SecureChat.Tabs.ChatTab;

namespace SecureChat.Tabs;
[Tab("/chat/index.html", typeof(Factory))]
internal class ChatTab : AbstractTab
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

    public ChatTab(WebView2 webView, CurrentSession currentSession)
    {
        _webView = webView;
        _currentSession = currentSession;
    }

    public override void PageLoaded()
    {
        Send(new UserConnected
        {
            Username = _currentSession.Username,
        });
        AppendSystemMessage("Вы подключились");
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
            switch (msgBase.Action)
            {
                case "msg":
                    var msg = packet.Deserialize<Message>();
                    AppendMessage(false, msg.Username, msg.Text);
                    Send(new ConfirmMessage
                    {
                        MessageId = msg.MessageId,
                    });
                    break;
                case "confirm_msg":
                    var confirmMsg = packet.Deserialize<ConfirmMessage>();
                    SetMessageState(confirmMsg.MessageId, "sent");
                    break;
                case "user_connected":
                    var userConnected = packet.Deserialize<UserConnected>();
                    AppendSystemMessage($"Пользователь \"{userConnected.Username}\" подключился");
                    break;
            }
        }
        AppendSystemMessage("Соединение оборвано");
    }

    private void AppendSystemMessage(string text)
    {
        _webView.Invoke(() =>
        {
            _webView.ExecuteScriptAsync($"appendMessage('system','{System.Web.HttpUtility.JavaScriptStringEncode(text)}')");
        });
    }

    private void AppendMessage(bool isMe, string who, string text)
    {
        _webView.Invoke(() =>
        {
            _webView.ExecuteScriptAsync($"appendMessage(" +
                /*role*/$"'{(isMe ? "user" : "bot")}', " +
                /*text*/$"'{System.Web.HttpUtility.JavaScriptStringEncode(text)}', " +
                /*id*/$"'{Environment.TickCount64}', " +
                /*status*/$"'{(isMe ? "pending" : "sent")}', " +
                /*senderName*/$"'{System.Web.HttpUtility.JavaScriptStringEncode(who)}'" +
            $")");
        });
    }

    public void SetMessageState(string msgId, string status)
    {
        _webView.Invoke(() =>
        {
            return _webView.ExecuteScriptAsync($"updateMessageStatus('{msgId}', '{status}')");
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
            switch (action)
            {
                default:
                    throw new Exception($"Unexpected action: {action}");

                case "send_message":
                    ProcessSendMessage(doc.RootElement.Deserialize<SendMessage>() ?? throw new Exception("Failed to deserialize 'send_message' message"));
                    break;

                case "get_history":
                    ProcessGetHistory(doc.RootElement.Deserialize<GetHistory>() ?? throw new Exception("Failed to deserialize 'get_history' message"));
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString());
        }
    }

    private void Send<T>(T value)
    {
        var session = _currentSession.Session;
        if (session is null || !session.IsActive)
        {
            return;
        }
        _ = session.SendEncodedAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));
    }

    public class SendMessage
    {
        [JsonPropertyName("id")]
        public string MessageId { get; set; } = string.Empty;
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    private void ProcessSendMessage(SendMessage request)
    {
        Send(new Message
        {
            MessageId = request.MessageId,
            Username = _currentSession.Username,
            Text = request.Text
        });
    }

    public class GetHistory
    {

    }

    private void ProcessGetHistory(GetHistory request)
    {
        // 1. Получаете данные из БД или сервиса
        var history = new[] {
            new { role = "bot", text = "История загружена" },
            new { role = "user", text = "Отлично!" }
        };

        // 2. Отправляете каждое сообщение в JS
        foreach (var msg in history)
        {
        }
    }

    public class MessageBase
    {
        [JsonPropertyName("action")]
        public string Action { get; set; } = string.Empty;
    }

    public class Message
    {
        [JsonPropertyName("action")]
        public string Action { get; set; } = "msg";

        [JsonPropertyName("msg_id")]
        public string MessageId { get; set; } = string.Empty;

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    public class ConfirmMessage
    {
        [JsonPropertyName("action")]
        public string Action { get; set; } = "confirm_msg";

        [JsonPropertyName("msg_id")]
        public string MessageId { get; set; } = string.Empty;
    }

    public class UserConnected
    {
        [JsonPropertyName("action")]
        public string Action { get; set; } = "user_connected";

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;
    }
}
