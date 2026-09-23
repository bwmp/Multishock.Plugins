using System.Text.Json;
using FanslyIntegration.Protocol;

namespace FanslyIntegration.Services;

/// <summary>
/// Owns the FanslySocketClient lifecycle and translates raw gateway frames into typed plugin
/// events. Resolves the streamer's account/chat-room id via the REST client before connecting,
/// and handles auto-reconnect.
///
/// The field extraction below is deliberately defensive: Fansly's payload field names are not
/// documented, so each getter tries the most likely names and falls back cleanly.
/// </summary>
public class FanslyConnectionService : IAsyncDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly FanslySettingsService _settings;
    private readonly FanslyApiClient _api;

    private FanslySocketClient? _client;
    private CancellationTokenSource? _cts;
    private bool _userDisconnected;

    public bool IsConnected => _client?.IsConnected == true;
    public bool IsConnecting { get; private set; }

    public event Action<bool>? ConnectionStateChanged;
    public event Action<string>? ErrorOccurred;
    public event Action<string>? OnStatusMessage;

    public event Action<FanslyTipEvent>? OnTip;
    public event Action<FanslyChatEvent>? OnChat;
    public event Action<FanslyFollowEvent>? OnFollow;
    public event Action<FanslySubscribeEvent>? OnSubscribe;
    public event Action<FanslyStreamStateEvent>? OnStreamState;

    public FanslyConnectionService(FanslySettingsService settings, FanslyApiClient api)
    {
        _settings = settings;
        _api = api;
    }

    public async Task<bool> ConnectAsync()
    {
        if (IsConnecting || IsConnected)
        {
            return false;
        }

        if (!_settings.HasCredentials)
        {
            ErrorOccurred?.Invoke("Enter your Fansly account token and username first.");
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

            OnStatusMessage?.Invoke("Resolving Fansly account...");
            var accountId = await _api.GetAccountIdAsync(ct);
            if (string.IsNullOrEmpty(accountId))
            {
                ErrorOccurred?.Invoke("Could not resolve that Fansly username. Check the username and token.");
                return false;
            }

            var chatRoomId = await _api.GetChatRoomIdAsync(accountId, ct);
            if (string.IsNullOrEmpty(chatRoomId))
            {
                ErrorOccurred?.Invoke("No live chat room found. Start your stream, then connect.");
                return false;
            }

            var client = new FanslySocketClient(_settings.AuthToken, chatRoomId);
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

    private void WireClientEvents(FanslySocketClient client)
    {
        client.Connected += () => OnStatusMessage?.Invoke("Connected to Fansly gateway");
        client.StatusMessage += m => OnStatusMessage?.Invoke(m);
        client.Disconnected += HandleDisconnected;
        client.ChatMessageReceived += HandleChatMessage;
        client.AccountEventReceived += HandleAccountEvent;
        client.UnknownFrameReceived += (type, raw) =>
            OnStatusMessage?.Invoke($"Unmapped frame t={type}: {Truncate(raw, 200)}");
    }

    /// <summary>
    /// A chat-room frame can be a plain chat message or a tip-carrying message. We branch on a
    /// "type" discriminator when present, otherwise treat it as chat.
    /// </summary>
    private void HandleChatMessage(JsonElement payload)
    {
        var userName = FirstString(payload, "username", "senderUsername");
        var displayName = FirstString(payload, "displayName", "senderDisplayName");
        if (string.IsNullOrEmpty(displayName)) displayName = userName;

        var text = FirstString(payload, "content", "text", "message");
        var senderId = FirstString(payload, "senderId", "accountId");
        var messageId = FirstString(payload, "id", "messageId");

        var tipAmount = ExtractTipAmount(payload);
        if (tipAmount > 0)
        {
            OnTip?.Invoke(new FanslyTipEvent
            {
                UserName = userName,
                DisplayName = displayName,
                Amount = tipAmount,
                Message = text,
            });
            return;
        }

        var (command, arg) = ParseBotCommand(text);
        OnChat?.Invoke(new FanslyChatEvent
        {
            UserName = userName,
            DisplayName = displayName,
            Text = text,
            BotCommand = command,
            BotCommandArg = arg,
            SenderId = senderId,
            MessageId = messageId,
        });
    }

    /// <summary>Account/feed frames carry follows and subscriptions, discriminated by a "type" field.</summary>
    private void HandleAccountEvent(JsonElement payload)
    {
        var type = FirstString(payload, "type", "event").ToLowerInvariant();
        var userName = FirstString(payload, "username", "followerUsername", "subscriberUsername");
        var displayName = FirstString(payload, "displayName");
        if (string.IsNullOrEmpty(displayName)) displayName = userName;

        if (type.Contains("follow"))
        {
            OnFollow?.Invoke(new FanslyFollowEvent { UserName = userName, DisplayName = displayName });
        }
        else if (type.Contains("subscri"))
        {
            OnSubscribe?.Invoke(new FanslySubscribeEvent
            {
                UserName = userName,
                DisplayName = displayName,
                Tier = FirstString(payload, "tier", "tierName"),
                IsGifted = FirstBool(payload, "isGift", "gifted"),
            });
        }
        else if (type.Contains("streamstart") || type == "start")
        {
            OnStreamState?.Invoke(new FanslyStreamStateEvent { State = FanslyStreamState.Started });
        }
        else if (type.Contains("streamend") || type == "end" || type.Contains("stop"))
        {
            OnStreamState?.Invoke(new FanslyStreamStateEvent { State = FanslyStreamState.Ended });
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

    // ----- payload helpers -----

    /// <summary>
    /// Pulls a tip amount out of a chat message. Fansly reports tip amounts in cents/hundredths,
    /// so we normalise to whole tokens. Returns 0 when the message is not a tip.
    /// </summary>
    private static int ExtractTipAmount(JsonElement payload)
    {
        foreach (var key in new[] { "tipAmount", "tip", "amount" })
        {
            if (payload.TryGetProperty(key, out var v))
            {
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var cents) && cents > 0)
                {
                    // Amounts are in hundredths of a token; round to the nearest whole token.
                    return (int)Math.Round(cents / 1000.0);
                }
                if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s) && s > 0)
                {
                    return (int)Math.Round(s / 1000.0);
                }
            }
        }
        return 0;
    }

    private static (string command, string arg) ParseBotCommand(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith('!'))
        {
            return ("", "");
        }

        var trimmed = text.TrimStart()[1..];
        var spaceIdx = trimmed.IndexOf(' ');
        return spaceIdx < 0
            ? (trimmed, "")
            : (trimmed[..spaceIdx], trimmed[(spaceIdx + 1)..].Trim());
    }

    private static string FirstString(JsonElement el, params string[] props)
    {
        if (el.ValueKind != JsonValueKind.Object) return "";
        foreach (var prop in props)
        {
            if (el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString();
                if (!string.IsNullOrEmpty(s)) return s;
            }
        }
        return "";
    }

    private static bool FirstBool(JsonElement el, params string[] props)
    {
        if (el.ValueKind != JsonValueKind.Object) return false;
        foreach (var prop in props)
        {
            if (el.TryGetProperty(prop, out var v))
            {
                if (v.ValueKind == JsonValueKind.True) return true;
                if (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b)) return b;
            }
        }
        return false;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

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
