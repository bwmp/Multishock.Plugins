using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace StreamElementsIntegration.Protocol;

/// <summary>
/// Minimal hand-rolled Socket.IO (Engine.IO v3) client over <see cref="ClientWebSocket"/>.
///
/// Both the StreamElements and Streamlabs realtime servers run socket.io 2.x, i.e.
/// Engine.IO protocol v3, where the CLIENT periodically sends an engine.io ping ("2")
/// and the server replies with a pong ("3"). Only the default namespace ("/") and
/// text (non-binary) events are used by these APIs, so that is all this client supports.
///
/// Frame reference (first char = engine.io packet type):
///   0 = open (JSON handshake with sid/pingInterval), 1 = close, 2 = ping, 3 = pong, 4 = message
/// Within a message ("4..."), the next char is the socket.io packet type:
///   0 = CONNECT, 1 = DISCONNECT, 2 = EVENT ["name", data...], 4 = ERROR
/// </summary>
public sealed class SocketIoClient : IDisposable
{
    private readonly Uri _uri;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private int _pingIntervalMs = 25000;

    public SocketIoClient(Uri uri) => _uri = uri;

    /// <summary>Fired when the socket.io default namespace connects (engine open followed by "40").</summary>
    public event Action? Connected;

    /// <summary>
    /// Fired for every socket.io event. <paramref name="data"/> is the first argument after the
    /// event name (its <see cref="JsonElement.ValueKind"/> is <see cref="JsonValueKind.Undefined"/>
    /// when the event carried no payload).
    /// </summary>
    public event Action<string, JsonElement>? EventReceived;

    /// <summary>Fired when the socket closes or errors, with a human-readable reason.</summary>
    public event Action<string>? Disconnected;

    /// <summary>Diagnostic log messages (not user-facing errors).</summary>
    public event Action<string>? Log;

    public bool IsOpen => _ws?.State == WebSocketState.Open;

    public async Task ConnectAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(_uri, _cts.Token);
        _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    /// <summary>Emits a socket.io event on the default namespace: <c>42["eventName", payload]</c>.</summary>
    public Task EmitAsync(string eventName, object payload, CancellationToken ct)
    {
        var arr = JsonSerializer.Serialize(new object[] { eventName, payload });
        return SendRawAsync("42" + arr, ct);
    }

    private async Task SendRawAsync(string frame, CancellationToken ct)
    {
        var ws = _ws;
        if (ws is null || ws.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(frame);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var sb = new StringBuilder();
        try
        {
            while (!ct.IsCancellationRequested && _ws!.State == WebSocketState.Open)
            {
                var result = await _ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Disconnected?.Invoke($"Server closed the connection ({result.CloseStatus})");
                    return;
                }

                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage) continue;

                var frame = sb.ToString();
                sb.Clear();
                HandleFrame(frame, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            Disconnected?.Invoke(ex.Message);
        }
    }

    private void HandleFrame(string frame, CancellationToken ct)
    {
        if (frame.Length == 0) return;

        var engineType = frame[0];
        var rest = frame.Substring(1);

        switch (engineType)
        {
            case '0': // engine.io open handshake
                try
                {
                    using var doc = JsonDocument.Parse(rest);
                    if (doc.RootElement.TryGetProperty("pingInterval", out var pi) && pi.TryGetInt32(out var ms) && ms > 0)
                        _pingIntervalMs = ms;
                }
                catch { /* keep default ping interval */ }
                _ = Task.Run(() => PingLoopAsync(ct));
                Log?.Invoke("Engine.IO handshake complete");
                break;

            case '1': // engine.io close
                Disconnected?.Invoke("Server closed the connection");
                break;

            case '2': // engine.io ping from server (not expected in EIO3, but answer defensively)
                _ = SendRawAsync("3", ct);
                break;

            case '3': // engine.io pong (reply to our ping) — nothing to do
                break;

            case '4': // socket.io message
                HandleMessage(rest);
                break;
        }
    }

    private void HandleMessage(string rest)
    {
        if (rest.Length == 0) return;

        var sioType = rest[0];
        var payload = rest.Substring(1);

        switch (sioType)
        {
            case '0': // CONNECT (namespace connected)
                Connected?.Invoke();
                break;
            case '1': // DISCONNECT
                Disconnected?.Invoke("Disconnected by server");
                break;
            case '2': // EVENT
                DispatchEvent(payload);
                break;
            case '4': // ERROR
                Disconnected?.Invoke("Socket.IO error: " + payload);
                break;
        }
    }

    private void DispatchEvent(string payload)
    {
        // An EVENT that requests an ack is prefixed with a numeric ack id before the JSON
        // array (e.g. 2<ackId>["name",...]). Strip any leading digits; the array starts with '['.
        var i = 0;
        while (i < payload.Length && char.IsDigit(payload[i])) i++;
        var json = payload.Substring(i);
        if (json.Length == 0) return;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return;

            var name = root[0].GetString() ?? "";
            var data = root.GetArrayLength() > 1 ? root[1].Clone() : default;
            EventReceived?.Invoke(name, data);
        }
        catch (Exception ex)
        {
            Log?.Invoke("Failed to parse event frame: " + ex.Message);
        }
    }

    private async Task PingLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _ws!.State == WebSocketState.Open)
            {
                await Task.Delay(_pingIntervalMs, ct);
                await SendRawAsync("2", ct); // engine.io ping
            }
        }
        catch (OperationCanceledException) { }
        catch { /* ping loop ends when the socket closes */ }
    }

    public async Task CloseAsync()
    {
        try
        {
            _cts?.Cancel();
            if (_ws is { State: WebSocketState.Open })
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
        }
        catch { /* best effort */ }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        _ws?.Dispose();
        _cts?.Dispose();
    }
}
