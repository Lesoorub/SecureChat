using Microsoft.IO;

namespace SecureChat.Core;

public interface IHasPayload
{
    RecyclableMemoryStream? Payload { get; set; }
}
