using System.Buffers;
using System.Net.WebSockets;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using SecureRemotePassword;

namespace SRP.Extra;

public static class WebsocketExtensions
{
    public static async Task<string> AuthSrpAsServer(this WebSocket webSocket, string username, string salt, string verifier, CancellationToken ct = default)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);
        try
        {
            var bufferSegment = new ArraySegment<byte>(buffer);
            var srpParams = SrpParameters.Create2048<SHA256>();
            var server = new SrpServer(srpParams);

            // 1. Client → Server: I, A
            var clientPublicEphemeral = await ReadStringAsync(webSocket, bufferSegment, ct);
            if (clientPublicEphemeral is null)
            {
                throw new AuthSrpException(AuthSrpException.ReasonEnum.ConnectionError);
            }
            if (!ValidatePublic(srpParams, clientPublicEphemeral))
            {
                throw new AuthSrpException(AuthSrpException.ReasonEnum.ProtocolErrorOrAttackDetected);
            }

            // 2. Server → Client: s, B
            var serverEphemeral = server.GenerateEphemeral(verifier);

            await WriteStringAsync(webSocket, salt, ct);
            await WriteStringAsync(webSocket, serverEphemeral.Public, ct);

            // 3. Client → Server: M1
            var clientSessionProof = await ReadStringAsync(webSocket, bufferSegment, ct);
            if (clientSessionProof is null)
            {
                throw new AuthSrpException(AuthSrpException.ReasonEnum.ConnectionError);
            }

            // 4. Server → Client: M2
            try
            {
                var serverSession = server.DeriveSession(serverEphemeral.Secret, clientPublicEphemeral, salt, username, verifier, clientSessionProof);
                await WriteStringAsync(webSocket, serverSession.Proof, ct);
                return serverSession.Key; // Session Key
            }
            catch (SecurityException)
            {
                throw new AuthSrpException(AuthSrpException.ReasonEnum.NotCorrectPassword);
            }

        }
        catch (WebSocketException ex)
        {
            throw new AuthSrpException(AuthSrpException.ReasonEnum.WebSocketError, ex);
        }
        finally
        {
            Array.Clear(buffer, 0, buffer.Length);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool ValidatePublic(SrpParameters srpParameters, string clientPublicEphemeral)
    {
        try
        {
            // 1. Парсим hex
            var A = SrpInteger.FromHex(clientPublicEphemeral);
            var N = srpParameters.Prime;

            return (A % N) != SrpInteger.Zero;
        }
        catch
        {
            // Если пришла абракадабра, которую FromHex не смог распарсить
            return false;
        }
    }

    public static async Task<string> AuthSrpAsClient(this ClientWebSocket webSocket, string username, string password, CancellationToken ct = default)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);
        try
        {
            var bufferSegment = new ArraySegment<byte>(buffer);
            var srpParams = SrpParameters.Create2048<SHA256>();
            var client = new SrpClient(srpParams);
            var clientEphemeral = client.GenerateEphemeral();

            // 1. Client → Server: I, A
            await WriteStringAsync(webSocket, clientEphemeral.Public, ct);

            // 2. Server → Client: s, B
            var salt = await ReadStringAsync(webSocket, bufferSegment, ct);
            var serverEphemeralPublic = await ReadStringAsync(webSocket, bufferSegment, ct);
            if (string.IsNullOrEmpty(salt) || string.IsNullOrEmpty(serverEphemeralPublic))
            {
                throw new AuthSrpException(AuthSrpException.ReasonEnum.WebSocketError);
            }

            // 3. Client → Server: M1
            var privateKey = client.DerivePrivateKey(salt, username, password);
            var clientSession = client.DeriveSession(clientEphemeral.Secret, serverEphemeralPublic, salt, username, privateKey);
            await WriteStringAsync(webSocket, clientSession.Proof, ct);

            // 4. Server → Client: M2
            var serverSessionProof = await ReadStringAsync(webSocket, bufferSegment, ct);
            if (string.IsNullOrEmpty(serverSessionProof))
            {
                throw new AuthSrpException(AuthSrpException.ReasonEnum.WebSocketError);
            }

            // 5. Client verifies M2
            try
            {
                client.VerifySession(clientEphemeral.Public, clientSession, serverSessionProof);
            }
            catch (SecurityException)
            {
                throw new AuthSrpException(AuthSrpException.ReasonEnum.NotCorrectPassword);
            }

            return clientSession.Key;
        }
        catch (WebSocketException ex)
        {
            throw new AuthSrpException(AuthSrpException.ReasonEnum.WebSocketError, ex);
        }
        finally
        {
            Array.Clear(buffer, 0, buffer.Length);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async Task<string?> ReadStringAsync(this WebSocket webSocket, ArraySegment<byte> byteBuffer, CancellationToken ct = default)
    {
        if (webSocket.State != WebSocketState.Open) return null;

        // Арендуем буферы, чтобы не нагружать GC
        char[] charBuffer = ArrayPool<char>.Shared.Rent(Encoding.UTF8.GetMaxCharCount(4096));

        var sb = new StringBuilder();
        var decoder = Encoding.UTF8.GetDecoder();

        byteBuffer.AsSpan().Clear();
        try
        {
            ValueWebSocketReceiveResult result;
            do
            {
                result = await webSocket.ReceiveAsync(byteBuffer.AsMemory(), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "OK", ct);
                    return null;
                }

                // Декодируем порцию байтов в порцию символов
                int charsUsed = decoder.GetChars(byteBuffer.Array!, 0, result.Count, charBuffer, 0, result.EndOfMessage);
                sb.Append(charBuffer, 0, charsUsed);

            } while (!result.EndOfMessage);

            return sb.ToString();
        }
        finally
        {
            // Возвращаем массивы в пул
            Array.Clear(charBuffer, 0, charBuffer.Length);
            ArrayPool<char>.Shared.Return(charBuffer);
        }
    }

    public static async Task WriteStringAsync(this WebSocket webSocket, string text, CancellationToken ct = default)
    {
        // 1. Быстрая проверка состояния без лишних условий
        if (webSocket.State != WebSocketState.Open)
            return;

        // 2. Используем ArrayPool для аренды буфера, чтобы избежать аллокации byte[]
        int maxByteCount = Encoding.UTF8.GetMaxByteCount(text.Length);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(maxByteCount);
        try
        {
            // 3. Кодируем напрямую в арендованный массив
            int bytesWritten = Encoding.UTF8.GetBytes(text, 0, text.Length, buffer, 0);

            // 4. Используем перегрузку с Memory<byte>, она эффективнее ArraySegment
            await webSocket.SendAsync(
                buffer.AsMemory(0, bytesWritten),
                WebSocketMessageType.Text,
                true,
                ct
            );
        }
        finally
        {
            // 5. Обязательно возвращаем буфер в пул
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}