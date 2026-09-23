using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace JoystickIntegration.Protocol;

/// <summary>
/// Thin ActionCable (actioncable-v1-json) websocket client for the joystick.tv Bot API.
/// Connects with the Base64(clientId:clientSecret) token, subscribes to the GatewayChannel,
/// and surfaces the raw message envelopes. Higher-level interpretation lives in
/// JoystickConnectionService.
/// </summary>
public sealed class JoystickCableClient : IAsyncDisposable
{
    private readonly string _basicKey;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public event Action? SubscriptionConfirmed;
    public event Action? SubscriptionRejected;
    public event Action<string>? Disconnected;
    public event Action<string>? StatusMessage;

    /// <summary>Raised for each chat message; carries the inner "message" object.</summary>
    public event Action<JsonElement>? ChatMessageReceived;

    /// <summary>Raised when a viewer enters/leaves; carries the inner "message" object.</summary>
    public event Action<JsonElement>? UserPresenceReceived;

    /// <summary>Raised for stream events (Tipped, Followed, etc.); carries the inner "message" object.</summary>
    public event Action<JsonElement>? StreamEventReceived;

    public JoystickCableClient(string basicKey)
    {
        _basicKey = basicKey;
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var wsUrl = JoystickConstants.CableUrl.Replace("https://", "wss://").Replace("http://", "ws://")
            + "?token=" + Uri.EscapeDataString(_basicKey);

        _ws = new ClientWebSocket();
        _ws.Options.AddSubProtocol(JoystickConstants.CableSubProtocol);

        await _ws.ConnectAsync(new Uri(wsUrl), _cts.Token);

        // Subscribe to the gateway channel.
        await SendRawAsync(JsonSerializer.Serialize(new
        {
            command = "subscribe",
            identifier = JoystickConstants.GatewayIdentifier,
        }), _cts.Token);

        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
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

                HandleMessage(sb.ToString());
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

    private void HandleMessage(string raw)
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

            // ActionCable protocol frames carry a top-level "type".
            if (root.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
            {
                switch (typeEl.GetString())
                {
                    case "ping":
                        return; // keepalive
                    case "welcome":
                        StatusMessage?.Invoke("Connected to Joystick.tv gateway");
                        return;
                    case "confirm_subscription":
                        SubscriptionConfirmed?.Invoke();
                        return;
                    case "reject_subscription":
                        SubscriptionRejected?.Invoke();
                        return;
                    case "disconnect":
                        Disconnected?.Invoke("Gateway requested disconnect");
                        return;
                }
            }

            // Data frames carry a "message" object with an "event" discriminator.
            if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var evt = message.TryGetProperty("event", out var evtEl) && evtEl.ValueKind == JsonValueKind.String
                ? evtEl.GetString()
                : null;

            // Clone so the element stays valid after the JsonDocument is disposed.
            var owned = message.Clone();

            switch (evt)
            {
                case "ChatMessage":
                    ChatMessageReceived?.Invoke(owned);
                    break;
                case "UserPresence":
                    UserPresenceReceived?.Invoke(owned);
                    break;
                case "StreamEvent":
                    StreamEventReceived?.Invoke(owned);
                    break;
            }
        }
    }

    /// <summary>Sends a public chat message to the given channel (requires SendMessage permission).</summary>
    public Task SendChatMessageAsync(string channelId, string text, CancellationToken ct = default)
    {
        var data = JsonSerializer.Serialize(new { action = "send_message", text, channelId });
        var frame = JsonSerializer.Serialize(new
        {
            command = "message",
            identifier = JoystickConstants.GatewayIdentifier,
            data,
        });
        return SendRawAsync(frame, ct);
    }

    private async Task SendRawAsync(string payload, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open)
        {
            return;
        }
        var bytes = Encoding.UTF8.GetBytes(payload);
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
            if (_receiveLoop != null)
            {
                await Task.WhenAny(_receiveLoop, Task.Delay(1000));
            }
        }
        catch { }

        _ws?.Dispose();
        _cts?.Dispose();
    }
}
