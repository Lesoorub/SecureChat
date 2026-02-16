using System.Text.Json.Serialization;
using Org.BouncyCastle.Tls;
using SecureChat.Core;
using SecureChat.Core.Attributes;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace SecureChat.Features.Chat.Components;

internal class ChatPanel
{
    private readonly ChatTab _tab;
    private readonly CurrentSession _currentSession;

    public ChatPanel(ChatTab tab, CurrentSession currentSession)
    {
        _tab = tab;
        _currentSession = currentSession;

        tab.RegisterNetCallback<Message>("msg", Process);
        tab.RegisterNetCallback<ConfirmMessage>("confirm_msg", Process);
        tab.RegisterNetCallback<UserConnected>("user_connected", Process);
    }

    public void PageLoaded()
    {
        _tab.Send(new UserConnected
        {
            Username = _currentSession.Username,
        });
        AppendSystemMessage("Вы подключились");
#if DEBUG
        AppendMessage(
            role: "user",
            text: "Не подтверженное",
            id: "123",
            status: "pending",
            senderName: "Я"
        );
        AppendMessage(
            role: $"user",
            text: $"Подтверженное",
            id: $"124",
            status: $"sent",
            senderName: $"Я"
        );
        AppendMessage(
            role: "user",
            text: "Ошибка",
            id: "124",
            status: "error",
            senderName: "Я"
        );
        AppendMessage(true, "Я", "Очень длинное сообщение. Очень длинное сообщение. Очень длинное сообщение. Очень длинное сообщение. Очень длинное сообщение. Очень длинное сообщение. Очень длинное сообщение. Очень длинное сообщение. Очень длинное сообщение. Очень длинное сообщение. Очень длинное сообщение. Очень длинное сообщение. Очень длинное сообщение. ");
        AppendMessage(false, "не Я", "Не мое сообщение");
#endif
    }

    internal void AppendSystemMessage(string text)
    {
        AppendMessage("system", text, string.Empty, string.Empty, string.Empty);
    }

    internal void AppendMessage(bool isMe, string who, string text)
    {
        AppendMessage(isMe ? "user" : "bot", text, Environment.TickCount64.ToString(), isMe ? "pending" : "sent", who);
    }

    internal void AppendMessage(string role, string text, string id, string status, string senderName)
    {
        _tab.PostMessage(new
        {
            action = "append_message",
            role = role,
            text = text,
            id = id,
            status = status,
            senderName = senderName
        });
    }

    public void SetMessageState(string msgId, string status)
    {
        _tab.ExecuteScript($"updateMessageStatus('{msgId}', '{status}')");
    }

    public class SendMessage
    {
        [JsonPropertyName("id")]
        public string MessageId { get; set; } = string.Empty;
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    [JsAction("send_message")]
    internal void Process(SendMessage request)
    {
        _tab.Send(new Message
        {
            MessageId = request.MessageId,
            Username = _currentSession.Username,
            Text = request.Text
        });
    }

    public class GetHistory
    {

    }

    [JsAction("get_history")]
    internal void Process(GetHistory _)
    {
        // 1. Получаете данные из БД или сервиса
        var history = new[] {
            new { role = "bot", text = "История загружена" },
            new { role = "user", text = "Отлично!" }
        };

        //// 2. Отправляете каждое сообщение в JS
        //foreach (var msg in history)
        //{
        //}
    }

    void Process(Message msg)
    {
        AppendMessage(false, msg.Username, msg.Text);
        _tab.Send(new ConfirmMessage
        {
            MessageId = msg.MessageId,
        });
    }

    void Process(ConfirmMessage confirmMsg)
    {
        SetMessageState(confirmMsg.MessageId, "sent");
    }

    void Process(UserConnected userConnected)
    {
        AppendSystemMessage($"Пользователь \"{userConnected.Username}\" подключился");
    }

    public class Message
    {
        public const string ACTION = "msg";

        [JsonPropertyName("action")]
        public string Action { get; set; } = ACTION;

        [JsonPropertyName("msg_id")]
        public string MessageId { get; set; } = string.Empty;

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    public class ConfirmMessage
    {
        public const string ACTION = "confirm_msg";

        [JsonPropertyName("action")]
        public string Action { get; set; } = ACTION;

        [JsonPropertyName("msg_id")]
        public string MessageId { get; set; } = string.Empty;
    }

    public class UserConnected
    {
        public const string ACTION = "user_connected";

        [JsonPropertyName("action")]
        public string Action { get; set; } = ACTION;

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;
    }
}
