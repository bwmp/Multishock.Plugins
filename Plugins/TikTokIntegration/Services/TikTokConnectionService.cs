using TikTokIntegration.Protocol;

namespace TikTokIntegration.Services;

/// <summary>
/// Owns the TikTokLiveClient lifecycle and translates raw webcast messages into
/// plugin-level events. Handles gift streak resolution, follow deduplication,
/// wait-for-live retry, and automatic reconnects.
/// </summary>
public class TikTokConnectionService : IAsyncDisposable
{
    private static readonly TimeSpan WaitForLiveInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly TikTokSettingsService _settings;
    private readonly TikTokSignProvider _signProvider = new();

    private TikTokLiveClient? _client;
    private CancellationTokenSource? _retryCts;
    private bool _userDisconnected;
    private readonly HashSet<ulong> _seenFollowers = [];

    public bool IsConnected => _client?.IsConnected == true;

    public bool IsConnecting { get; private set; }

    public bool IsWaitingForLive { get; private set; }

    public string? ConnectedUsername { get; private set; }

    public string? RoomTitle => _client?.Room?.Title;

    public int ViewerCount { get; private set; }

    public IReadOnlyList<TikTokGiftInfo> AvailableGifts => _client?.AvailableGifts ?? [];

    public event Action<bool>? ConnectionStateChanged;
    public event Action<string>? ErrorOccurred;
    public event Action<string>? OnStatusMessage;
    public event Action? GiftCatalogUpdated;

    /// <summary>Fired once per completed gift (streaks resolve to a single event with the final count).</summary>
    public event Action<TikTokGiftEvent>? OnGift;

    /// <summary>Fired for every streak tick of a streakable gift (in-progress combos).</summary>
    public event Action<TikTokGiftEvent>? OnGiftCombo;

    public event Action<TikTokChatEvent>? OnChat;
    public event Action<TikTokLikeEvent>? OnLike;
    public event Action<TikTokSocialEvent>? OnFollow;
    public event Action<TikTokSocialEvent>? OnShare;
    public event Action<TikTokSocialEvent>? OnJoin;
    public event Action<TikTokSubscribeEvent>? OnSubscribe;
    public event Action<TikTokEnvelopeEvent>? OnEnvelope;
    public event Action<int>? OnViewerCount;
    public event Action? OnStreamEnded;

    public TikTokConnectionService(TikTokSettingsService settings)
    {
        _settings = settings;
    }

    public async Task<bool> ConnectAsync(string? username = null)
    {
        if (IsConnecting || IsConnected)
        {
            return false;
        }

        var target = TikTokLiveClient.NormalizeUniqueId(username ?? _settings.Username);
        if (string.IsNullOrWhiteSpace(target))
        {
            ErrorOccurred?.Invoke("Enter the TikTok @username to connect to");
            return false;
        }

        _userDisconnected = false;
        CancelRetry();
        _retryCts = new CancellationTokenSource();

        var connected = await TryConnectOnceAsync(target, _retryCts.Token);
        if (!connected && IsWaitingForLive)
        {
            // Offline and wait-for-live is on: keep retrying in the background
            _ = RunWaitForLiveLoopAsync(target, _retryCts.Token);
        }

        return connected;
    }

    public async Task DisconnectAsync()
    {
        _userDisconnected = true;
        CancelRetry();
        SetWaitingForLive(false);

        if (_client != null)
        {
            await _client.DisconnectAsync("Disconnected");
        }
    }

    private async Task<bool> TryConnectOnceAsync(string username, CancellationToken ct)
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

            _signProvider.ApiKey = string.IsNullOrWhiteSpace(_settings.EulerApiKey) ? null : _settings.EulerApiKey;
            _signProvider.CustomHost = string.IsNullOrWhiteSpace(_settings.CustomSignServer) ? null : _settings.CustomSignServer;

            var client = new TikTokLiveClient(
                username,
                _signProvider,
                string.IsNullOrWhiteSpace(_settings.SessionId) ? null : _settings.SessionId);

            WireClientEvents(client);
            _client = client;
            _seenFollowers.Clear();
            ViewerCount = 0;

            await client.ConnectAsync(ct);

            ConnectedUsername = client.UniqueId;
            SetWaitingForLive(false);
            ConnectionStateChanged?.Invoke(true);
            GiftCatalogUpdated?.Invoke();
            return true;
        }
        catch (UserOfflineException ex)
        {
            OnStatusMessage?.Invoke(ex.Message);
            if (_settings.WaitForLive && !_userDisconnected)
            {
                SetWaitingForLive(true);
            }
            else
            {
                ErrorOccurred?.Invoke(ex.Message);
            }
            return false;
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

    private async Task RunWaitForLiveLoopAsync(string username, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && IsWaitingForLive && !IsConnected)
            {
                await Task.Delay(WaitForLiveInterval, ct);

                if (ct.IsCancellationRequested || IsConnected || _userDisconnected)
                {
                    break;
                }

                OnStatusMessage?.Invoke($"Checking if @{username} is live...");
                await TryConnectOnceAsync(username, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Retry loop cancelled
        }
    }

    private void WireClientEvents(TikTokLiveClient client)
    {
        client.StatusMessage += message => OnStatusMessage?.Invoke(message);

        client.ChatReceived += chat => OnChat?.Invoke(new TikTokChatEvent
        {
            UserName = chat.User.UniqueId,
            DisplayName = chat.User.Nickname,
            Message = chat.Comment,
        });

        client.GiftReceived += HandleGift;

        client.LikeReceived += like => OnLike?.Invoke(new TikTokLikeEvent
        {
            UserName = like.User.UniqueId,
            DisplayName = like.User.Nickname,
            LikeCount = like.LikeCount,
            TotalLikeCount = like.TotalLikeCount,
        });

        client.SocialReceived += HandleSocial;

        client.MemberJoined += member => OnJoin?.Invoke(new TikTokSocialEvent
        {
            UserName = member.User.UniqueId,
            DisplayName = member.User.Nickname,
        });

        client.SubscribeReceived += sub => OnSubscribe?.Invoke(new TikTokSubscribeEvent
        {
            UserName = sub.User.UniqueId,
            DisplayName = sub.User.Nickname,
            SubMonth = sub.SubMonth,
        });

        client.EnvelopeReceived += envelope => OnEnvelope?.Invoke(new TikTokEnvelopeEvent
        {
            Coins = envelope.Coins,
        });

        client.ViewerCountChanged += count =>
        {
            ViewerCount = count;
            OnViewerCount?.Invoke(count);
        };

        client.StreamEnded += () =>
        {
            OnStatusMessage?.Invoke("Stream ended");
            OnStreamEnded?.Invoke();
        };

        client.Disconnected += HandleDisconnected;
    }

    private void HandleGift(WebcastGift gift)
    {
        var giftEvent = new TikTokGiftEvent
        {
            UserName = gift.User.UniqueId,
            DisplayName = gift.User.Nickname,
            GiftId = gift.GiftId,
            GiftName = gift.GiftName,
            DiamondCount = gift.DiamondCount,
            RepeatCount = gift.RepeatCount,
            GiftImageUrl = gift.GiftImageUrl,
            IsStreakable = gift.IsStreakable,
            IsStreakEnd = gift.RepeatEnd,
        };

        // Streakable gifts (combos) fire a message per tick and one final message with
        // RepeatEnd set carrying the full count. Only the final message counts as "the gift";
        // intermediate ticks are exposed separately via OnGiftCombo.
        if (giftEvent.IsStreakable && !giftEvent.IsStreakEnd)
        {
            OnGiftCombo?.Invoke(giftEvent);
        }
        else
        {
            OnGift?.Invoke(giftEvent);
        }
    }

    private void HandleSocial(WebcastSocial social)
    {
        var socialEvent = new TikTokSocialEvent
        {
            UserName = social.User.UniqueId,
            DisplayName = social.User.Nickname,
        };

        var displayType = social.Event.DisplayType;
        if (displayType.Contains("follow", StringComparison.OrdinalIgnoreCase))
        {
            // TikTok re-sends follow messages for the same viewer within a stream
            lock (_seenFollowers)
            {
                if (social.User.UserId != 0 && !_seenFollowers.Add(social.User.UserId))
                {
                    return;
                }
            }
            OnFollow?.Invoke(socialEvent);
        }
        else if (displayType.Contains("share", StringComparison.OrdinalIgnoreCase))
        {
            OnShare?.Invoke(socialEvent);
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

        var username = ConnectedUsername ?? TikTokLiveClient.NormalizeUniqueId(_settings.Username);
        if (string.IsNullOrEmpty(username))
        {
            return;
        }

        var ct = _retryCts?.Token ?? CancellationToken.None;
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
                var connected = await TryConnectOnceAsync(username, ct);
                if (!connected && IsWaitingForLive)
                {
                    await RunWaitForLiveLoopAsync(username, ct);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);
    }

    private void SetWaitingForLive(bool waiting)
    {
        if (IsWaitingForLive != waiting)
        {
            IsWaitingForLive = waiting;
            ConnectionStateChanged?.Invoke(IsConnected);
        }
    }

    private void CancelRetry()
    {
        _retryCts?.Cancel();
        _retryCts?.Dispose();
        _retryCts = null;
    }

    public async ValueTask DisposeAsync()
    {
        _userDisconnected = true;
        CancelRetry();
        if (_client != null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
    }
}
