using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IO;

namespace SecureChat.Core;

public class ChatSession : IDisposable
{
    private readonly ClientWebSocket _ws;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly RecyclableMemoryStreamManager _streamManager;

    // Длина Nonce (12 байт) и Tag (16 байт) фиксированы для AES-GCM
    private const int NONCE_SIZE = 12;
    private const int TAG_SIZE = 16;

    public bool IsActive => _ws.State == WebSocketState.Open;

    private byte[] _sharedKey;

    public ChatSession(ClientWebSocket ws, byte[] sharedKey, RecyclableMemoryStreamManager manager)
    {
        _ws = ws;
        _sharedKey = sharedKey;
        _streamManager = manager;
    }

    public async Task SendJsonAsync<T>(T value) where T : notnull
    {
        // 1. Берем поток из пула
        using var stream = _streamManager.GetStream("SendJsonAsync");

        // 2. Сериализуем напрямую в UTF-8 поток
        using (var writer = new Utf8JsonWriter((Stream)stream))
        {
            JsonSerializer.Serialize(writer, value);
            writer.Flush(); // Обязательно сбрасываем буферы writer'а в поток
        }

        // 3. Получаем данные без копирования через GetReadOnlySequence()
        await SendAsync(stream, true);
    }

    private static void WriteInt32(RecyclableMemoryStream stream, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(stream.GetSpan(4), value);
        stream.Advance(4);
    }

    private static int ReadInt32(ReadOnlySequence<byte> sequence)
    {
        var reader = new SequenceReader<byte>(sequence);
        if (!reader.TryReadLittleEndian(out int value))
        {
            throw new InvalidOperationException();
        }
        return value;
    }

    private static int ReadInt32(RecyclableMemoryStream stream)
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(stream.GetSpan(4));
        stream.Advance(4);
        return value;
    }

    public async Task SendEncodedAsync<T>(T value, RecyclableMemoryStream? payload) where T : notnull
    {
        // 1. Сериализуем во временный поток
        using var plainStream = _streamManager.GetStream("SendEncodedAsync.1");
        using (var writer = new Utf8JsonWriter((Stream)plainStream))
        {
            JsonSerializer.Serialize(writer, value);
            writer.Flush();
        }
        if (payload is not null)
        {
            WriteInt32(plainStream, (int)payload.Length);
            payload.Position = 0;
            await payload.CopyToAsync(plainStream);
        }
        else
        {
            plainStream.Write(stackalloc byte[4]);
        }

        // 2. Шифруем данные из временного потока в новый
        using var encryptedStream = _streamManager.GetStream("SendEncodedAsync.2");
        plainStream.Position = 0;
        await EncryptToStream(plainStream, encryptedStream);

        // 3. Отправляем зашифрованную последовательность
        await SendAsync(encryptedStream, true);
    }

    async Task SendAsync(RecyclableMemoryStream stream, bool endOfMessage = true)
    {
        if (_ws.State != WebSocketState.Open) return;

        // RMS может состоять из множества блоков (сегментов)
        // GetReadOnlySequence() — самый быстрый способ получить доступ к ним без копирования
        var sequence = stream.GetReadOnlySequence();

        await SendAsync(sequence, endOfMessage);
    }

    async Task SendAsync(ReadOnlySequence<byte> sequence, bool endOfMessage = true)
    {
        if (_ws.State != WebSocketState.Open) return;

        await _sendLock.WaitAsync();
        try
        {
            if (_ws.State != WebSocketState.Open) return;

            // Если последовательность пуста
            if (sequence.IsEmpty)
            {
                return;
            }

            // Итерируемся по всем сегментам ReadOnlySequence
            var position = sequence.Start;
            while (sequence.TryGet(ref position, out ReadOnlyMemory<byte> memory))
            {
                // Проверяем, является ли текущий сегмент последним в этой последовательности
                bool isLastSegment = position.GetObject() == null;

                // Флаг конца сообщения для WS ставится только если это последний кусок 
                // И пользователь изначально передал endOfMessage = true
                bool finalEndOfMessage = isLastSegment && endOfMessage;

                await _ws.SendAsync(memory, WebSocketMessageType.Binary, finalEndOfMessage, _cts.Token);
            }
        }
        catch (Exception ex)
        {
            // Log.Error(ex);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<(JsonDocument json, RecyclableMemoryStream? payload)> ReceiveJsonAsync()
    {
        // 1. Получаем и расшифровываем всё сообщение целиком
        using var encryptedMessage = await ReceiveFullMessageAsStreamAsync();
        var plainStream = _streamManager.GetStream("ReceiveJsonAsync.Decrypted");

        await DecryptOptimized(encryptedMessage, plainStream);
        plainStream.Position = 0;

        return Parse();

        (JsonDocument json, RecyclableMemoryStream? payload) Parse()
        {
            try
            {
                var reader = new Utf8JsonReader(plainStream.GetReadOnlySequence());
                JsonDocument json = JsonDocument.ParseValue(ref reader);
                var jsonLen = reader.BytesConsumed;
                plainStream.Position = jsonLen;
                var payloadLen = ReadInt32(plainStream);

                RecyclableMemoryStream? payloadStream = null;
                if (payloadLen > 0)
                {
                    payloadStream = _streamManager.GetStream("ReceiveJsonAsync.Payload");
                    payloadStream.SetLength(payloadLen);
                    plainStream.Position = 0;
                    var buffer = plainStream.GetReadOnlySequence().Slice(4 + jsonLen, payloadLen);
                    foreach (var segment in buffer)
                    {
                        payloadStream.Write(segment.Span);
                    }
                    payloadStream.Position = 0;
                }

                return (json, payloadStream);
            }
            catch (Exception ex)
            {
                throw;
            }
        }
    }

    async Task<RecyclableMemoryStream> ReceiveFullMessageAsStreamAsync()
    {
        // 1. Берем поток из менеджера (вызывающий код ДОЛЖЕН сделать ему Dispose)
        var ms = _streamManager.GetStream();
        var buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);

        try
        {
            WebSocketReceiveResult result;
            do
            {
                result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);

                if (result.Count == 0 || result.MessageType == WebSocketMessageType.Close)
                {
                    ms.Dispose();
                    throw new WebSocketException("Соединение закрыто сервером");
                }

                // Записываем байты напрямую в поток
                await ms.WriteAsync(buffer.AsMemory(0, result.Count));

            } while (!result.EndOfMessage);

            // 2. Сбрасываем позицию в начало, чтобы поток был готов к чтению
            ms.Position = 0;
            return ms;
        }
        catch
        {
            ms.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private const int CHUNK_SIZE = 1024 * 1024; // 1 МБ на чанк

    public async Task EncryptToStream(Stream inputStream, Stream outputStream)
    {
        using var aes = new AesGcm(_sharedKey, TAG_SIZE);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(CHUNK_SIZE);
        byte[] cipherBuffer = ArrayPool<byte>.Shared.Rent(CHUNK_SIZE);
        byte[] nonce = new byte[NONCE_SIZE];
        byte[] tag = new byte[TAG_SIZE];

        try
        {
            int bytesRead;
            while ((bytesRead = await inputStream.ReadAsync(buffer, 0, CHUNK_SIZE)) > 0)
            {
                RandomNumberGenerator.Fill(nonce);

                // Шифруем текущий чанк
                aes.Encrypt(nonce, buffer.AsSpan(0, bytesRead), cipherBuffer.AsSpan(0, bytesRead), tag);

                // Записываем метаданные чанка
                await outputStream.WriteAsync(BitConverter.GetBytes(bytesRead), 0, 4);
                await outputStream.WriteAsync(nonce, 0, NONCE_SIZE);
                await outputStream.WriteAsync(tag, 0, TAG_SIZE);
                await outputStream.WriteAsync(cipherBuffer, 0, bytesRead);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            ArrayPool<byte>.Shared.Return(cipherBuffer);
        }
    }

    public async Task DecryptOptimized(Stream encryptedStream, Stream outputStream)
    {
        using var aes = new AesGcm(_sharedKey, TAG_SIZE);
        byte[] sizeBuffer = new byte[4];
        byte[] nonce = new byte[NONCE_SIZE];
        byte[] tag = new byte[TAG_SIZE];

        try
        {
            while (await encryptedStream.ReadAsync(sizeBuffer, 0, 4) == 4)
            {
                int chunkSize = BitConverter.ToInt32(sizeBuffer, 0);
                await encryptedStream.ReadAsync(nonce, 0, NONCE_SIZE);
                await encryptedStream.ReadAsync(tag, 0, TAG_SIZE);

                byte[] cipherText = ArrayPool<byte>.Shared.Rent(chunkSize);
                byte[] plainText = ArrayPool<byte>.Shared.Rent(chunkSize);

                try
                {
                    await encryptedStream.ReadAsync(cipherText, 0, chunkSize);

                    // Расшифровываем чанк
                    aes.Decrypt(nonce, cipherText.AsSpan(0, chunkSize), tag, plainText.AsSpan(0, chunkSize));

                    await outputStream.WriteAsync(plainText, 0, chunkSize);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(cipherText);
                    ArrayPool<byte>.Shared.Return(plainText);
                }
            }
        }
        catch (Exception ex)
        {
            throw new CryptographicException("Ошибка целостности файла или неверный ключ", ex);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _ws.Dispose();
        _cts.Dispose();
    }
}