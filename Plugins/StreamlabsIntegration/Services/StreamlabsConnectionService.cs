using System.Text.Json;
using Microsoft.Extensions.Logging;
using StreamlabsIntegration.Models;
using StreamlabsIntegration.Protocol;

namespace StreamlabsIntegration.Services;

/// <summary>
/// Owns the Streamlabs socket connection lifecycle: connecting with the socket token,
/// parsing incoming "event" frames into <see cref="StreamlabsEvent"/>s, raising them, and
/// automatically reconnecting on unexpected drops.
/// </summary>
public class StreamlabsConnectionService : IDisposable
{
    private const int ReconnectDelayMs = 5000;

    private readonly StreamlabsSettingsService _settings;
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
    public event Action<StreamlabsEvent>? OnEvent;

    public StreamlabsConnectionService(StreamlabsSettingsService settings, ILogger<StreamlabsConnectionService>? logger = null)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task ConnectAsync()
    {
        if (IsConnected || IsConnecting) return;

        if (!_settings.HasToken)
        {
            ErrorOccurred?.Invoke("No socket token set. Paste your Streamlabs Socket API Token first.");
            return;
        }

        IsConnecting = true;
        _intentionalDisconnect = false;
        _cts = new CancellationTokenSource();
        OnStatusMessage?.Invoke("Connecting to Streamlabs...");

        try
        {
            await OpenSocketAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            IsConnecting = false;
            _logger?.LogError(ex, "Failed to connect to Streamlabs");
            ErrorOccurred?.Invoke($"Failed to connect: {ex.Message}");
            RaiseState(false);
        }
    }

    private async Task OpenSocketAsync(CancellationToken ct)
    {
        var client = new SocketIoClient(StreamlabsConstants.BuildSocketUri(_settings.SocketToken));
        client.Connected += OnSocketConnected;
        client.EventReceived += OnSocketEvent;
        client.Disconnected += OnSocketDisconnected;
        client.Log += msg => _logger?.LogDebug("[Streamlabs socket] {Message}", msg);

        // Dispose any previous (dropped) client before replacing it, so reconnects don't leak sockets.
        SocketIoClient? previous;
        lock (_lock) { previous = _client; _client = client; }
        previous?.Dispose();

        await client.ConnectAsync(ct);
    }

    private void OnSocketConnected()
    {
        IsConnecting = false;
        RaiseState(true);
        OnStatusMessage?.Invoke("Connected! Listening for events...");
    }

    private void OnSocketDisconnected(string reason)
    {
        var wasConnected = IsConnected;
        IsConnecting = false;
        RaiseState(false);

        if (_intentionalDisconnect)
        {
            OnStatusMessage?.Invoke("Disconnected from Streamlabs");
            return;
        }

        if (wasConnected)
            ErrorOccurred?.Invoke($"Connection lost: {reason}");
        else
            ErrorOccurred?.Invoke($"Could not connect: {reason}. Check your socket token.");

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
        // The Streamlabs socket only emits a single listener called "event".
        if (!name.Equals("event", StringComparison.OrdinalIgnoreCase)) return;
        if (data.ValueKind != JsonValueKind.Object) return;

        try
        {
            foreach (var evt in ParseEvent(data))
                OnEvent?.Invoke(evt);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to parse Streamlabs event");
        }
    }

    /// <summary>
    /// Parses a raw Streamlabs socket payload (<c>{ type, message: [...], for }</c>) into zero or
    /// more normalized events. The <c>message</c> array typically holds one item but can hold many.
    /// </summary>
    private static IEnumerable<StreamlabsEvent> ParseEvent(JsonElement root)
    {
        var rawType = GetString(root, "type");
        var platform = GetString(root, "for");
        var normalized = NormalizeType(rawType);
        if (normalized is null) yield break;

        if (!root.TryGetProperty("message", out var messages) || messages.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var m in messages.EnumerateArray())
        {
            if (m.ValueKind != JsonValueKind.Object) continue;

            yield return new StreamlabsEvent
            {
                Type = normalized,
                RawType = rawType,
                Platform = platform,
                Name = FirstString(m, "name", "from", "display_name", "username"),
                Message = FirstString(m, "message", "comment"),
                Amount = ExtractAmount(normalized, m),
                FormattedAmount = FirstString(m, "formatted_amount", "formattedAmount", "displayString"),
                Currency = GetString(m, "currency"),
                SubPlan = FirstString(m, "sub_plan", "sub_plan_name"),
                Id = FirstString(m, "_id", "id", "message_id"),
            };
        }
    }

    private static double ExtractAmount(string normalizedType, JsonElement m) => normalizedType switch
    {
        StreamlabsEventType.Donation => ReadNumber(m, "amount"),
        StreamlabsEventType.SuperChat => ReadNumber(m, "amount"),
        StreamlabsEventType.Bits => ReadNumber(m, "amount"),
        StreamlabsEventType.Subscription => ReadNumber(m, "months"),
        StreamlabsEventType.Host => ReadNumber(m, "viewers"),
        StreamlabsEventType.Raid => ReadNumber(m, "raiders"),
        _ => 0,
    };

    private static string? NormalizeType(string rawType) => rawType.ToLowerInvariant() switch
    {
        "donation" => StreamlabsEventType.Donation,
        "subscription" or "resub" or "submysterygift" => StreamlabsEventType.Subscription,
        "follow" => StreamlabsEventType.Follow,
        "bits" => StreamlabsEventType.Bits,
        "host" => StreamlabsEventType.Host,
        "raid" => StreamlabsEventType.Raid,
        "superchat" => StreamlabsEventType.SuperChat,
        _ => null,
    };

    public async Task DisconnectAsync()
    {
        _intentionalDisconnect = true;
        _cts?.Cancel();

        SocketIoClient? client;
        lock (_lock) { client = _client; _client = null; }

        if (client != null)
        {
            await client.CloseAsync();
            client.Dispose();
        }

        RaiseState(false);
        OnStatusMessage?.Invoke("Disconnected from Streamlabs");
    }

    private void RaiseState(bool connected)
    {
        IsConnected = connected;
        if (connected) IsConnecting = false;
        ConnectionStateChanged?.Invoke(connected);
    }

    // ----- JSON helpers (defensive: Streamlabs mixes string and numeric fields) -----

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

    public void Dispose()
    {
        _cts?.Cancel();
        _client?.Dispose();
        _cts?.Dispose();
    }
}
