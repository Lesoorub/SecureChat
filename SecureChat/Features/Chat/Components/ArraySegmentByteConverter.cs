using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecureChat.Features.Chat.Components;

public class ArraySegmentByteConverter : JsonConverter<ArraySegment<byte>>
{
    public override ArraySegment<byte> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // System.Text.Json serializes byte[] as Base64 strings by default.
        // We read the Base64 string and convert it back to a byte array.
        if (reader.TokenType == JsonTokenType.String)
        {
            byte[] byteArray = reader.GetBytesFromBase64();
            return new ArraySegment<byte>(byteArray);
        }

        // Handle other cases (e.g., if it was manually serialized as a JSON array of numbers)
        // by throwing an exception or implementing custom logic.
        throw new JsonException("Expected Base64 string for ArraySegment<byte>.");
    }

    public override void Write(Utf8JsonWriter writer, ArraySegment<byte> value, JsonSerializerOptions options)
    {
        // Write the ArraySegment<byte> value to the writer as a Base64 string.
        // This is the standard way System.Text.Json handles binary data.
        writer.WriteBase64StringValue(value.AsSpan());
    }
}
