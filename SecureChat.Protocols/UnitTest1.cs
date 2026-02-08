using System.Text.Json.Serialization;

namespace SecureChat.Protocols.WebSockets;

public class ChatSecureMessage
{
    public const string TYPE = "secmsg";

    [JsonPropertyName("type")]
    public string Type { get; set; } = TYPE;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("msg")]
    public string Message { get; set; } = string.Empty;
}
public class ChatSystemMessage
{
    public const string TYPE = "sysmsg";

    [JsonPropertyName("type")]
    public string Type { get; set; } = TYPE;

    [JsonPropertyName("msg")]
    public string Message { get; set; } = string.Empty;
}
public class ChatUserConnectedMessage
{
    public const string TYPE = "user_connected";

    [JsonPropertyName("type")]
    public string Type { get; set; } = TYPE;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("msg")]
    public string Message { get; set; } = string.Empty;
}
public class ChatUserDisconnectedMessage
{
    public const string TYPE = "user_disconnected";

    [JsonPropertyName("type")]
    public string Type { get; set; } = TYPE;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("msg")]
    public string Message { get; set; } = string.Empty;
}
public class ChatAudioMessage
{
    public const string TYPE = "audio";

    [JsonPropertyName("type")]
    public string Type { get; set; } = TYPE;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("msg")]
    public string Message { get; set; } = string.Empty;
}
public class ChatFileMessage
{
    public const string TYPE = "file";

    [JsonPropertyName("type")]
    public string Type { get; set; } = TYPE;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("msg")]
    public string Message { get; set; } = string.Empty;
}