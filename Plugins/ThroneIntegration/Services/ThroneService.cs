using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using ThroneAPI.Client;
using ThroneIntegration.Models;

namespace ThroneIntegration.Services;

/// <summary>
/// Service for listening to Throne WebSocket for gift events using real-time streaming
/// </summary>
public class ThroneService : IDisposable
{
    private const string WEBSOCKET_URL = "wss://throne.bwmp.dev/api/ws/subscribe";
    private const int RECONNECT_DELAY_MS = 5000;
    private const int MAX_RECONNECT_ATTEMPTS = 10;
    private const int RECEIVE_BUFFER_SIZE = 8192;

    private readonly ILogger<ThroneService>? _logger;
    private readonly ThroneApiClient _throneClient;
    private readonly Dictionary<string, string> _creatorIdCache = new(StringComparer.OrdinalIgnoreCase);
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _listenerCts;
    private string? _username;
    private string? _creatorId;
    private long _lastSeenTime = 0;
    private bool _isRunning;
    private int _reconnectAttempts;

    /// <summary>
    /// Whether the service is currently connected and listening for events
    /// </summary>
    public bool IsConnected => _isRunning && _webSocket?.State == WebSocketState.Open;

    /// <summary>
    /// The current username being monitored
    /// </summary>
    public string? CurrentUsername => _username;

    /// <summary>
    /// The current creator ID being monitored
    /// </summary>
    public string? CurrentCreatorId => _creatorId;

    /// <summary>
    /// Event fired when the connection state changes
    /// </summary>
    public event Action<bool>? ConnectionStateChanged;

    /// <summary>
    /// Event fired when a status message is available
    /// </summary>
    public event Action<string>? StatusMessage;

    /// <summary>
    /// Event fired when an error occurs
    /// </summary>
    public event Action<string>? ErrorOccurred;

    public event Action<ThroneEvent>? OnGiftReceived;

    public ThroneService(ILogger<ThroneService>? logger = null)
    {
        _logger = logger;

        var authProvider = new AnonymousAuthenticationProvider();
        var adapter = new HttpClientRequestAdapter(authProvider)
        {
            BaseUrl = "https://throne.bwmp.dev/api"
        };
        _throneClient = new ThroneApiClient(adapter);
    }

    /// <summary>
    /// Starts listening for throne gifts for the specified creator using WebSocket
    /// </summary>
    public async Task StartAsync(string username)
    {
        if (_isRunning)
        {
            _logger?.LogWarning("ThroneService is already running");
            return;
        }

        _username = username;
        _isRunning = true;
        _reconnectAttempts = 0;
        _listenerCts = new CancellationTokenSource();

        _logger?.LogInformation("Starting Throne WebSocket listener for user: {Username}", username);
        StatusMessage?.Invoke("Fetching creator ID...");

        try
        {
            if (_creatorIdCache.TryGetValue(username, out var cachedId))
            {
                _creatorId = cachedId;
                _logger?.LogInformation("Using cached creator ID: {CreatorId} for username: {Username}", _creatorId, username);
            }
            else
            {
                _creatorId = await FetchCreatorIdAsync(username, _listenerCts.Token);

                if (string.IsNullOrEmpty(_creatorId))
                {
                    _isRunning = false;
                    ErrorOccurred?.Invoke($"Could not find creator ID for username: {username}");
                    ConnectionStateChanged?.Invoke(false);
                    return;
                }

                _creatorIdCache[username] = _creatorId;
                _logger?.LogInformation("Found and cached creator ID: {CreatorId} for username: {Username}", _creatorId, username);
            }

            StatusMessage?.Invoke("Connecting to Throne WebSocket...");

            _ = StartWebSocketListenerWithReconnect(_listenerCts.Token);
        }
        catch (Exception ex)
        {
            _isRunning = false;
            _logger?.LogError(ex, "Failed to start ThroneService");
            ErrorOccurred?.Invoke($"Failed to start: {ex.Message}");
            ConnectionStateChanged?.Invoke(false);
        }
    }

    /// <summary>
    /// Fetches the creator ID from the Throne API using the username
    /// </summary>
    private async Task<string?> FetchCreatorIdAsync(string username, CancellationToken cancellationToken)
    {
        try
        {
            var creator = await _throneClient.Creators[username].GetAsWithUsernameGetResponseAsync(cancellationToken: cancellationToken);

            if (creator == null)
            {
                _logger?.LogWarning("Creator not found for username: {Username}", username);
                return null;
            }

            if (creator?.AdditionalData.TryGetValue("_id", out var idValue) == true)
            {
                var id = idValue?.ToString();
                _logger?.LogDebug("Creator API response for {Username}: Id={Id}", username, id);
                return id;
            }

            _logger?.LogWarning("Could not extract ID from creator response for {Username}", username);
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error fetching creator ID for username: {Username}", username);
            throw;
        }
    }

    /// <summary>
    /// Stops the streaming service
    /// </summary>
    public void Stop()
    {
        _isRunning = false;
        _listenerCts?.Cancel();

        if (_webSocket != null)
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Stopped by user", CancellationToken.None)
                        .GetAwaiter().GetResult();
                }
            }
            catch
            {
                // Ignore errors during close
            }
            finally
            {
                _webSocket.Dispose();
                _webSocket = null;
            }
        }

        _listenerCts?.Dispose();
        _listenerCts = null;
        _username = null;
        _creatorId = null;

        _logger?.LogInformation("Stopped Throne listener");
        ConnectionStateChanged?.Invoke(false);
        StatusMessage?.Invoke("Disconnected from Throne");
    }

    private async Task StartWebSocketListenerWithReconnect(CancellationToken cancellationToken)
    {
        while (_isRunning && !cancellationToken.IsCancellationRequested && _reconnectAttempts < MAX_RECONNECT_ATTEMPTS)
        {
            try
            {
                await StartWebSocketListener(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger?.LogInformation("Listener cancelled");
                break;
            }
            catch (Exception ex)
            {
                _reconnectAttempts++;
                _logger?.LogError(ex, "WebSocket connection error. Attempt {Attempt}/{Max}",
                    _reconnectAttempts, MAX_RECONNECT_ATTEMPTS);

                if (_reconnectAttempts >= MAX_RECONNECT_ATTEMPTS)
                {
                    ErrorOccurred?.Invoke($"Failed to connect after {MAX_RECONNECT_ATTEMPTS} attempts");
                    ConnectionStateChanged?.Invoke(false);
                    break;
                }

                StatusMessage?.Invoke($"Connection lost. Reconnecting in {RECONNECT_DELAY_MS / 1000}s...");
                ConnectionStateChanged?.Invoke(false);

                try
                {
                    await Task.Delay(RECONNECT_DELAY_MS, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task StartWebSocketListener(CancellationToken cancellationToken)
    {
        _webSocket?.Dispose();
        _webSocket = new ClientWebSocket();

        await _webSocket.ConnectAsync(new Uri(WEBSOCKET_URL), cancellationToken);

        _logger?.LogInformation("Connected to Throne WebSocket");

        var subscribeMessage = new WebSocketSubscribeMessage
        {
            Action = "subscribe",
            Collection = "overlays",
            CreatorId = _creatorId!
        };

        var messageJson = JsonSerializer.Serialize(subscribeMessage);
        _logger?.LogInformation("Sending subscription message: {Message}", messageJson);
        var messageBytes = Encoding.UTF8.GetBytes(messageJson);

        await _webSocket.SendAsync(
            new ArraySegment<byte>(messageBytes),
            WebSocketMessageType.Text,
            true,
            cancellationToken);

        _logger?.LogInformation("Sent subscription message for creatorId: {CreatorId}", _creatorId);
        _reconnectAttempts = 0; // Reset on successful connection
        ConnectionStateChanged?.Invoke(true);
        StatusMessage?.Invoke("Connected! Listening for gifts...");

        var buffer = new byte[RECEIVE_BUFFER_SIZE];
        var messageBuffer = new StringBuilder();

        while (_isRunning && !cancellationToken.IsCancellationRequested &&
               _webSocket.State == WebSocketState.Open)
        {
            try
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger?.LogInformation("WebSocket closed by server: {Status} - {Description}",
                        result.CloseStatus, result.CloseStatusDescription);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        var message = messageBuffer.ToString();
                        messageBuffer.Clear();
                        ProcessWebSocketMessage(message);
                    }
                }
            }
            catch (WebSocketException ex)
            {
                _logger?.LogError(ex, "WebSocket receive error");
                throw;
            }
        }
    }

    private void ProcessWebSocketMessage(string json)
    {
        try
        {
            _logger?.LogInformation("Received WebSocket message: {Message}", json);
            Console.WriteLine($"[ThroneService] Received WebSocket message: {json.Substring(0, Math.Min(200, json.Length))}");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var throneEvent = ParseWebSocketMessage(root);

            if (throneEvent == null)
            {
                Console.WriteLine($"[ThroneService] ParseWebSocketMessage returned null");
            }
            else
            {
                Console.WriteLine($"[ThroneService] Parsed event - CreatedAt: {throneEvent.CreatedAt}, LastSeenTime: {_lastSeenTime}");
            }

            if (throneEvent != null && throneEvent.CreatedAt > _lastSeenTime)
            {
                _lastSeenTime = throneEvent.CreatedAt;
                _logger?.LogInformation("New Throne event: {Type} from {User}",
                    throneEvent.OverlayInformation?.Type ?? "Unknown",
                    throneEvent.OverlayInformation?.GifterUsername ?? "Anonymous");

                Console.WriteLine($"[ThroneService] Invoking OnGiftReceived event. Subscriber count: {OnGiftReceived?.GetInvocationList()?.Length ?? 0}");
                OnGiftReceived?.Invoke(throneEvent);
                Console.WriteLine($"[ThroneService] OnGiftReceived event invoked");
            }
            else if (throneEvent != null)
            {
                Console.WriteLine($"[ThroneService] Event timestamp check failed. Event is old or already processed.");
            }
        }
        catch (JsonException ex)
        {
            _logger?.LogDebug(ex, "Failed to parse WebSocket message: {Json}", json[..Math.Min(200, json.Length)]);
            Console.WriteLine($"[ThroneService] JSON parse error: {ex.Message}");
        }
    }

    private ThroneEvent? ParseWebSocketMessage(JsonElement root)
    {
        try
        {
            if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "change")
            {
                if (!root.TryGetProperty("data", out var dataElement))
                {
                    _logger?.LogDebug("Change message has no data property");
                    return null;
                }

                return ParseEventData(root, dataElement);
            }

            return ParseEventData(root, root);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error parsing WebSocket message");
            return null;
        }
    }

    private ThroneEvent? ParseEventData(JsonElement root, JsonElement dataElement)
    {
        var evt = new ThroneEvent();

        if (root.TryGetProperty("documentId", out var docIdProp))
            evt.Id = docIdProp.GetString() ?? string.Empty;
        else if (dataElement.TryGetProperty("_id", out var idProp))
            evt.Id = idProp.GetString() ?? string.Empty;
        else if (dataElement.TryGetProperty("id", out var id2Prop))
            evt.Id = id2Prop.GetString() ?? string.Empty;

        if (dataElement.TryGetProperty("creatorId", out var creatorIdProp))
            evt.CreatorId = creatorIdProp.GetString() ?? string.Empty;

        if (dataElement.TryGetProperty("createdAt", out var createdAtProp))
        {
            if (createdAtProp.ValueKind == JsonValueKind.Number)
                evt.CreatedAt = createdAtProp.GetInt64();
            else if (createdAtProp.ValueKind == JsonValueKind.String &&
                     long.TryParse(createdAtProp.GetString(), out var timestamp))
                evt.CreatedAt = timestamp;
        }

        if (dataElement.TryGetProperty("overlayInformation", out var overlayProp))
        {
            evt.OverlayInformation = ParseOverlayInformation(overlayProp);
        }

        if (!string.IsNullOrEmpty(evt.Id) || evt.OverlayInformation != null)
            return evt;

        return null;
    }

    private OverlayInformation ParseOverlayInformation(JsonElement element)
    {
        var overlay = new OverlayInformation();

        if (element.TryGetProperty("gifterUsername", out var gifterProp))
            overlay.GifterUsername = gifterProp.GetString() ?? "Anonymous";

        if (element.TryGetProperty("message", out var messageProp))
            overlay.Message = messageProp.GetString() ?? string.Empty;

        if (element.TryGetProperty("type", out var typeProp))
            overlay.Type = typeProp.GetString() ?? "Unknown";

        if (element.TryGetProperty("itemImage", out var imageProp))
            overlay.ItemImage = imageProp.GetString();

        if (element.TryGetProperty("itemName", out var nameProp))
            overlay.ItemName = nameProp.GetString();

        if (element.TryGetProperty("amount", out var amountProp))
        {
            if (amountProp.ValueKind == JsonValueKind.Number)
                overlay.Amount = amountProp.GetDouble();
            else if (amountProp.ValueKind == JsonValueKind.String &&
                    double.TryParse(amountProp.GetString(), out var amount))
                overlay.Amount = amount;
        }

        return overlay;
    }

    public void Dispose()
    {
        Stop();
        _listenerCts?.Dispose();
        _webSocket?.Dispose();
    }

    private class WebSocketSubscribeMessage
    {
        [JsonPropertyName("action")]
        public string Action { get; set; } = string.Empty;

        [JsonPropertyName("collection")]
        public string Collection { get; set; } = string.Empty;

        [JsonPropertyName("creatorId")]
        public string CreatorId { get; set; } = string.Empty;
    }
}
