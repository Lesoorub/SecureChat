using System.Buffers;
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
    private static readonly RecyclableMemoryStreamManager s_streamManager = new();

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

    public async Task SendJsonAsync<T>(T value) where T : notnull
    {
        // 1. Берем поток из пула
        using var stream = s_streamManager.GetStream();

        // 2. Сериализуем напрямую в UTF-8 поток
        using (var writer = new Utf8JsonWriter((Stream)stream))
        {
            JsonSerializer.Serialize(writer, value);
            writer.Flush(); // Обязательно сбрасываем буферы writer'а в поток
        }

        // 3. Получаем данные без копирования через GetReadOnlySequence()
        await SendAsync(stream, true);
    }

    public async Task SendEncodedAsync(RecyclableMemoryStream plainStream)
    {
        // 2. Шифруем данные из временного потока в новый
        var plainData = new ArraySegment<byte>(plainStream.GetBuffer(), 0, (int)plainStream.Length);
        using var encryptedStream = EncryptToStream(plainData);

        // 3. Отправляем зашифрованную последовательность
        await SendAsync(encryptedStream, true);
    }

    public async Task SendJsonEncodedAsync<T>(T value) where T : notnull
    {
        // 1. Сериализуем во временный поток
        using var plainStream = s_streamManager.GetStream("Serialization");
        using (var writer = new Utf8JsonWriter((Stream)plainStream))
        {
            JsonSerializer.Serialize(writer, value);
            writer.Flush();
        }

        // 2. Шифруем данные из временного потока в новый
        var plainData = new ArraySegment<byte>(plainStream.GetBuffer(), 0, (int)plainStream.Length);
        using var encryptedStream = EncryptToStream(plainData);

        // 3. Отправляем зашифрованную последовательность
        await SendAsync(encryptedStream, true);
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

    public async Task SendAsync(ReadOnlySequence<byte> sequence, bool endOfMessage = true)
    {
        if (_ws.State != WebSocketState.Open) return;

        await _sendLock.WaitAsync();
        try
        {
            if (_ws.State != WebSocketState.Open) return;

            // Если последовательность пуста
            if (sequence.IsEmpty)
            {
                await _ws.SendAsync(ReadOnlyMemory<byte>.Empty, WebSocketMessageType.Binary, endOfMessage, _cts.Token);
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
    
    public async Task SendAsync(RecyclableMemoryStream stream, bool endOfMessage = true)
    {
        if (_ws.State != WebSocketState.Open) return;

        // RMS может состоять из множества блоков (сегментов)
        // GetReadOnlySequence() — самый быстрый способ получить доступ к ним без копирования
        var sequence = stream.GetReadOnlySequence();

        await SendAsync(sequence, endOfMessage);
    }

    public async Task<T?> ReceiveJsonAsync<T>()
    {
        // 3. Используем поток и автоматически возвращаем его в пул через Dispose
        using var stream = await ReceiveFullMessageAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<T>(stream, (JsonSerializerOptions?)null, _cts.Token);
    }

    public async Task<RecyclableMemoryStream> ReceiveFullMessageAsStreamAsync()
    {
        // 1. Берем поток из менеджера (вызывающий код ДОЛЖЕН сделать ему Dispose)
        var ms = s_streamManager.GetStream();
        var buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);

        try
        {
            WebSocketReceiveResult result;
            do
            {
                result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
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

    public RecyclableMemoryStream EncryptToStream(ArraySegment<byte> data)
    {
        int payloadSize = data.Count;
        int totalSize = NONCE_SIZE + TAG_SIZE + payloadSize;

        // Берем новый поток для зашифрованных данных
        var outputStream = s_streamManager.GetStream("EncryptionOutput", totalSize);

        // Резервируем место в потоке (важно для получения Span)
        outputStream.SetLength(totalSize);
        var buffer = outputStream.GetSpan(totalSize);

        using (var aes = new AesGcm(_sharedKey, TAG_SIZE))
        {
            var nonceSpan = buffer.Slice(0, NONCE_SIZE);
            var tagSpan = buffer.Slice(NONCE_SIZE, TAG_SIZE);
            var cipherSpan = buffer.Slice(NONCE_SIZE + TAG_SIZE, payloadSize);

            RandomNumberGenerator.Fill(nonceSpan);

            // Шифруем напрямую в Span, выделенный из RecyclableMemoryStream
            aes.Encrypt(nonceSpan, data.AsSpan(), cipherSpan, tagSpan);
        }

        // Сбрасываем позицию в начало для последующего чтения/отправки
        outputStream.Position = 0;
        return outputStream;
    }

    public RecyclableMemoryStream DecryptOptimized(RecyclableMemoryStream encryptedStream)
    {
        // 1. Проверки длины (Nonce + Tag)
        long totalLength = encryptedStream.Length;
        if (totalLength < NONCE_SIZE + TAG_SIZE)
            throw new CryptographicException("Сообщение слишком короткое");

        int plaintextLength = (int)totalLength - NONCE_SIZE - TAG_SIZE;

        // 2. Получаем доступ к исходным данным без копирования
        // RecyclableMemoryStream гарантирует доступ к буферу
        byte[] inputBuffer = encryptedStream.GetBuffer();

        // 3. Создаем целевой поток для расшифрованных данных
        var resultStream = (RecyclableMemoryStream)s_streamManager.GetStream();

        try
        {
            // Устанавливаем размер заранее, чтобы избежать реаллокаций внутри потока
            resultStream.SetLength(plaintextLength);
            byte[] outputBuffer = resultStream.GetBuffer();

            using (var aes = new AesGcm(_sharedKey, TAG_SIZE))
            {
                // Используем Span для сегментации данных в исходном массиве
                var nonceSpan = inputBuffer.AsSpan(0, NONCE_SIZE);
                var tagSpan = inputBuffer.AsSpan(NONCE_SIZE, TAG_SIZE);
                var cipherSpan = inputBuffer.AsSpan(NONCE_SIZE + TAG_SIZE, plaintextLength);

                // Расшифровываем напрямую в буфер нового потока
                aes.Decrypt(nonceSpan, cipherSpan, tagSpan, outputBuffer.AsSpan(0, plaintextLength));
            }

            resultStream.Position = 0;
            return resultStream;
        }
        catch
        {
            resultStream.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _ws.Dispose();
        _cts.Dispose();
    }
}