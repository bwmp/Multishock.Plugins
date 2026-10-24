using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TwitchIntegration.Models;

namespace TwitchIntegration.Services;

public class TwitchEventSubService : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly TwitchAuthService _authService;
    private readonly TwitchPerformanceProfiler _profiler;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _connectionCts;
    private Task? _receiveTask;
    private string? _sessionId;
    private string? _userId;
    private string? _oauthToken;
    private bool _useLocalCli;
    private int _keepaliveTimeoutSeconds = 10;
    private DateTime _lastMessageTime = DateTime.UtcNow;
    private Timer? _keepaliveTimer;
    private bool _isConnected;
    private bool _isConnecting;
    private bool _isSessionReconnect;
    private bool _usesManagedToken;
    private bool _authRejected;
    private CancellationTokenSource? _reconnectCts;
    private int _reconnectLoopActive;

    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
    ];

    public bool IsConnected => _isConnected && _webSocket?.State == WebSocketState.Open;

    public bool IsConnecting => _isConnecting;

    public string? CurrentUserName { get; private set; }

    public string? CurrentUserId => _userId;

    public event Action<bool>? ConnectionStateChanged;

    public event Action<string>? ErrorOccurred;

    public event Action<CheerEvent>? OnCheer;

    public event Action<SubscribeEvent>? OnSubscribe;

    public event Action<SubscriptionGiftEvent>? OnSubscriptionGift;

    public event Action<SubscriptionMessageEvent>? OnSubscriptionMessage;

    public event Action<FollowEvent>? OnFollow;

    public event Action<HypeTrainBeginEvent>? OnHypeTrainBegin;

    public event Action<HypeTrainProgressEvent>? OnHypeTrainProgress;

    public event Action<HypeTrainEndEvent>? OnHypeTrainEnd;

    public event Action<RaidEvent>? OnRaid;

    public event Action<ChannelPointRedemptionEvent>? OnChannelPointRedemption;

    public event Action<ChatMessageEvent>? OnChatMessage;

    public event Action<string>? OnStatusMessage;

    private const string LocalCliUserId = "12345";
    private const string LocalCliUserName = "Twitch CLI";

    public TwitchEventSubService(TwitchAuthService authService, TwitchPerformanceProfiler profiler)
    {
        _httpClient = new HttpClient();
        _authService = authService;
        _profiler = profiler;
    }

    private void ReportStatus(string message)
    {
        TwitchIntegrationPlugin.Logger?.LogInformation("[EventSub] {Message}", message);
        OnStatusMessage?.Invoke(message);
    }

    private void ReportError(string message)
    {
        TwitchIntegrationPlugin.Logger?.LogWarning("[EventSub] {Message}", message);
        ErrorOccurred?.Invoke(message);
    }

    private enum TokenProblem
    {
        None,
        Transient,
        Rejected,
        MissingScopes,
    }

    public async Task<(bool IsValid, string? UserId, string? UserName, string? Error)> ValidateTokenAsync(string oauthToken)
    {
        var (problem, userId, userName, error) = await CheckTokenAsync(oauthToken);
        return (problem == TokenProblem.None, userId, userName, error);
    }

    private async Task<(TokenProblem Problem, string? UserId, string? UserName, string? Error)> CheckTokenAsync(string oauthToken)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
            request.Headers.Add("Authorization", $"OAuth {oauthToken}");

            var response = await _httpClient.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return (TokenProblem.Rejected, null, null, "Invalid or expired OAuth token");
            }

            if (!response.IsSuccessStatusCode)
            {
                return (TokenProblem.Transient, null, null, $"Token validation failed: HTTP {(int)response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var validation = JsonSerializer.Deserialize<TokenValidationResponse>(content);

            if (validation == null)
            {
                return (TokenProblem.Transient, null, null, "Failed to parse token validation response");
            }

            // Check for required scopes
            var missingScopes = TwitchConstants.RequiredScopes
                .Where(s => !validation.Scopes.Contains(s))
                .ToList();

            if (missingScopes.Count > 0)
            {
                return (TokenProblem.MissingScopes, null, null, $"Missing required scopes: {string.Join(", ", missingScopes)}");
            }

            return (TokenProblem.None, validation.UserId, validation.Login, null);
        }
        catch (Exception ex)
        {
            return (TokenProblem.Transient, null, null, $"Token validation failed: {ex.Message}");
        }
    }

    // A null token uses the login stored by TwitchAuthService, which is refreshed as it nears expiry.
    public async Task<bool> ConnectAsync(string? oauthToken = null)
    {
        if (_isConnecting || IsConnected)
        {
            return false;
        }

        _isConnecting = true;
        _usesManagedToken = oauthToken == null;
        _oauthToken = oauthToken;
        _authRejected = false;
        _useLocalCli = _authService.UseLocalCli;

        try
        {
            if (_useLocalCli)
            {
                // The Twitch CLI mock server doesn't check tokens and sends events for any broadcaster.
                _userId = LocalCliUserId;
                CurrentUserName = LocalCliUserName;
            }
            else
            {
                var token = await GetTokenForRequestAsync();
                if (string.IsNullOrEmpty(token))
                {
                    _authRejected = true;
                    ReportError(_authService.LoginExpired ? "Twitch login expired, log in again" : "Log in to Twitch before connecting");
                    return false;
                }

                var validation = await CheckTokenAsync(token);
                if (validation.Problem == TokenProblem.Rejected && _usesManagedToken && _authService.CanRefresh &&
                    await _authService.RefreshAccessTokenAsync())
                {
                    token = await GetTokenForRequestAsync() ?? token;
                    validation = await CheckTokenAsync(token);
                }

                if (validation.Problem != TokenProblem.None)
                {
                    var error = validation.Error ?? "Token validation failed";
                    ReportError(error);

                    // A managed login that still has a refresh token is retried; anything else needs a new login.
                    var needsLogin = validation.Problem == TokenProblem.MissingScopes ||
                        (validation.Problem == TokenProblem.Rejected && !(_usesManagedToken && _authService.CanRefresh));
                    if (needsLogin)
                    {
                        _authRejected = true;
                        if (_usesManagedToken) _authService.MarkLoginExpired(error);
                    }
                    return false;
                }

                _userId = validation.UserId;
                CurrentUserName = validation.UserName;
                ReportStatus($"Authenticated as {validation.UserName}");
            }

            _connectionCts = new CancellationTokenSource();
            _webSocket = new ClientWebSocket();

            var websocketUrl = _authService.EventSubWebSocketUrl;
            var connectionType = _useLocalCli ? $"local CLI ({websocketUrl})" : "production";
            ReportStatus($"Connecting to {connectionType} WebSocket...");

            await _webSocket.ConnectAsync(new Uri(websocketUrl), _connectionCts.Token);
            ReportStatus("WebSocket connected, waiting for welcome message...");

            // Started before the welcome so a connection that never receives one is also retried.
            _isSessionReconnect = false;
            _keepaliveTimeoutSeconds = 10;
            _lastMessageTime = DateTime.UtcNow;
            _keepaliveTimer = new Timer(CheckKeepalive, null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(10));

            // A connection initiated from Blazor must not run event handlers on its UI context.
            var cancellationToken = _connectionCts.Token;
            _receiveTask = Task.Run(() => ReceiveMessagesAsync(cancellationToken));

            return true;
        }
        catch (Exception ex)
        {
            ReportError($"Connection failed: {ex.Message}");
            await CloseConnectionAsync();
            return false;
        }
        finally
        {
            _isConnecting = false;
        }
    }

    public async Task DisconnectAsync()
    {
        _reconnectCts?.Cancel();
        await CloseConnectionAsync();
    }

    private async Task CloseConnectionAsync()
    {
        _isConnected = false;
        _keepaliveTimer?.Dispose();
        _keepaliveTimer = null;
        var receiveTask = _receiveTask;
        _connectionCts?.Cancel();

        if (_webSocket != null)
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", CancellationToken.None);
                }
            }
            catch { }
            finally
            {
                _webSocket.Dispose();
                _webSocket = null;
            }
        }

        if (receiveTask != null)
            await receiveTask.ConfigureAwait(false);
        _receiveTask = null;
        _connectionCts?.Dispose();
        _connectionCts = null;

        _sessionId = null;
        _userId = null;
        CurrentUserName = null;
        ConnectionStateChanged?.Invoke(false);
        ReportStatus("Disconnected from Twitch");
    }

    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var messageBuffer = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var webSocket = _webSocket;
                if (webSocket?.State != WebSocketState.Open) break;
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    ReportStatus("WebSocket closed by server");
                    break;
                }

                messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (result.EndOfMessage)
                {
                    var message = messageBuffer.ToString();
                    messageBuffer.Clear();
                    _lastMessageTime = DateTime.UtcNow;

                    await ProcessMessageAsync(message).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            ReportError($"WebSocket error: {ex.Message}");
        }
        finally
        {
            _isConnected = false;
            ConnectionStateChanged?.Invoke(false);

            // Teardown cancels the token first, so an uncancelled exit is a dropped connection.
            if (!cancellationToken.IsCancellationRequested)
            {
                ScheduleReconnect("Connection lost");
            }
        }
    }

    private async Task ProcessMessageAsync(string message)
    {
        try
        {
            TwitchWebSocketMessage? wsMessage;
            using (_profiler.Measure("EventSub JSON parsing"))
                wsMessage = JsonSerializer.Deserialize<TwitchWebSocketMessage>(message);
            if (wsMessage == null) return;

            using var timing = _profiler.IsCapturing ? _profiler.Measure($"EventSub: {wsMessage.Metadata.MessageType}") : default;
            switch (wsMessage.Metadata.MessageType)
            {
                case "session_welcome":
                    await HandleWelcomeAsync(wsMessage.Payload);
                    break;

                case "session_keepalive":
                    break;

                case "session_reconnect":
                    await HandleReconnectAsync(wsMessage.Payload);
                    break;

                case "notification":
                    HandleNotification(wsMessage);
                    break;

                case "revocation":
                    var revoked = JsonSerializer.Deserialize<NotificationPayload>(wsMessage.Payload);
                    ReportError($"Subscription revoked: {revoked?.Subscription.Type} ({revoked?.Subscription.Status})");
                    if (revoked?.Subscription.Status == "authorization_revoked" && _usesManagedToken)
                    {
                        _authService.MarkLoginExpired("Twitch revoked MultiShock's authorization");
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            ReportError($"Error processing message: {ex.Message}");
        }
    }

    private async Task HandleWelcomeAsync(JsonElement payload)
    {
        var welcome = JsonSerializer.Deserialize<WelcomePayload>(payload);
        if (welcome == null) return;

        _sessionId = welcome.Session.Id;
        _keepaliveTimeoutSeconds = welcome.Session.KeepaliveTimeoutSeconds;

        ReportStatus($"Session established: {_sessionId}");

        // Twitch moves existing subscriptions to the new session on a session_reconnect.
        if (_isSessionReconnect)
        {
            _isSessionReconnect = false;
        }
        else
        {
            await SubscribeToEventsAsync();
        }

        _isConnected = true;
        ConnectionStateChanged?.Invoke(true);
    }

    private async Task HandleReconnectAsync(JsonElement payload)
    {
        var reconnect = JsonSerializer.Deserialize<ReconnectPayload>(payload);
        if (reconnect?.Session.ReconnectUrl == null) return;

        ReportStatus("Reconnecting to new URL...");

        var newWebSocket = new ClientWebSocket();
        await newWebSocket.ConnectAsync(new Uri(reconnect.Session.ReconnectUrl), _connectionCts?.Token ?? CancellationToken.None);

        var oldSocket = _webSocket;
        _isSessionReconnect = true;
        _webSocket = newWebSocket;
        oldSocket?.Dispose();

        ReportStatus("Reconnected successfully");
    }

    private void HandleNotification(TwitchWebSocketMessage wsMessage)
    {
        var notification = JsonSerializer.Deserialize<NotificationPayload>(wsMessage.Payload);
        if (notification == null) return;

        var eventType = notification.Subscription.Type;
        var eventJson = notification.Event;

        try
        {
            switch (eventType)
            {
                case TwitchConstants.EventTypes.Cheer:
                    var cheer = JsonSerializer.Deserialize<CheerEvent>(eventJson);
                    if (cheer != null)
                    {
                        ReportStatus($"Cheer: {cheer.UserName} cheered {cheer.Bits} bits");
                        Dispatch(OnCheer, cheer);
                    }
                    break;

                case TwitchConstants.EventTypes.Subscribe:
                    var subscribe = JsonSerializer.Deserialize<SubscribeEvent>(eventJson);
                    if (subscribe != null)
                    {
                        ReportStatus($"Subscribe: {subscribe.UserName} subscribed (Tier {subscribe.TierNumber})");
                        Dispatch(OnSubscribe, subscribe);
                    }
                    break;

                case TwitchConstants.EventTypes.SubscriptionGift:
                    var gift = JsonSerializer.Deserialize<SubscriptionGiftEvent>(eventJson);
                    if (gift != null)
                    {
                        ReportStatus($"Gift Sub: {gift.UserName} gifted {gift.Total} subs");
                        Dispatch(OnSubscriptionGift, gift);
                    }
                    break;

                case TwitchConstants.EventTypes.SubscriptionMessage:
                    var subMessage = JsonSerializer.Deserialize<SubscriptionMessageEvent>(eventJson);
                    if (subMessage != null)
                    {
                        ReportStatus($"Resub: {subMessage.UserName} resubscribed for {subMessage.CumulativeMonths} months");
                        Dispatch(OnSubscriptionMessage, subMessage);
                    }
                    break;

                case TwitchConstants.EventTypes.Follow:
                    var follow = JsonSerializer.Deserialize<FollowEvent>(eventJson);
                    if (follow != null)
                    {
                        ReportStatus($"Follow: {follow.UserName} followed");
                        Dispatch(OnFollow, follow);
                    }
                    break;

                case TwitchConstants.EventTypes.HypeTrainBegin:
                    var hypeBegin = JsonSerializer.Deserialize<HypeTrainBeginEvent>(eventJson);
                    if (hypeBegin != null)
                    {
                        ReportStatus($"Hype Train started at level {hypeBegin.Level}!");
                        Dispatch(OnHypeTrainBegin, hypeBegin);
                    }
                    break;

                case TwitchConstants.EventTypes.HypeTrainProgress:
                    var hypeProgress = JsonSerializer.Deserialize<HypeTrainProgressEvent>(eventJson);
                    if (hypeProgress != null)
                    {
                        ReportStatus($"Hype Train level {hypeProgress.Level}: {hypeProgress.Progress}/{hypeProgress.Goal}");
                        Dispatch(OnHypeTrainProgress, hypeProgress);
                    }
                    break;

                case TwitchConstants.EventTypes.HypeTrainEnd:
                    var hypeEnd = JsonSerializer.Deserialize<HypeTrainEndEvent>(eventJson);
                    if (hypeEnd != null)
                    {
                        ReportStatus($"Hype Train ended at level {hypeEnd.Level}!");
                        Dispatch(OnHypeTrainEnd, hypeEnd);
                    }
                    break;

                case TwitchConstants.EventTypes.Raid:
                    var raid = JsonSerializer.Deserialize<RaidEvent>(eventJson);
                    if (raid != null)
                    {
                        ReportStatus($"Raid: {raid.FromBroadcasterUserName} raided with {raid.Viewers} viewers");
                        Dispatch(OnRaid, raid);
                    }
                    break;

                case TwitchConstants.EventTypes.ChannelPointRedemption:
                    var redemption = JsonSerializer.Deserialize<ChannelPointRedemptionEvent>(eventJson);
                    if (redemption != null)
                    {
                        ReportStatus($"Redemption: {redemption.UserName} redeemed '{redemption.Reward.Title}'");
                        Dispatch(OnChannelPointRedemption, redemption);
                    }
                    break;

                case TwitchConstants.EventTypes.ChatMessage:
                    var chatMessage = JsonSerializer.Deserialize<ChatMessageEvent>(eventJson);
                    if (chatMessage != null)
                    {
                        Dispatch(OnChatMessage, chatMessage);
                    }
                    break;

                default:
                    ReportStatus($"Unknown event type: {eventType}");
                    break;
            }
        }
        catch (Exception ex)
        {
            ReportError($"Error handling {eventType}: {ex.Message}");
        }
    }

    private void Dispatch<T>(Action<T>? handlers, T value)
    {
        if (handlers == null) return;
        if (!_profiler.IsCapturing)
        {
            handlers(value);
            return;
        }

        foreach (Action<T> handler in handlers.GetInvocationList())
        {
            using var timing = _profiler.Measure($"Handler: {handler.Method.DeclaringType?.Name}.{handler.Method.Name}");
            handler(value);
        }
    }

    private async Task<string?> GetTokenForRequestAsync()
    {
        if (_usesManagedToken)
        {
            _oauthToken = await _authService.GetAccessTokenAsync();
        }

        return _oauthToken;
    }

    private async Task SubscribeToEventsAsync()
    {
        await GetTokenForRequestAsync();
        if (string.IsNullOrEmpty(_sessionId) || string.IsNullOrEmpty(_userId) || (!_useLocalCli && string.IsNullOrEmpty(_oauthToken)))
        {
            return;
        }

        var eventTypes = new[]
        {
            TwitchConstants.EventTypes.Cheer,
            TwitchConstants.EventTypes.Subscribe,
            TwitchConstants.EventTypes.SubscriptionGift,
            TwitchConstants.EventTypes.SubscriptionMessage,
            TwitchConstants.EventTypes.Follow,
            TwitchConstants.EventTypes.HypeTrainBegin,
            TwitchConstants.EventTypes.HypeTrainProgress,
            TwitchConstants.EventTypes.HypeTrainEnd,
            TwitchConstants.EventTypes.Raid,
            TwitchConstants.EventTypes.ChannelPointRedemption,
            TwitchConstants.EventTypes.ChatMessage,
        };

        foreach (var eventType in eventTypes)
        {
            await SubscribeToEventAsync(eventType);
        }
    }

    private async Task SubscribeToEventAsync(string eventType)
    {
        if (!TwitchConstants.EventVersions.TryGetValue(eventType, out var version))
        {
            version = "1";
        }

        var condition = GetConditionForEvent(eventType);
        if (condition == null)
        {
            return;
        }

        var request = new CreateSubscriptionRequest
        {
            Type = eventType,
            Version = version,
            Condition = condition,
            Transport = new CreateSubscriptionTransport
            {
                Method = "websocket",
                SessionId = _sessionId!
            }
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_authService.HelixApiBaseUrl}/eventsub/subscriptions")
        {
            Content = content
        };
        httpRequest.Headers.Add("Client-Id", TwitchConstants.ClientId);
        if (!string.IsNullOrEmpty(_oauthToken))
        {
            httpRequest.Headers.Add("Authorization", $"Bearer {_oauthToken}");
        }

        try
        {
            var response = await _httpClient.SendAsync(httpRequest);
            if (response.IsSuccessStatusCode)
            {
                ReportStatus($"Subscribed to {eventType}");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                ReportStatus($"Failed to subscribe to {eventType}: {response.StatusCode} - {errorContent}");
            }
        }
        catch (Exception ex)
        {
            ReportError($"Error subscribing to {eventType}: {ex.Message}");
        }
    }

    private Dictionary<string, string>? GetConditionForEvent(string eventType)
    {
        var condition = new Dictionary<string, string>
        {
            ["broadcaster_user_id"] = _userId!
        };

        if (eventType == TwitchConstants.EventTypes.Follow)
        {
            condition["moderator_user_id"] = _userId!;
        }

        if (eventType == TwitchConstants.EventTypes.ChatMessage)
        {
            condition["user_id"] = _userId!;
            return condition;
        }

        if (eventType == TwitchConstants.EventTypes.Raid)
        {
            return new Dictionary<string, string>
            {
                ["to_broadcaster_user_id"] = _userId!
            };
        }

        return condition;
    }

    public async Task<bool> SendChatMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_userId) || string.IsNullOrEmpty(await GetTokenForRequestAsync()))
        {
            return false;
        }

        try
        {
            var requestBody = new
            {
                broadcaster_id = _userId,
                sender_id = _userId,
                message = message
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_authService.HelixApiBaseUrl}/chat/messages")
            {
                Content = content
            };
            httpRequest.Headers.Add("Client-Id", TwitchConstants.ClientId);
            httpRequest.Headers.Add("Authorization", $"Bearer {_oauthToken}");

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            ReportError($"Error sending chat message: {ex.Message}");
            return false;
        }
    }

    private void CheckKeepalive(object? state)
    {
        var timeSinceLastMessage = DateTime.UtcNow - _lastMessageTime;
        if (timeSinceLastMessage.TotalSeconds > _keepaliveTimeoutSeconds + 10)
        {
            ScheduleReconnect("Keepalive timeout");
        }
    }

    private void ScheduleReconnect(string reason)
    {
        if (Interlocked.Exchange(ref _reconnectLoopActive, 1) == 1) return;

        ReportError($"{reason}, reconnecting...");
        var cts = new CancellationTokenSource();
        _reconnectCts = cts;
        _ = Task.Run(() => ReconnectLoopAsync(cts.Token));
    }

    private async Task ReconnectLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                await Task.Delay(ReconnectDelays[Math.Min(attempt - 1, ReconnectDelays.Length - 1)], cancellationToken);
                if (IsConnected || _isConnecting) return;

                await CloseConnectionAsync();
                cancellationToken.ThrowIfCancellationRequested();

                ReportStatus($"Reconnect attempt {attempt}");
                if (await ConnectAsync(_usesManagedToken ? null : _oauthToken)) return;
                if (_authRejected)
                {
                    ReportError("Stopped reconnecting: Twitch rejected the login. Log in again to reconnect.");
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled by DisconnectAsync
        }
        catch (Exception ex)
        {
            ReportError($"Reconnect stopped: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _reconnectLoopActive, 0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _httpClient.Dispose();
    }
}
