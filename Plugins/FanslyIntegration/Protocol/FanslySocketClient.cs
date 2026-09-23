using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace FanslyIntegration.Protocol;

/// <summary>
/// Websocket client for the Fansly realtime gateway.
///
/// Fansly frames are JSON envelopes of the form <c>{ "t": &lt;int&gt;, "d": &lt;string|object&gt; }</c>
/// where <c>d</c> is usually a *stringified* JSON payload. On connect we send the auth handshake
/// (carrying the account token), subscribe to the chat room, and then keep the socket alive with
/// periodic heartbeats.
///
/// The exact opcodes and payload field names are not officially documented; they live in
/// <see cref="FanslyConstants.Op"/> and are decoded defensively. Any frame whose type we do not
/// recognise is surfaced via <see cref="UnknownFrameReceived"/> so it can be inspected in the
/// event log and the mapping finalised against live traffic.
/// </summary>
public sealed class FanslySocketClient : IAsyncDisposable
{
    private readonly string _authToken;
    private readonly string _chatRoomId;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private Task? _heartbeatLoop;

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public event Action? Connected;
    public event Action<string>? Disconnected;
    public event Action<string>? StatusMessage;

    /// <summary>Raised for a chat-room message; carries the decoded inner payload object.</summary>
    public event Action<JsonElement>? ChatMessageReceived;

    /// <summary>Raised for an account/feed event (follow, subscribe); carries the payload object.</summary>
    public event Action<JsonElement>? AccountEventReceived;

    /// <summary>Raised for a frame whose type code we do not yet map. Carries the raw frame text.</summary>
    public event Action<int, string>? UnknownFrameReceived;

    public FanslySocketClient(string authToken, string chatRoomId)
    {
        _authToken = authToken;
        _chatRoomId = chatRoomId;
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(FanslyConstants.WebSocketUrl), _cts.Token);

        // Auth handshake: the token payload is a stringified JSON object.
        await SendFrameAsync(FanslyConstants.Op.Connect,
            JsonSerializer.Serialize(new { token = _authToken }), _cts.Token);

        // Subscribe to the live chat room.
        await SendFrameAsync(FanslyConstants.Op.Subscribe,
            JsonSerializer.Serialize(new { chatRoomId = _chatRoomId }), _cts.Token);

        Connected?.Invoke();

        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        _heartbeatLoop = Task.Run(() => HeartbeatLoopAsync(_cts.Token));
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                await Task.Delay(FanslyConstants.HeartbeatInterval, ct);
                await SendFrameAsync(FanslyConstants.Op.Heartbeat, null, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        catch
        {
            // heartbeat failures will surface through the receive loop's disconnect
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[32 * 1024];
        var sb = new StringBuilder();

        try
        {
            while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Disconnected?.Invoke("Server closed the connection");
                        return;
                    }
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                HandleFrame(sb.ToString());
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        catch (WebSocketException ex)
        {
            Disconnected?.Invoke($"Websocket error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Disconnected?.Invoke($"Receive error: {ex.Message}");
        }
    }

    private void HandleFrame(string raw)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(raw);
        }
        catch
        {
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("t", out var typeEl) ||
                typeEl.ValueKind != JsonValueKind.Number ||
                !typeEl.TryGetInt32(out var type))
            {
                return;
            }

            // The "d" payload is normally a stringified JSON object; sometimes an object directly.
            var payload = ExtractPayload(root);

            switch (type)
            {
                case FanslyConstants.Op.Heartbeat:
                    return; // keepalive echo

                case FanslyConstants.Op.Connect:
                    StatusMessage?.Invoke("Authenticated with Fansly gateway");
                    return;

                case FanslyConstants.Op.Subscribe:
                    StatusMessage?.Invoke("Subscribed to chat room; listening for events.");
                    return;

                case FanslyConstants.Op.ChatRoomMessage:
                    if (payload is { } chat) ChatMessageReceived?.Invoke(chat);
                    return;

                case FanslyConstants.Op.AccountEvent:
                    if (payload is { } acct) AccountEventReceived?.Invoke(acct);
                    return;

                default:
                    UnknownFrameReceived?.Invoke(type, raw);
                    return;
            }
        }
    }

    /// <summary>Decodes the "d" field, which may be a stringified JSON object or an inline object.</summary>
    private static JsonElement? ExtractPayload(JsonElement root)
    {
        if (!root.TryGetProperty("d", out var d))
        {
            return null;
        }

        try
        {
            if (d.ValueKind == JsonValueKind.String)
            {
                var s = d.GetString();
                if (string.IsNullOrWhiteSpace(s)) return null;
                using var doc = JsonDocument.Parse(s);
                return doc.RootElement.Clone();
            }
            if (d.ValueKind == JsonValueKind.Object)
            {
                return d.Clone();
            }
        }
        catch
        {
            // malformed payload
        }
        return null;
    }

    private async Task SendFrameAsync(int type, string? data, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open)
        {
            return;
        }

        var frame = data is null
            ? JsonSerializer.Serialize(new { t = type })
            : JsonSerializer.Serialize(new { t = type, d = data });

        var bytes = Encoding.UTF8.GetBytes(frame);
        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    public async Task DisconnectAsync(string reason = "Disconnected")
    {
        try
        {
            _cts?.Cancel();
            if (_ws?.State == WebSocketState.Open)
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, CancellationToken.None);
            }
        }
        catch
        {
            // best-effort close
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _cts?.Cancel();
            var loops = new[] { _receiveLoop, _heartbeatLoop }.Where(t => t != null).Cast<Task>().ToArray();
            if (loops.Length > 0)
            {
                await Task.WhenAny(Task.WhenAll(loops), Task.Delay(1000));
            }
        }
        catch { }

        _ws?.Dispose();
        _cts?.Dispose();
    }
}
