using System.Collections.Concurrent;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Microsoft.IO;
using SecureChat.Core;
using SecureChat.Core.Attributes;

namespace SecureChat.Features.Chat.Components;

internal class ChatPanel
{
    private string CustomDownloadFolderName = "SecureChatDownloads";

    private readonly ChatTab _tab;
    private readonly CurrentSession _currentSession;
    private readonly RecyclableMemoryStreamManager _streamManager;

    private readonly ConcurrentDictionary<string, FileInfo> _uploadedFiles = new();

    public ChatPanel(ChatTab tab, CurrentSession currentSession, RecyclableMemoryStreamManager manager)
    {
        _tab = tab;
        _currentSession = currentSession;
        _streamManager = manager;

        tab.RegisterNetCallback<Message>("msg", Process);
        tab.RegisterNetCallback<ConfirmMessage>("confirm_msg", Process);
        tab.RegisterNetCallback<UserConnected>("user_connected", Process);
        tab.RegisterNetCallback<WhereAreYou>(WhereAreYou.ACTION, Process);
        tab.RegisterNetCallback<VoicePing>(VoicePing.ACTION, Process);
        tab.RegisterNetCallback<LoadFileRequest>(LoadFileRequest.ACTION, Process);
        tab.RegisterNetCallback<LoadFileResponse>(LoadFileResponse.ACTION, Process);
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

    internal void AppendMessage(bool isMe, string who, string text, Attachment? attachment = null)
    {
        AppendMessage(isMe ? "user" : "bot", text, Environment.TickCount64.ToString(), isMe ? "pending" : "sent", who, attachment);
    }

    internal void AppendMessage(string role, string text, string id, string status, string senderName, Attachment? imageUrl = null)
    {
        _tab.PostMessage(new
        {
            action = "append_message",
            role = role,
            text = text,
            id = id,
            status = status,
            senderName = senderName,
            imageUrl = imageUrl
        });
    }

    public void SetMessageState(string msgId, string status)
    {
        _tab.ExecuteScript($"updateMessageStatus('{msgId}', '{status}')");
    }

    public class SendMessage
    {
        [JsonPropertyName("id")]
        public string? MessageId { get; set; }
        [JsonPropertyName("text")]
        public string? Text { get; set; }
        [JsonPropertyName("attachment")]
        public Attachment? Attachment { get; set; }
    }

    public class Attachment
    {
        [JsonPropertyName("data")]
        public string? Data { get; set; }
        [JsonPropertyName("type")]
        public string? Type { get; set; }
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    [JsAction("send_message")]
    internal void Process(SendMessage request)
    {
        if (string.IsNullOrWhiteSpace(request.MessageId) ||
            string.IsNullOrWhiteSpace(_currentSession.Username) ||
            (string.IsNullOrWhiteSpace(request.Text) && request.Attachment?.Data is null))
        {
            return;
        }

        _tab.Send(new Message
        {
            MessageId = request.MessageId,
            Username = _currentSession.Username,
            Text = request.Text,
            Attachment = request.Attachment
        });
    }

    public class GetHistory
    {

    }

    [JsAction("get_history")]
    internal void Process(GetHistory request)
    {
    }

    [JsAction("voice_ping")]
    internal void ProcessVoicePing(VoicePing request)
    {
        PlayVoicePing();
        _tab.Send(new VoicePing());
    }

    [JsAction("where_are_you")]
    internal void ProcessWhereAreYou(WhereAreYou request)
    {
        _tab.Send(new WhereAreYou());
    }

    internal record OpenFileDialogJsAction();

    [JsAction("open_file_dialog")]
    internal void ProcessOpenSelectFileDialog(OpenFileDialogJsAction request)
    {
        Task.Run(async () =>
        {
            FileInfo? fileInfo = await _tab.WebView.OpenFileDialog();
            if (fileInfo is null)
            {
                return;
            }
            var path = fileInfo.FullName;
            bool isImage = path.EndsWith(".png") || path.EndsWith(".jpg") || path.EndsWith(".raw") || path.EndsWith(".webp") || path.EndsWith(".bmp");
            if (isImage)
            {
                using var stream = _streamManager.GetStream();
                using var fs = fileInfo.OpenRead();
                await fs.CopyToAsync(stream);

                _tab.PostMessage(new
                {
                    action = "set_attachment",
                    base64Data = Convert.ToBase64String(stream.GetBuffer()),
                    fileName = Path.GetFileNameWithoutExtension(path),
                    isImage = true,
                });
            }
            else
            {
                var fileId = Guid.NewGuid().ToString();
                _uploadedFiles[fileId] = fileInfo;
                _tab.PostMessage(new
                {
                    action = "set_attachment",
                    base64Data = fileId,
                    fileName = Path.GetFileNameWithoutExtension(path),
                    isImage = false,
                });
            }
        });
    }

    internal record TryOpenLoadedFile([property:JsonPropertyName("fileName")] string FileName);

    /// <summary>
    /// Пытаемся открыть файл.
    /// </summary>
    /// <param name="request"></param>
    [JsAction("try_open_loaded_file")]
    internal void ProcessSaveFileDialogJsAction(TryOpenLoadedFile request)
    {
        if (request.FileName is null)
        {
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string downloadsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                CustomDownloadFolderName
            );
            Directory.CreateDirectory(downloadsPath);

            string filePath = Path.Combine(downloadsPath, request.FileName);

            if (!File.Exists(filePath))
            {
                MessageBox.Show("Файл не найден");
                return;
            }

            string argument = $"/select,\"{filePath}\"";
            System.Diagnostics.Process.Start("explorer.exe", argument);
        }
        else
        {
            MessageBox.Show("Такое не поддерживается");
        }
    }

    void Process(Message msg)
    {
        if (msg.Username is null || msg.Text is null)
        {
            return;
        }

        PlayNewMessage();
        AppendMessage(false, msg.Username, msg.Text, attachment: msg.Attachment);
        _tab.Send(new ConfirmMessage
        {
            MessageId = msg.MessageId,
        });
        if (msg.Attachment is not null && msg.Attachment.Data is not null && !msg.Attachment.Data.StartsWith("data:image/png;base64,"))
        {
            _tab.Send(new LoadFileRequest()
            {
                FileId = msg.Attachment.Data,
            });
        }
    }

    void Process(ConfirmMessage confirmMsg)
    {
        if (confirmMsg.MessageId is null)
        {
            return;
        }

        SetMessageState(confirmMsg.MessageId, "sent");
    }

    void Process(UserConnected userConnected)
    {
        AppendSystemMessage($"Пользователь \"{userConnected.Username}\" подключился");
    }

    void Process(WhereAreYou request)
    {
        Task.Run(() =>
        {
            MessageBox.Show("Ты тут?");
        });
    }

    void Process(VoicePing request)
    {
        PlayVoicePing();
    }

    void Process(LoadFileRequest request)
    {
        Task.Run(async () =>
        {
            if (request.FileId is not null && _uploadedFiles.TryGetValue(request.FileId, out var fileInfo))
            {
                using var stream = _streamManager.GetStream();
                using var fs = fileInfo.OpenRead();
                await fs.CopyToAsync(stream);
                await _tab.Send(new LoadFileResponse()
                {
                    FileName = fileInfo.Name,
                    Payload = stream
                });
            }
            else
            {
                await _tab.Send(new LoadFileResponse()
                {
                    Payload = null,
                });
            }
        });
    }

    async Task Process(LoadFileResponse request)
    {
        if (request.Payload is null || request.FileName is null)
        {
            MessageBox.Show("Ошибка при загрузке файла");
            return;
        }
        string downloadsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            CustomDownloadFolderName
        );
        Directory.CreateDirectory(downloadsPath);

        string filePath = Path.Combine(downloadsPath, request.FileName.Replace("/", string.Empty));

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        using var fs = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write);
        await request.Payload.CopyToAsync(fs);
        await fs.FlushAsync();
        fs.Close();
        // Файл получен.
    }

    void PlayNewMessage()
    {
        using (SoundPlayer player = new SoundPlayer(Path.Combine(Directory.GetCurrentDirectory(), @"wwwroot/assets/delivered-message-sound.wav")))
        {
            player.Play(); // Асинхронное воспроизведение
        }
    }

    void PlayVoicePing()
    {
        using (SoundPlayer player = new SoundPlayer(Path.Combine(Directory.GetCurrentDirectory(), @"wwwroot/assets/calm-sound-of-the-appearance-in-the-system.wav")))
        {
            player.Play(); // Асинхронное воспроизведение
        }
    }

    public class Message
    {
        public const string ACTION = "msg";

        [JsonPropertyName("action")]
        public string Action { get; set; } = ACTION;

        [JsonPropertyName("msg_id")]
        public string? MessageId { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("attachment")]
        public Attachment? Attachment { get; set; }
    }

    public class ConfirmMessage
    {
        public const string ACTION = "confirm_msg";

        [JsonPropertyName("action")]
        public string Action { get; set; } = ACTION;

        [JsonPropertyName("msg_id")]
        public string? MessageId { get; set; }
    }

    public class UserConnected
    {
        public const string ACTION = "user_connected";

        [JsonPropertyName("action")]
        public string Action { get; set; } = ACTION;

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;
    }

    public class WhereAreYou
    {
        public const string ACTION = "where_are_you";

        [JsonPropertyName("action")]
        public string Action { get; set; } = ACTION;
    }

    public class VoicePing
    {
        public const string ACTION = "voice_ping";

        [JsonPropertyName("action")]
        public string Action { get; set; } = ACTION;
    }

    public class LoadFileRequest
    {
        public const string ACTION = "load_file_request";

        [JsonPropertyName("action")]
        public string Action { get; set; } = ACTION;

        [JsonPropertyName("file_id")]
        public string? FileId { get; set; }
    }

    public class LoadFileResponse : IHasPayload
    {
        public const string ACTION = "load_file_response";

        [JsonPropertyName("action")]
        public string Action { get; set; } = ACTION;

        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }

        [JsonIgnore]
        public RecyclableMemoryStream? Payload { get; set; }
    }
}
