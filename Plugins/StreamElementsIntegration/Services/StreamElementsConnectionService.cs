using System.Text.Json;
using Microsoft.Extensions.Logging;
using StreamElementsIntegration.Models;
using StreamElementsIntegration.Protocol;

namespace StreamElementsIntegration.Services;

/// <summary>
/// Owns the StreamElements realtime connection lifecycle: connecting, authenticating with the JWT,
/// parsing "event"/"event:test" frames into <see cref="StreamElementsEvent"/>s, raising them, and
/// automatically reconnecting on unexpected drops.
///
/// Unlike Streamlabs, "connected" here means <em>authenticated</em>: the socket first connects,
/// we emit <c>authenticate {method:"jwt", token}</c>, and only the server's <c>authenticated</c>
/// event flips <see cref="IsConnected"/> to true.
/// </summary>
public class StreamElementsConnectionService : IDisposable
{
    private const int ReconnectDelayMs = 5000;

    private readonly StreamElementsSettingsService _settings;
    private readonly ILogger? _logger;
    private readonly object _lock = new();

    private SocketIoClient? _client;
    private CancellationTokenSource? _cts;
    private bool _intentionalDisconnect;

    public bool IsConnected { get; private set; }
    public bool IsConnecting { get; private set; }

    public event Action<bool>? ConnectionStateChanged;
    public event Action<string>? ErrorOccurred;
    public event Action<string>? OnStatusMessage;
    public event Action<StreamElementsEvent>? OnEvent;

    public StreamElementsConnectionService(StreamElementsSettingsService settings, ILogger<StreamElementsConnectionService>? logger = null)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task ConnectAsync()
    {
        if (IsConnected || IsConnecting) return;

        if (!_settings.HasToken)
        {
            ErrorOccurred?.Invoke("No JWT token set. Paste your StreamElements JWT token first.");
            return;
        }

        IsConnecting = true;
        _intentionalDisconnect = false;
        _cts = new CancellationTokenSource();
        OnStatusMessage?.Invoke("Connecting to StreamElements...");

        try
        {
            await OpenSocketAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            IsConnecting = false;
            _logger?.LogError(ex, "Failed to connect to StreamElements");
            ErrorOccurred?.Invoke($"Failed to connect: {ex.Message}");
            RaiseState(false);
        }
    }

    private async Task OpenSocketAsync(CancellationToken ct)
    {
        var client = new SocketIoClient(StreamElementsConstants.SocketUri);
        client.Connected += OnSocketConnected;
        client.EventReceived += OnSocketEvent;
        client.Disconnected += OnSocketDisconnected;
        client.Log += msg => _logger?.LogDebug("[StreamElements socket] {Message}", msg);

        // Dispose any previous (dropped) client before replacing it, so reconnects don't leak sockets.
        SocketIoClient? previous;
        lock (_lock) { previous = _client; _client = client; }
        previous?.Dispose();

        await client.ConnectAsync(ct);
    }

    private void OnSocketConnected()
    {
        // Namespace connected — now authenticate with the JWT.
        OnStatusMessage?.Invoke("Authenticating...");
        var cts = _cts;
        if (cts is null) return;

        SocketIoClient? client;
        lock (_lock) client = _client;
        if (client is null) return;

        _ = client.EmitAsync("authenticate", new { method = "jwt", token = _settings.JwtToken }, cts.Token);
    }

    private void OnSocketDisconnected(string reason)
    {
        var wasConnected = IsConnected;
        IsConnecting = false;
        RaiseState(false);

        if (_intentionalDisconnect)
        {
            OnStatusMessage?.Invoke("Disconnected from StreamElements");
            return;
        }

        if (wasConnected)
            ErrorOccurred?.Invoke($"Connection lost: {reason}");
        else
            ErrorOccurred?.Invoke($"Could not connect: {reason}");

        if (_settings.AutoReconnect)
            _ = ReconnectAfterDelayAsync();
    }

    private async Task ReconnectAfterDelayAsync()
    {
        var cts = _cts;
        if (cts is null || cts.IsCancellationRequested) return;

        OnStatusMessage?.Invoke($"Reconnecting in {ReconnectDelayMs / 1000}s...");
        try
        {
            await Task.Delay(ReconnectDelayMs, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_intentionalDisconnect || cts.IsCancellationRequested) return;

        IsConnecting = true;
        try
        {
            await OpenSocketAsync(cts.Token);
        }
        catch (Exception ex)
        {
            IsConnecting = false;
            ErrorOccurred?.Invoke($"Reconnect failed: {ex.Message}");
            if (_settings.AutoReconnect) _ = ReconnectAfterDelayAsync();
        }
    }

    private void OnSocketEvent(string name, JsonElement data)
    {
        switch (name)
        {
            case "authenticated":
                IsConnecting = false;
                RaiseState(true);
                OnStatusMessage?.Invoke("Authenticated! Listening for events...");
                break;

            case "unauthorized":
                var msg = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                    ? m.GetString()
                    : "invalid or expired JWT token";
                ErrorOccurred?.Invoke($"Authentication failed: {msg}");
                _intentionalDisconnect = true; // don't auto-reconnect on a bad token
                _ = DisconnectInternalAsync();
                break;

            case "event":
                if (data.ValueKind == JsonValueKind.Object)
                    RaiseIfParsed(ParseRealEvent(data, isTest: false));
                break;

            case "event:test":
                if (data.ValueKind == JsonValueKind.Object)
                    RaiseIfParsed(ParseTestEvent(data));
                break;
        }
    }

    private void RaiseIfParsed(StreamElementsEvent? e)
    {
        if (e != null) OnEvent?.Invoke(e);
    }

    /// <summary>Parses a real event: <c>{ type, provider, data: { ... } }</c>.</summary>
    private static StreamElementsEvent? ParseRealEvent(JsonElement root, bool isTest)
    {
        var normalized = NormalizeType(GetString(root, "type"));
        if (normalized is null) return null;

        var provider = GetString(root, "provider");
        var data = root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? d : root;
        return BuildEvent(normalized, provider, data, isTest);
    }

    /// <summary>
    /// Parses a dashboard test event: <c>{ listener: "tip-latest", event: { ... } }</c>.
    /// The inner event object uses the same field names as a real event's data object.
    /// </summary>
    private static StreamElementsEvent? ParseTestEvent(JsonElement root)
    {
        var listener = GetString(root, "listener"); // e.g. "tip-latest"
        var rawType = listener.Contains('-') ? listener[..listener.IndexOf('-')] : listener;
        var normalized = NormalizeType(rawType);
        if (normalized is null) return null;

        var data = root.TryGetProperty("event", out var e) && e.ValueKind == JsonValueKind.Object ? e : root;
        return BuildEvent(normalized, GetString(data, "provider"), data, isTest: true);
    }

    private static StreamElementsEvent BuildEvent(string normalizedType, string provider, JsonElement data, bool isTest) => new()
    {
        Type = normalizedType,
        Provider = provider,
        Name = FirstString(data, "username", "displayName", "name"),
        Message = FirstString(data, "message"),
        Amount = ReadNumber(data, "amount"),
        Currency = FirstString(data, "currency"),
        Tier = FirstString(data, "tier"),
        Gifted = ReadBool(data, "gifted"),
        Sender = FirstString(data, "sender"),
        Item = ExtractItem(data),
        IsTest = isTest,
    };

    private static string ExtractItem(JsonElement data)
    {
        // Store redemptions carry the item under "redemption" or a nested "item.name".
        var direct = FirstString(data, "redemption", "item");
        if (!string.IsNullOrEmpty(direct)) return direct;
        if (data.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object)
            return FirstString(item, "name");
        return "";
    }

    private static string? NormalizeType(string rawType) => rawType.ToLowerInvariant() switch
    {
        "tip" => StreamElementsEventType.Tip,
        "cheer" => StreamElementsEventType.Cheer,
        "follow" or "follower" => StreamElementsEventType.Follow,
        "subscriber" or "subscription" or "sub" => StreamElementsEventType.Subscriber,
        "host" => StreamElementsEventType.Host,
        "raid" => StreamElementsEventType.Raid,
        "redemption" or "redemptions" => StreamElementsEventType.Redemption,
        _ => null,
    };

    public async Task DisconnectAsync()
    {
        _intentionalDisconnect = true;
        await DisconnectInternalAsync();
        OnStatusMessage?.Invoke("Disconnected from StreamElements");
    }

    private async Task DisconnectInternalAsync()
    {
        _cts?.Cancel();

        SocketIoClient? client;
        lock (_lock) { client = _client; _client = null; }

        if (client != null)
        {
            await client.CloseAsync();
            client.Dispose();
        }

        RaiseState(false);
    }

    private void RaiseState(bool connected)
    {
        IsConnected = connected;
        if (connected) IsConnecting = false;
        ConnectionStateChanged?.Invoke(connected);
    }

    // ----- JSON helpers -----

    private static string GetString(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string FirstString(JsonElement obj, params string[] props)
    {
        foreach (var p in props)
        {
            if (obj.TryGetProperty(p, out var v))
            {
                if (v.ValueKind == JsonValueKind.String) { var s = v.GetString(); if (!string.IsNullOrEmpty(s)) return s; }
                else if (v.ValueKind == JsonValueKind.Number) return v.ToString();
            }
        }
        return "";
    }

    private static double ReadNumber(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String => double.TryParse(v.GetString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0,
            _ => 0,
        };
    }

    private static bool ReadBool(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;

    public void Dispose()
    {
        _cts?.Cancel();
        _client?.Dispose();
        _cts?.Dispose();
    }
}
