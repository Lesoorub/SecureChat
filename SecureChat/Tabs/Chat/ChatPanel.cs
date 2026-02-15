using System.Text.Json;
using System.Text.Json.Serialization;
using SecureChat.Core;
using static System.Net.Mime.MediaTypeNames;

namespace SecureChat.Tabs.Chat;

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

        tab.RegisterUiCallback<SendMessage>("send_message", Process);
        tab.RegisterUiCallback<GetHistory>("get_history", Process);
    }

    public void PageLoaded()
    {
        _tab.Send(new UserConnected
        {
            Username = _currentSession.Username,
        });
        AppendSystemMessage("Вы подключились");
        _tab.ExecuteScript($"appendMessage(" +
            /*role*/$"'user', " +
            /*text*/$"'Не подтверженное', " +
            /*id*/$"'123', " +
            /*status*/$"'pending', " +
            /*senderName*/$"'Я'" +
        $")");
        _tab.ExecuteScript($"appendMessage(" +
            /*role*/$"'user', " +
            /*text*/$"'Подтверженное', " +
            /*id*/$"'124', " +
            /*status*/$"'sent', " +
            /*senderName*/$"'Я'" +
        $")");
        _tab.ExecuteScript($"appendMessage(" +
            /*role*/$"'user', " +
            /*text*/$"'Ошибка', " +
            /*id*/$"'124', " +
            /*status*/$"'error', " +
            /*senderName*/$"'Я'" +
        $")");
        AppendMessage(true, "Я", "Очень длинное сообщение. Очень длинное сообщение. Очень длинное сообщение. Очень длинное сообщение. Очень длинное сообщение. Очень длинное сообщение. Очень длинное сообщение. Очень длинное сообщение. Очень длинное сообщение. Очень длинное сообщение. Очень длинное сообщение. Очень длинное сообщение. Очень длинное сообщение. ");
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

    private void Process(SendMessage request)
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

    private void Process(GetHistory _)
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
