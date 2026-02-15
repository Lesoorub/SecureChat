using System.Buffers;
using System.Text;
using System.Text.Json;

namespace SecureChat.Core;

public readonly struct DecryptedMessage : IDisposable
{
    private readonly byte[] _buffer;
    public readonly int Length;

    // Предоставляем удобный доступ через Span
    public Span<byte> Data => _buffer.AsSpan(0, Length);

    public DecryptedMessage(byte[] buffer, int length)
    {
        _buffer = buffer;
        Length = length;
    }

    public void Dispose()
    {
        if (_buffer != null)
        {
            // Безопасность: затираем данные перед возвратом в пул
            Array.Clear(_buffer, 0, Length);
            ArrayPool<byte>.Shared.Return(_buffer);
        }
    }

    public T Deserialize<T>()
    {
        return JsonSerializer.Deserialize<T>(Data) ?? throw new Exception("Data is not json.");
    }

    public override string ToString() => Encoding.UTF8.GetString(Data);
}
