using System.Text.Json.Serialization;

namespace SecureChat.Protocols.WebSockets.ServiceMessage;

public class MembersCountResponse
{
    public const string ACTION = "members_count";

    [JsonPropertyName("action")]
    public string? Action { get; set; } = ACTION;
    [JsonPropertyName("count")]
    public int Count { get; set; }
}