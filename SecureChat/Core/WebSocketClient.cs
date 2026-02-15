using System.Buffers;
using System.IO;
using System.Net.WebSockets;

namespace SecureChat.Core;

public class WebSocketClient : IDisposable
{
    private ClientWebSocket? _client;
    private readonly Uri _uri;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    public readonly struct WebSocketMessage
    {
        public readonly WebSocketMessageType Type;
        public readonly ArraySegment<byte> Segment;
        public readonly bool EndOfMessage;

        public WebSocketMessage(WebSocketMessageType type, ArraySegment<byte> segment, bool endOfMessage)
        {
            Type = type;
            Segment = segment;
            EndOfMessage = endOfMessage;
        }
    }

    private readonly AsyncSingleSlotBuffer<WebSocketMessage> _receivedMsg = new();

    public delegate ValueTask ConnectArgs(WebSocket webSocket);
    public event ConnectArgs? Connect;

    public delegate ValueTask CloseReceived(WebSocketCloseStatus? status, string? reason);
    public event CloseReceived? Close;

    public WebSocketState State => _client?.State ?? WebSocketState.Aborted;

    public WebSocketClient(Uri uri)
    {
        _uri = uri;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using (var ws = _client = new ClientWebSocket())
            {
                try
                {
                    await ws.ConnectAsync(_uri, ct);
                    if (Connect is not null)
                    {
                        try
                        {
                            await Connect.Invoke(ws);
                        }
                        catch { }
                    }
                    var (status, desc) = await ReceiveLoop(ws, ct);
                    if (Close is not null)
                    {
                        try
                        {
                            await Close.Invoke(status, desc);
                        }
                        catch { }
                    }
                }
                catch (OperationCanceledException)
                {
                    // do nothing
                }
                catch
                {
                    try
                    {
                        await Task.Delay(ReconnectDelay, ct); // Пауза перед следующей попыткой
                    }
                    catch (OperationCanceledException)
                    {
                        // do nothing
                    }
                }
            }
        }
    }

    public async ValueTask Send(ArraySegment<byte> bytes, WebSocketMessageType type = WebSocketMessageType.Binary, bool endOfMessage = true, CancellationToken cancellationToken = default)
    {
        // Копируем ссылку на текущий клиент, чтобы избежать Race Condition при пересоздании _client
        var ws = _client;

        if (ws == null || ws.State != WebSocketState.Open) return;

        await _sendLock.WaitAsync();
        try
        {
            await ws.SendAsync(bytes, type, endOfMessage, cancellationToken);
        }
        catch
        {
            // Логируем ошибку, если нужно. При разрыве ReceiveLoop упадет сам и перезапустит цикл.
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task ReceiveMessageToStream(MemoryStream ms, CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var msg = await _receivedMsg.ReadAsync(cancellationToken);
            if (msg.Type == WebSocketMessageType.Close)
            {
                break;
            }
            await ms.WriteAsync(msg.Segment, cancellationToken);
            _receivedMsg.Reset();
            if (msg.EndOfMessage)
            {
                break;
            }
        }
    }

    private async Task<(WebSocketCloseStatus? status, string? reason)> ReceiveLoop(ClientWebSocket ws, CancellationToken ct)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(1024 * 32);
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                try
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                    await _receivedMsg.PutAsync(new WebSocketMessage(result.MessageType, new ArraySegment<byte>(buffer, 0, result.Count), result.EndOfMessage));

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return (result.CloseStatus, result.CloseStatusDescription);
                    }
                }
                catch (Exception ex)
                {
                    // Оповещаем потребителя о системной ошибке через тип Close
                    await _receivedMsg.PutAsync(
                        new WebSocketMessage(
                            WebSocketMessageType.Close,
                            ArraySegment<byte>.Empty,
                            true
                        ),
                        ct
                    );
                    return (WebSocketCloseStatus.InternalServerError, ex.Message);
                }
            }
            return (WebSocketCloseStatus.NormalClosure, string.Empty);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void Dispose()
    {
        _sendLock.Dispose();
        _client?.Dispose();
        Connect = null;
        Close = null;
    }
}

public class AsyncSingleSlotBuffer<T>
{
    private T? _value;
    // Начинаем с 1: слот свободен для записи
    private readonly SemaphoreSlim _canWrite = new(1, 1);
    // Начинаем с 0: читать пока нечего
    private readonly SemaphoreSlim _canRead = new(0, 1);

    public async Task PutAsync(T value, CancellationToken ct = default)
    {
        // Ждем, пока слот станет пустым
        await _canWrite.WaitAsync(ct);

        _value = value;

        // Сигнализируем, что можно читать
        _canRead.Release();
    }

    public async Task<T> ReadAsync(CancellationToken ct = default)
    {
        // Ждем, пока в слоте что-то появится
        await _canRead.WaitAsync(ct);

        T value = _value!;
        _value = default; // Опционально: очищаем ссылку

        return value;
    }

    public void Reset()
    {
        // Сигнализируем, что слот снова свободен для записи
        _canWrite.Release();
    }
}
