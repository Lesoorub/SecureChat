using System.Buffers;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Identity.Data;
using System.Security;
using System.Text.Json;
using Microsoft.IO;
using System.Text;
using SecureRemotePassword;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using SRP.Extra;
using System;

namespace Backend.Controllers;

public class Room : IDisposable
{
    private static readonly RecyclableMemoryStreamManager _streamManager = new RecyclableMemoryStreamManager();

    private readonly List<Member> _members = new();
    private readonly object _syncRoot = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public int MembersCount
    {
        get
        {
            lock (_syncRoot) return _members.Count;
        }
    }

    public bool IsDisposed
    {
        get
        {
            lock (_syncRoot) return _disposed;
        }
    }

    private readonly string _salt;
    private readonly string _verifier;

    public Room(string salt, string verifier)
    {
        _salt = salt;
        _verifier = verifier;
    }

    public async Task AddMember(WebSocket ws)
    {
        Member member;
        var token = _cts.Token;
        lock (_syncRoot)
        {
            if (_disposed || _members.Any(m => m.Socket == ws))
            {
                return;
            }
            member = new Member(ws);
            _members.Add(member);
        }
        await Propogate(ws, token);
        lock (_syncRoot)
        {
            _members.Remove(member);
        }
    }

    public Task<bool> TryAuthSRC(WebSocket webSocket, string roomname)
    {
        return TryAuthSRC(webSocket, roomname, _salt, _verifier);
    }

    static async Task<bool> TryAuthSRC(WebSocket webSocket, string roomname, string salt, string verifier)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var sessionKey = await webSocket.AuthSrpAsServer(roomname, salt, verifier, cts.Token);
            return !string.IsNullOrWhiteSpace(sessionKey);
        }
        catch (AuthSrpException srpEx)
        {
            return false;
        }
        finally
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    async Task Propogate(WebSocket ws, CancellationToken token)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);
        // Получаем стрим из пула менеджера
        using var ms = _streamManager.GetStream();
        try
        {
            while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                await ReceiveMessage(ws, buffer, ms, token);
                await BroadcastAsync(ms, ws);
            }
        }
        catch (OperationCanceledException)
        {
            /* Норма при выключении */
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    async Task ReceiveMessage(WebSocket ws, ArraySegment<byte> buffer, MemoryStream ms, CancellationToken token)
    {
        ms.SetLength(0);
        while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            var result = await ws.ReceiveAsync(buffer, token);
            if (result.MessageType == WebSocketMessageType.Close) break;

            ms.Write(buffer.Array!, buffer.Offset, result.Count);

            if (result.EndOfMessage)
            {
                return;
            }
        }
    }

    public async Task BroadcastAsync(MemoryStream ms, WebSocket sender)
    {
        // RecyclableMemoryStream эффективно возвращает данные через GetBuffer() 
        // или TryGetBuffer(), если они влезают в один блок пула.
        if (!ms.TryGetBuffer(out ArraySegment<byte> buffer))
        {
            // Если данные фрагментированы, GetBuffer() вернет объединенный массив из пула
            buffer = new ArraySegment<byte>(ms.GetBuffer(), 0, (int)ms.Length);
        }

        Member[]? targets = null;
        int count = 0;

        lock (_syncRoot)
        {
            if (_disposed) return;
            targets = ArrayPool<Member>.Shared.Rent(_members.Count);
            foreach (var m in _members)
            {
                if (m.Socket != sender) targets[count++] = m;
            }
        }

        if (count == 0)
        {
            if (targets != null) ArrayPool<Member>.Shared.Return(targets);
            return;
        }

        Task[] tasks = ArrayPool<Task>.Shared.Rent(count);
        try
        {
            for (int i = 0; i < count; i++)
            {
                // Отправляем сегмент. SendSafeAsync должен ожидать завершения отправки.
                tasks[i] = targets[i].SendSafeAsync(buffer, true);
            }

            await Task.WhenAll(new ArraySegment<Task>(tasks, 0, count));
        }
        finally
        {
            Array.Clear(tasks, 0, count);
            ArrayPool<Task>.Shared.Return(tasks);
            Array.Clear(targets, 0, count);
            ArrayPool<Member>.Shared.Return(targets);
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed) return;
            _disposed = true;

            _cts.Cancel();
            _cts.Dispose();
            foreach (var member in _members) member.Dispose();
            _members.Clear();
        }
    }

    private class Member : IDisposable
    {
        public readonly WebSocket Socket;
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public Member(WebSocket socket) => Socket = socket;

        public async Task SendSafeAsync(ArraySegment<byte> data, bool endMessage)
        {
            if (Socket.State != WebSocketState.Open) return;

            await _sendLock.WaitAsync();
            try
            {
                if (Socket.State == WebSocketState.Open)
                {
                    await Socket.SendAsync(data, WebSocketMessageType.Binary, endMessage, CancellationToken.None);
                }
            }
            catch { /* Логируем или игнорируем ошибки конкретного сокета */ }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Dispose()
        {
            _sendLock.Dispose();
            Socket.Dispose();
        }
    }

    public class LogInRequest
    {
        [JsonPropertyName("clientEphemeralPublic")]
        public string ClientEphemeralPublic { get; set; } = string.Empty;
    }

    public class LogInResponse
    {
        [JsonPropertyName("salt")]
        public string Salt { get; set; } = string.Empty;
        [JsonPropertyName("serverEphemeralPublic")]
        public string ServerEphemeralPublic { get; set; } = string.Empty;
    }
}
