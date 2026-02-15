using System.Text.Json.Serialization;

namespace SecureChat.Protocols.WebSockets.ServiceMessage;

public class MembersCountRequest
{
    public const string ACTION = "members_count";

    [JsonPropertyName("action")]
    public string? Action { get; set; } = ACTION;
}
