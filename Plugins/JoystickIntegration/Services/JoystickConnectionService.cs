using System.Text.Json;
using JoystickIntegration.Protocol;

namespace JoystickIntegration.Services;

/// <summary>
/// Owns the JoystickCableClient lifecycle and translates raw gateway messages into
/// typed plugin events. Handles auto-reconnect and remembers the streamer's channelId
/// (needed to send chat messages back).
/// </summary>
public class JoystickConnectionService : IAsyncDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly JoystickSettingsService _settings;
    private readonly JoystickAuthService _auth;

    private JoystickCableClient? _client;
    private CancellationTokenSource? _cts;
    private bool _userDisconnected;

    public bool IsConnected => _client?.IsConnected == true;
    public bool IsConnecting { get; private set; }

    /// <summary>The most recently seen channelId, used for sending chat back.</summary>
    public string? ChannelId { get; private set; }

    public event Action<bool>? ConnectionStateChanged;
    public event Action<string>? ErrorOccurred;
    public event Action<string>? OnStatusMessage;

    public event Action<JoystickTipEvent>? OnTip;
    public event Action<JoystickTipGoalEvent>? OnTipGoal;
    public event Action<JoystickChatEvent>? OnChat;
    public event Action<JoystickFollowEvent>? OnFollow;
    public event Action<JoystickSubscribeEvent>? OnSubscribe;
    public event Action<JoystickWheelSpinEvent>? OnWheelSpin;
    public event Action<JoystickPresenceEvent>? OnPresence;
    public event Action<JoystickStreamStateEvent>? OnStreamState;

    public JoystickConnectionService(JoystickSettingsService settings, JoystickAuthService auth)
    {
        _settings = settings;
        _auth = auth;
    }

    /// <summary>Sends a public chat message to the connected channel (needs SendMessage permission).</summary>
    public async Task<bool> SendChatAsync(string text)
    {
        var client = _client;
        if (client == null || !client.IsConnected || string.IsNullOrEmpty(ChannelId))
        {
            return false;
        }

        try
        {
            await client.SendChatMessageAsync(ChannelId, text);
            return true;
        }
        catch (Exception ex)
        {
            OnStatusMessage?.Invoke($"Failed to send chat message: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ConnectAsync()
    {
        if (IsConnecting || IsConnected)
        {
            return false;
        }

        if (!_settings.HasCredentials)
        {
            ErrorOccurred?.Invoke("Enter your bot Client ID and Client Secret first.");
            return false;
        }

        _userDisconnected = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        return await TryConnectOnceAsync(_cts.Token);
    }

    private async Task<bool> TryConnectOnceAsync(CancellationToken ct)
    {
        IsConnecting = true;
        ConnectionStateChanged?.Invoke(false);

        try
        {
            if (_client != null)
            {
                await _client.DisposeAsync();
                _client = null;
            }

            // Keep the OAuth access token fresh for REST calls (the websocket itself uses the basic key).
            await _auth.EnsureFreshTokenAsync(ct);

            var client = new JoystickCableClient(_settings.BasicKey);
            WireClientEvents(client);
            _client = client;

            await client.ConnectAsync(ct);
            ConnectionStateChanged?.Invoke(true);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Connection failed: {ex.Message}");
            return false;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    public async Task DisconnectAsync()
    {
        _userDisconnected = true;
        _cts?.Cancel();
        if (_client != null)
        {
            await _client.DisconnectAsync();
        }
        ConnectionStateChanged?.Invoke(false);
    }

    private void WireClientEvents(JoystickCableClient client)
    {
        client.StatusMessage += m => OnStatusMessage?.Invoke(m);
        client.SubscriptionConfirmed += () => OnStatusMessage?.Invoke("Subscribed to gateway; listening for events.");
        client.SubscriptionRejected += () =>
            ErrorOccurred?.Invoke("Gateway rejected the subscription. Check that the bot is installed and credentials are correct.");
        client.Disconnected += HandleDisconnected;
        client.ChatMessageReceived += HandleChat;
        client.UserPresenceReceived += HandlePresence;
        client.StreamEventReceived += HandleStreamEvent;
    }

    private void HandleChat(JsonElement message)
    {
        var channelId = GetString(message, "channelId");
        if (!string.IsNullOrEmpty(channelId))
        {
            ChannelId = channelId;
        }

        message.TryGetProperty("author", out var author);

        OnChat?.Invoke(new JoystickChatEvent
        {
            UserName = GetString(author, "username"),
            DisplayName = GetString(author, "username"),
            Text = GetString(message, "text"),
            BotCommand = GetString(message, "botCommand"),
            BotCommandArg = GetString(message, "botCommandArg"),
            ChannelId = channelId,
            MessageId = GetString(message, "messageId"),
            IsStreamer = GetBool(author, "isStreamer"),
            IsModerator = GetBool(author, "isModerator"),
            IsSubscriber = GetBool(author, "isSubscriber"),
        });
    }

    private void HandlePresence(JsonElement message)
    {
        var type = GetString(message, "type");
        OnPresence?.Invoke(new JoystickPresenceEvent
        {
            UserName = GetString(message, "text"),
            Entered = type.Equals("enter_stream", StringComparison.OrdinalIgnoreCase),
        });
    }

    private void HandleStreamEvent(JsonElement message)
    {
        var channelId = GetString(message, "channelId");
        if (!string.IsNullOrEmpty(channelId))
        {
            ChannelId = channelId;
        }

        var type = GetString(message, "type");
        var meta = ParseMetadata(message);

        switch (type)
        {
            case "Tipped":
            case "TipMenu":
                OnTip?.Invoke(new JoystickTipEvent
                {
                    UserName = MetaString(meta, "who"),
                    Tokens = MetaInt(meta, "how_much"),
                    TipMenuItem = MetaString(meta, "tip_menu_item"),
                });
                break;

            case "TipGoalMet":
                OnTipGoal?.Invoke(new JoystickTipGoalEvent
                {
                    UserName = MetaString(meta, "who"),
                    Tokens = MetaInt(meta, "how_much"),
                });
                break;

            case "Followed":
                OnFollow?.Invoke(new JoystickFollowEvent { UserName = MetaString(meta, "who") });
                break;

            case "GiftedSubscriptions":
                OnSubscribe?.Invoke(new JoystickSubscribeEvent
                {
                    UserName = MetaString(meta, "who"),
                    Quantity = Math.Max(1, MetaInt(meta, "how_much")),
                    IsGifted = true,
                });
                break;

            case "WheelSpinClaimed":
                OnWheelSpin?.Invoke(new JoystickWheelSpinEvent
                {
                    UserName = MetaString(meta, "who"),
                    Prize = MetaString(meta, "prize"),
                    Tokens = MetaInt(meta, "how_much"),
                });
                break;

            case "Started":
                OnStreamState?.Invoke(new JoystickStreamStateEvent { State = JoystickStreamState.Started });
                break;
            case "StreamEnding":
                OnStreamState?.Invoke(new JoystickStreamStateEvent { State = JoystickStreamState.Ending });
                break;
            case "StreamResuming":
                OnStreamState?.Invoke(new JoystickStreamStateEvent { State = JoystickStreamState.Resuming });
                break;
            case "Ended":
                OnStreamState?.Invoke(new JoystickStreamStateEvent { State = JoystickStreamState.Ended });
                break;
        }
    }

    private void HandleDisconnected(string reason)
    {
        ConnectionStateChanged?.Invoke(false);
        OnStatusMessage?.Invoke(reason);

        if (_userDisconnected || !_settings.AutoReconnect)
        {
            return;
        }

        var ct = _cts?.Token ?? CancellationToken.None;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ReconnectDelay, ct);
                if (IsConnected || _userDisconnected)
                {
                    return;
                }
                OnStatusMessage?.Invoke("Reconnecting...");
                await TryConnectOnceAsync(ct);
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);
    }

    // ----- JSON helpers -----

    private static string GetString(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object &&
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    private static bool GetBool(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object &&
        el.TryGetProperty(prop, out var v) &&
        (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b) && b));

    /// <summary>The metadata field is a JSON *string*; parse it into a dictionary.</summary>
    private static Dictionary<string, JsonElement> ParseMetadata(JsonElement message)
    {
        if (!message.TryGetProperty("metadata", out var metaEl))
        {
            return [];
        }

        try
        {
            if (metaEl.ValueKind == JsonValueKind.String)
            {
                var s = metaEl.GetString();
                if (string.IsNullOrWhiteSpace(s)) return [];
                using var doc = JsonDocument.Parse(s);
                return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            }
            if (metaEl.ValueKind == JsonValueKind.Object)
            {
                return metaEl.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            }
        }
        catch
        {
            // malformed metadata
        }
        return [];
    }

    private static string MetaString(Dictionary<string, JsonElement> meta, string key) =>
        meta.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int MetaInt(Dictionary<string, JsonElement> meta, string key)
    {
        if (!meta.TryGetValue(key, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
        return 0;
    }

    public async ValueTask DisposeAsync()
    {
        _userDisconnected = true;
        _cts?.Cancel();
        if (_client != null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
        _cts?.Dispose();
    }
}
