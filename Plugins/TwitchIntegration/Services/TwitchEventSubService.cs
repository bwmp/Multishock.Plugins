using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using TwitchIntegrationPlugin.Models;

namespace TwitchIntegrationPlugin.Services;

public class TwitchEventSubService : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly TwitchAuthService _authService;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _connectionCts;
    private Task? _receiveTask;
    private string? _sessionId;
    private string? _userId;
    private string? _oauthToken;
    private int _keepaliveTimeoutSeconds = 10;
    private DateTime _lastMessageTime = DateTime.UtcNow;
    private Timer? _keepaliveTimer;
    private bool _isConnected;
    private bool _isConnecting;

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

    public TwitchEventSubService(TwitchAuthService authService)
    {
        _httpClient = new HttpClient();
        _authService = authService;
    }

    public async Task<(bool IsValid, string? UserId, string? UserName, string? Error)> ValidateTokenAsync(string oauthToken)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
            request.Headers.Add("Authorization", $"OAuth {oauthToken}");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null, null, "Invalid or expired OAuth token");
            }

            var content = await response.Content.ReadAsStringAsync();
            var validation = JsonSerializer.Deserialize<TokenValidationResponse>(content);

            if (validation == null)
            {
                return (false, null, null, "Failed to parse token validation response");
            }

            // Check for required scopes
            var missingScopes = TwitchConstants.RequiredScopes
                .Where(s => !validation.Scopes.Contains(s))
                .ToList();

            if (missingScopes.Count > 0)
            {
                return (false, null, null, $"Missing required scopes: {string.Join(", ", missingScopes)}");
            }

            return (true, validation.UserId, validation.Login, null);
        }
        catch (Exception ex)
        {
            return (false, null, null, $"Token validation failed: {ex.Message}");
        }
    }

    public async Task<bool> ConnectAsync(string oauthToken)
    {
        if (_isConnecting || IsConnected)
        {
            return false;
        }

        _isConnecting = true;
        _oauthToken = oauthToken;

        try
        {
            // Validate token first
            var (isValid, userId, userName, error) = await ValidateTokenAsync(oauthToken);
            if (!isValid)
            {
                ErrorOccurred?.Invoke(error ?? "Token validation failed");
                return false;
            }

            _userId = userId;
            CurrentUserName = userName;
            OnStatusMessage?.Invoke($"Authenticated as {userName}");

            _connectionCts = new CancellationTokenSource();
            _webSocket = new ClientWebSocket();

            var websocketUrl = _authService.UseLocalCli 
                ? TwitchConstants.EventSubWebSocketUrlLocal 
                : TwitchConstants.EventSubWebSocketUrl;
            
            var connectionType = _authService.UseLocalCli ? "local CLI" : "production";
            OnStatusMessage?.Invoke($"Connecting to {connectionType} WebSocket...");

            await _webSocket.ConnectAsync(new Uri(websocketUrl), _connectionCts.Token);
            OnStatusMessage?.Invoke("WebSocket connected, waiting for welcome message...");

            _receiveTask = ReceiveMessagesAsync(_connectionCts.Token);

            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Connection failed: {ex.Message}");
            await DisconnectAsync();
            return false;
        }
        finally
        {
            _isConnecting = false;
        }
    }

    public async Task DisconnectAsync()
    {
        _isConnected = false;
        _keepaliveTimer?.Dispose();
        _keepaliveTimer = null;
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

        _sessionId = null;
        _userId = null;
        CurrentUserName = null;
        ConnectionStateChanged?.Invoke(false);
        OnStatusMessage?.Invoke("Disconnected from Twitch");
    }

    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var messageBuffer = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    OnStatusMessage?.Invoke("WebSocket closed by server");
                    break;
                }

                messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (result.EndOfMessage)
                {
                    var message = messageBuffer.ToString();
                    messageBuffer.Clear();
                    _lastMessageTime = DateTime.UtcNow;

                    await ProcessMessageAsync(message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"WebSocket error: {ex.Message}");
        }
        finally
        {
            _isConnected = false;
            ConnectionStateChanged?.Invoke(false);
        }
    }

    private async Task ProcessMessageAsync(string message)
    {
        try
        {
            var wsMessage = JsonSerializer.Deserialize<TwitchWebSocketMessage>(message);
            if (wsMessage == null) return;

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
                    OnStatusMessage?.Invoke("Subscription revoked");
                    break;
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Error processing message: {ex.Message}");
        }
    }

    private async Task HandleWelcomeAsync(JsonElement payload)
    {
        var welcome = JsonSerializer.Deserialize<WelcomePayload>(payload.GetRawText());
        if (welcome == null) return;

        _sessionId = welcome.Session.Id;
        _keepaliveTimeoutSeconds = welcome.Session.KeepaliveTimeoutSeconds;

        OnStatusMessage?.Invoke($"Session established: {_sessionId}");

        _keepaliveTimer = new Timer(CheckKeepalive, null, TimeSpan.FromSeconds(_keepaliveTimeoutSeconds + 5), TimeSpan.FromSeconds(10));

        await SubscribeToEventsAsync();

        _isConnected = true;
        ConnectionStateChanged?.Invoke(true);
    }

    private async Task HandleReconnectAsync(JsonElement payload)
    {
        var reconnect = JsonSerializer.Deserialize<ReconnectPayload>(payload.GetRawText());
        if (reconnect?.Session.ReconnectUrl == null) return;

        OnStatusMessage?.Invoke("Reconnecting to new URL...");

        var newWebSocket = new ClientWebSocket();
        await newWebSocket.ConnectAsync(new Uri(reconnect.Session.ReconnectUrl), _connectionCts?.Token ?? CancellationToken.None);

        var oldSocket = _webSocket;
        _webSocket = newWebSocket;
        oldSocket?.Dispose();

        OnStatusMessage?.Invoke("Reconnected successfully");
    }

    private void HandleNotification(TwitchWebSocketMessage wsMessage)
    {
        var notification = JsonSerializer.Deserialize<NotificationPayload>(wsMessage.Payload.GetRawText());
        if (notification == null) return;

        var eventType = notification.Subscription.Type;
        var eventJson = notification.Event.GetRawText();

        try
        {
            switch (eventType)
            {
                case TwitchConstants.EventTypes.Cheer:
                    var cheer = JsonSerializer.Deserialize<CheerEvent>(eventJson);
                    if (cheer != null)
                    {
                        OnStatusMessage?.Invoke($"Cheer: {cheer.UserName} cheered {cheer.Bits} bits");
                        OnCheer?.Invoke(cheer);
                    }
                    break;

                case TwitchConstants.EventTypes.Subscribe:
                    var subscribe = JsonSerializer.Deserialize<SubscribeEvent>(eventJson);
                    if (subscribe != null)
                    {
                        OnStatusMessage?.Invoke($"Subscribe: {subscribe.UserName} subscribed (Tier {subscribe.TierNumber})");
                        OnSubscribe?.Invoke(subscribe);
                    }
                    break;

                case TwitchConstants.EventTypes.SubscriptionGift:
                    var gift = JsonSerializer.Deserialize<SubscriptionGiftEvent>(eventJson);
                    if (gift != null)
                    {
                        OnStatusMessage?.Invoke($"Gift Sub: {gift.UserName} gifted {gift.Total} subs");
                        OnSubscriptionGift?.Invoke(gift);
                    }
                    break;

                case TwitchConstants.EventTypes.SubscriptionMessage:
                    var subMessage = JsonSerializer.Deserialize<SubscriptionMessageEvent>(eventJson);
                    if (subMessage != null)
                    {
                        OnStatusMessage?.Invoke($"Resub: {subMessage.UserName} resubscribed for {subMessage.CumulativeMonths} months");
                        OnSubscriptionMessage?.Invoke(subMessage);
                    }
                    break;

                case TwitchConstants.EventTypes.Follow:
                    var follow = JsonSerializer.Deserialize<FollowEvent>(eventJson);
                    if (follow != null)
                    {
                        OnStatusMessage?.Invoke($"Follow: {follow.UserName} followed");
                        OnFollow?.Invoke(follow);
                    }
                    break;

                case TwitchConstants.EventTypes.HypeTrainBegin:
                    var hypeBegin = JsonSerializer.Deserialize<HypeTrainBeginEvent>(eventJson);
                    if (hypeBegin != null)
                    {
                        OnStatusMessage?.Invoke($"Hype Train started at level {hypeBegin.Level}!");
                        OnHypeTrainBegin?.Invoke(hypeBegin);
                    }
                    break;

                case TwitchConstants.EventTypes.HypeTrainProgress:
                    var hypeProgress = JsonSerializer.Deserialize<HypeTrainProgressEvent>(eventJson);
                    if (hypeProgress != null)
                    {
                        OnStatusMessage?.Invoke($"Hype Train level {hypeProgress.Level}: {hypeProgress.Progress}/{hypeProgress.Goal}");
                        OnHypeTrainProgress?.Invoke(hypeProgress);
                    }
                    break;

                case TwitchConstants.EventTypes.HypeTrainEnd:
                    var hypeEnd = JsonSerializer.Deserialize<HypeTrainEndEvent>(eventJson);
                    if (hypeEnd != null)
                    {
                        OnStatusMessage?.Invoke($"Hype Train ended at level {hypeEnd.Level}!");
                        OnHypeTrainEnd?.Invoke(hypeEnd);
                    }
                    break;

                case TwitchConstants.EventTypes.Raid:
                    var raid = JsonSerializer.Deserialize<RaidEvent>(eventJson);
                    if (raid != null)
                    {
                        OnStatusMessage?.Invoke($"Raid: {raid.FromBroadcasterUserName} raided with {raid.Viewers} viewers");
                        OnRaid?.Invoke(raid);
                    }
                    break;

                case TwitchConstants.EventTypes.ChannelPointRedemption:
                    var redemption = JsonSerializer.Deserialize<ChannelPointRedemptionEvent>(eventJson);
                    if (redemption != null)
                    {
                        OnStatusMessage?.Invoke($"Redemption: {redemption.UserName} redeemed '{redemption.Reward.Title}'");
                        OnChannelPointRedemption?.Invoke(redemption);
                    }
                    break;

                case TwitchConstants.EventTypes.ChatMessage:
                    var chatMessage = JsonSerializer.Deserialize<ChatMessageEvent>(eventJson);
                    if (chatMessage != null)
                    {
                        OnChatMessage?.Invoke(chatMessage);
                    }
                    break;

                default:
                    OnStatusMessage?.Invoke($"Unknown event type: {eventType}");
                    break;
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Error handling {eventType}: {ex.Message}");
        }
    }

    private async Task SubscribeToEventsAsync()
    {
        if (string.IsNullOrEmpty(_sessionId) || string.IsNullOrEmpty(_userId) || string.IsNullOrEmpty(_oauthToken))
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

        var helixApiUrl = _authService.UseLocalCli 
            ? TwitchConstants.HelixApiBaseUrlLocal 
            : TwitchConstants.HelixApiBaseUrl;

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{helixApiUrl}/eventsub/subscriptions")
        {
            Content = content
        };
        httpRequest.Headers.Add("Client-Id", TwitchConstants.ClientId);
        httpRequest.Headers.Add("Authorization", $"Bearer {_oauthToken}");

        try
        {
            var response = await _httpClient.SendAsync(httpRequest);
            if (response.IsSuccessStatusCode)
            {
                OnStatusMessage?.Invoke($"Subscribed to {eventType}");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                OnStatusMessage?.Invoke($"Failed to subscribe to {eventType}: {response.StatusCode} - {errorContent}");
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Error subscribing to {eventType}: {ex.Message}");
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
        if (string.IsNullOrEmpty(_userId) || string.IsNullOrEmpty(_oauthToken))
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

            var helixApiUrl = _authService.UseLocalCli 
                ? TwitchConstants.HelixApiBaseUrlLocal 
                : TwitchConstants.HelixApiBaseUrl;

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{helixApiUrl}/chat/messages")
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
            ErrorOccurred?.Invoke($"Error sending chat message: {ex.Message}");
            return false;
        }
    }

    private void CheckKeepalive(object? state)
    {
        var timeSinceLastMessage = DateTime.UtcNow - _lastMessageTime;
        if (timeSinceLastMessage.TotalSeconds > _keepaliveTimeoutSeconds + 10)
        {
            OnStatusMessage?.Invoke("Keepalive timeout, reconnecting...");
            _ = Task.Run(async () =>
            {
                await DisconnectAsync();
                if (!string.IsNullOrEmpty(_oauthToken))
                {
                    await ConnectAsync(_oauthToken);
                }
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _httpClient.Dispose();
    }
}
