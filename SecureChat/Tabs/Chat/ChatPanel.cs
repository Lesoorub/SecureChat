using System.Text.Json;
using System.Text.Json.Serialization;
using SecureChat.Core;

namespace SecureChat.Tabs.Chat;

internal class ChatPanel
{
    private readonly ChatTab _tab;
    private readonly CurrentSession _currentSession;

    public ChatPanel(ChatTab tab, CurrentSession currentSession)
    {
        _tab = tab;
        _currentSession = currentSession;

        tab.RegisterMessageReceivedCallback("msg", x => Process(x.Deserialize<Message>()));
        tab.RegisterMessageReceivedCallback("confirm_msg", x => Process(x.Deserialize<ConfirmMessage>()));
        tab.RegisterMessageReceivedCallback("user_connected", x => Process(x.Deserialize<UserConnected>()));

        tab.RegisterPostMsgCallback("send_message", x => ProcessSendMessage(x.Deserialize<SendMessage>() ?? throw new Exception("Failed to deserialize 'send_message' message")));
        tab.RegisterPostMsgCallback("get_history", x => ProcessGetHistory(x.Deserialize<GetHistory>() ?? throw new Exception("Failed to deserialize 'get_history' message")));
    }

    public void PageLoaded()
    {
        _tab.Send(new UserConnected
        {
            Username = _currentSession.Username,
        });
        AppendSystemMessage("Вы подключились");
        AppendMessage(true, "Я", "Мое сообщение");
        AppendMessage(false, "не Я", "Не мое сообщение");
    }

    internal void AppendSystemMessage(string text)
    {
        _tab.ExecuteScript($"appendMessage('system','{System.Web.HttpUtility.JavaScriptStringEncode(text)}')");
    }

    internal void AppendMessage(bool isMe, string who, string text)
    {
        _tab.ExecuteScript($"appendMessage(" +
            /*role*/$"'{(isMe ? "user" : "bot")}', " +
            /*text*/$"'{System.Web.HttpUtility.JavaScriptStringEncode(text)}', " +
            /*id*/$"'{Environment.TickCount64}', " +
            /*status*/$"'{(isMe ? "pending" : "sent")}', " +
            /*senderName*/$"'{System.Web.HttpUtility.JavaScriptStringEncode(who)}'" +
        $")");
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

    private void ProcessSendMessage(SendMessage request)
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

    private void ProcessGetHistory(GetHistory _)
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
