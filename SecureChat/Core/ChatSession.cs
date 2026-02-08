using System.Buffers;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IO;

namespace SecureChat.Core;

public class ChatSession : IDisposable
{
    private readonly ClientWebSocket _ws;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private static readonly RecyclableMemoryStreamManager s_streamManager = new RecyclableMemoryStreamManager();

    // Длина Nonce (12 байт) и Tag (16 байт) фиксированы для AES-GCM
    private const int NONCE_SIZE = 12;
    private const int TAG_SIZE = 16;

    public bool IsActive => _ws.State == WebSocketState.Open;

    private byte[] _sharedKey;

    public ChatSession(ClientWebSocket ws, byte[] sharedKey)
    {
        _ws = ws;
        _sharedKey = sharedKey;
    }

    public async Task SendEncodedAsync(ArraySegment<byte> plainData)
    {
        byte[] key = _sharedKey;

        if (_ws.State != WebSocketState.Open) return;

        // 1. Шифруем данные, используя ArrayPool
        var (buffer, length) = EncryptOptimized(plainData, key);

        try
        {
            // 2. Используем существующий метод для безопасной отправки
            // Передаем ArraySegment, ограниченный реальной длиной зашифрованных данных
            await SendAsync(new ArraySegment<byte>(buffer, 0, length), true);
        }
        finally
        {
            // 3. ОЧИСТКА: затираем данные в буфере перед возвратом в пул (защита памяти)
            Array.Clear(buffer, 0, length);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task<DecryptedMessage> ReceiveEncodedAsync()
    {
        byte[] key = _sharedKey;
        // 1. Получаем зашифрованные данные из сокета
        // Используем ваш менеджер стримов для накопления полного сообщения
        using var ms = s_streamManager.GetStream();
        var tempBuffer = ArrayPool<byte>.Shared.Rent(32 * 1024);

        try
        {
            // Получаем сырые данные (внутри вашего ReceiveFullMessageAsync)
            var encryptedSegment = await ReceiveFullMessageAsync(ms, tempBuffer);

            // 2. Расшифровываем
            var (buffer, length) = DecryptOptimized(encryptedSegment, key);
            return new DecryptedMessage(buffer, length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(tempBuffer);
            // Стрим закроется и вернется в пул благодаря using
        }
    }

    public async Task SendAsync(ArraySegment<byte> data, bool endOfMessage = true)
    {
        if (_ws.State != WebSocketState.Open) return;

        await _sendLock.WaitAsync();
        try
        {
            if (_ws.State == WebSocketState.Open)
            {
                await _ws.SendAsync(data, WebSocketMessageType.Binary, endOfMessage, _cts.Token);
            }
        }
        catch { /* Логируем или игнорируем ошибки конкретного сокета */ }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<byte[]> ReceiveFullMessageManagedAsync()
    {
        var ms = s_streamManager.GetStream();
        var buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);
        try
        {
            return (await ReceiveFullMessageAsync(ms, buffer)).ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task<ArraySegment<byte>> ReceiveFullMessageAsync(MemoryStream ms, byte[] buffer)
    {
        ms.SetLength(0);
        WebSocketReceiveResult result;
        do
        {
            result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("Соединение закрыто сервером");

            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        ms.Position = 0;
        return ms.TryGetBuffer(out var segment) ? segment : new ArraySegment<byte>(ms.ToArray());
    }

    // Возвращаем IMemoryOwner или кастомную структуру, чтобы вызывающий код мог вернуть массив в пул
    public static (byte[] Buffer, int Length) EncryptOptimized(ArraySegment<byte> data, byte[] key)
    {
        int requiredLength = NONCE_SIZE + TAG_SIZE + data.Count;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(requiredLength);

        using (var aes = new AesGcm(key, TAG_SIZE))
        {
            var nonceSpan = buffer.AsSpan(0, NONCE_SIZE);
            var tagSpan = buffer.AsSpan(NONCE_SIZE, TAG_SIZE);
            var cipherSpan = buffer.AsSpan(NONCE_SIZE + TAG_SIZE, data.Count);

            // Заполняем Nonce напрямую в целевой буфер
            RandomNumberGenerator.Fill(nonceSpan);

            aes.Encrypt(nonceSpan, data.AsSpan(), cipherSpan, tagSpan);
        }

        return (buffer, requiredLength);
    }

    public static (byte[] Buffer, int Length) DecryptOptimized(ArraySegment<byte> encryptedData, byte[] key)
    {
        if (encryptedData.Count < NONCE_SIZE + TAG_SIZE)
            throw new CryptographicException("Сообщение слишком короткое");

        int plaintextLength = encryptedData.Count - NONCE_SIZE - TAG_SIZE;
        byte[] resultBuffer = ArrayPool<byte>.Shared.Rent(plaintextLength);

        using (var aes = new AesGcm(key, TAG_SIZE))
        {
            var span = encryptedData.AsSpan();
            var nonceSpan = span.Slice(0, NONCE_SIZE);
            var tagSpan = span.Slice(NONCE_SIZE, TAG_SIZE);
            var cipherSpan = span.Slice(NONCE_SIZE + TAG_SIZE, plaintextLength);

            // Расшифровка напрямую в буфер из пула
            aes.Decrypt(nonceSpan, cipherSpan, tagSpan, resultBuffer.AsSpan(0, plaintextLength));
        }

        return (resultBuffer, plaintextLength);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _ws.Dispose();
        _cts.Dispose();
    }
}
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