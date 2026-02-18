using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Serialization;
using Konscious.Security.Cryptography;
using Microsoft.IO;
using SecureChat.Core.Attributes;
using SecureRemotePassword;
using SRP.Extra;

namespace SecureChat.Core;

[Singeltone(Order: 1)]
internal class BackendAdapter
{
    public string ServerUrl { get; set; } =
#if DEBUG
        "https://localhost:44362/";
#else
        "http://212.193.27.71:5000/";
#endif

    private readonly HttpClient _httpClient;
    private readonly RecyclableMemoryStreamManager _streamManager;

    public BackendAdapter(HttpClient httpClient, RecyclableMemoryStreamManager streamManager)
    {
        _httpClient = httpClient;
        _streamManager = streamManager;
    }

    // HTTP-запрос для создания комнаты
    public async Task CreateChat(string roomname, string password)
    {
        var client = new SrpClient();
        var salt = client.GenerateSalt();
        var x = client.DerivePrivateKey(salt, roomname, password);
        var verifier = client.DeriveVerifier(x);

        var uri = new UriBuilder(ServerUrl)
        {
            Path = "/chat/create",
        }.Uri;

        using var content = JsonContent.Create(new CreateRoomRequest
        {
            RoomName = roomname,
            Salt = salt,
            Verifier = verifier
        });
        using var response = await _httpClient.PostAsync(uri, content);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string> GetVersion()
    {
        var uri = new UriBuilder(ServerUrl)
        {
            Path = "/app/version",
        }.Uri;

        using var response = await _httpClient.GetAsync(uri);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
    public struct DownloadProgress
    {
        public long ReadBytes;
        public long TotalBytes;
        public double Percent => TotalBytes > 0 ? (double)ReadBytes / TotalBytes * 100 : 0;
    }

    // Делегат для передачи структуры по ссылке (in)
    public delegate void ProgressHandler(in DownloadProgress progress);

    public async Task GetLatest(string filepath, ProgressHandler progress, CancellationToken ct = default)
    {
        var uri = new UriBuilder(ServerUrl) { Path = "/app/latest" }.Uri;

        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? -1L;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
        DownloadProgress progressInfo = new() { TotalBytes = totalBytes };

        try
        {
            // Открываем поток внутри try, чтобы в случае чего гарантированно закрыть и удалить файл
            using (var fs = new FileStream(filepath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
            {
                using var netStream = await response.Content.ReadAsStreamAsync(ct);
                int read;
                long totalRead = 0;

                while ((read = await netStream.ReadAsync(buffer.AsMemory(), ct)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                    totalRead += read;
                    progressInfo.ReadBytes = totalRead;
                    progress?.Invoke(in progressInfo);
                }
            }
        }
        catch (Exception) when (CleanupFile(filepath))
        {
            // Этот блок никогда не выполнится из-за CleanupFile, 
            // но исключение пробросится дальше (rethrow)
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        bool CleanupFile(string filepath)
        {
            try
            {
                if (File.Exists(filepath)) File.Delete(filepath);
            }
            catch { /* Игнорируем ошибки удаления */ }
            return false; // Возвращаем false, чтобы catch не "поглотил" исключение
        }
    }

    public async Task<string> GetLatestHashsum()
    {
        var uri = new UriBuilder(ServerUrl)
        {
            Path = "/app/checksum",
        }.Uri;

        using var response = await _httpClient.GetAsync(uri);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<ChatSession> JoinChat(string roomname, string password)
    {
        var ws = new ClientWebSocket();
        var wsUri = new UriBuilder(ServerUrl)
        {
            Scheme = ServerUrl.StartsWith("https") ? "wss" : "ws",
            Path = "/chat/join",
            Query = $"roomname={WebUtility.UrlEncode(roomname)}"
        }.Uri;

        // Подключаемся
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        {
            await ws.ConnectAsync(wsUri, cts.Token);
        }
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        {
            await ws.AuthSrpAsClient(roomname, password);
        }
        return new ChatSession(ws, await DeriveKeyAsync(password, roomname), _streamManager);
    }

    public async Task<byte[]> DeriveKeyAsync(string password, string roomName)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        // Соль должна быть уникальной для каждой комнаты
        var salt = Encoding.UTF8.GetBytes(roomName + "static_salt_for_app_v1");

        using var argon2 = new Argon2id(passwordBytes);
        argon2.Salt = salt;
        argon2.DegreeOfParallelism = 4;     // Количество ядер
        argon2.Iterations = 4;              // Количество проходов
        argon2.MemorySize = 65536;          // 64 MB RAM

        return await argon2.GetBytesAsync(32); // Возвращаем 256 бит для AES
    }

    public class CreateRoomRequest
    {
        [JsonPropertyName("room")]
        public string RoomName { get; set; } = string.Empty;
        [JsonPropertyName("salt")]
        public string Salt { get; set; } = string.Empty;
        [JsonPropertyName("verifier")]
        public string Verifier { get; set; } = string.Empty;
    }

    public class ErrorResponse
    {
        [JsonPropertyName("error")]
        public string Reason { get; set; } = string.Empty;
    }

    public class LogInRequest
    {
        [JsonPropertyName("clientEphemeralPublic")]
        public string ClientEphemeralPublic { get; set; } = string.Empty;
    }

    public class LogInServerResponse
    {
        [JsonPropertyName("salt")]
        public string Salt { get; set; } = string.Empty;
        [JsonPropertyName("serverEphemeralPublic")]
        public string ServerEphemeralPublic { get; set; } = string.Empty;
    }

}
