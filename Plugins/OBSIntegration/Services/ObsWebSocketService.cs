using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OBSIntegration.Models;

namespace OBSIntegration.Services;

public class ObsWebSocketService : IAsyncDisposable
{
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _connectionCts;
    private Task? _receiveTask;
    private bool _isConnected;
    private bool _isConnecting;

    private readonly ConcurrentDictionary<string, TaskCompletionSource<ObsRequestResponseData>> _pendingRequests = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public bool IsConnected => _isConnected && _webSocket?.State == WebSocketState.Open;
    public bool IsConnecting => _isConnecting;

    public string? ConnectedHost { get; private set; }
    public string? ObsVersion { get; private set; }
    public string? WebSocketVersion { get; private set; }

    // Connection events
    public event Action<bool>? ConnectionStateChanged;
    public event Action<string>? ErrorOccurred;
    public event Action<string>? StatusMessage;

    // OBS events
    public event Action<string, Dictionary<string, object?>?>? OnEvent;
    public event Action<SceneChangedEventData>? OnCurrentProgramSceneChanged;
    public event Action<SceneChangedEventData>? OnCurrentPreviewSceneChanged;
    public event Action<SceneItemEnableStateChangedEventData>? OnSceneItemEnableStateChanged;
    public event Action<InputMuteStateChangedEventData>? OnInputMuteStateChanged;
    public event Action<SourceFilterEnableStateChangedEventData>? OnSourceFilterEnableStateChanged;
    public event Action<StreamStateChangedEventData>? OnStreamStateChanged;
    public event Action<RecordStateChangedEventData>? OnRecordStateChanged;

    public async Task<bool> ConnectAsync(string host, int port, string? password = null)
    {
        if (_isConnecting || IsConnected)
        {
            return false;
        }

        _isConnecting = true;
        ConnectedHost = $"{host}:{port}";

        try
        {
            _connectionCts = new CancellationTokenSource();
            _webSocket = new ClientWebSocket();

            var uri = new Uri($"ws://{host}:{port}");
            StatusMessage?.Invoke($"Connecting to {uri}...");

            await _webSocket.ConnectAsync(uri, _connectionCts.Token);
            StatusMessage?.Invoke("WebSocket connected, waiting for Hello...");

            // Start receive loop
            _receiveTask = ReceiveMessagesAsync(_connectionCts.Token, password);

            // Wait for identification to complete (with timeout)
            var timeout = Task.Delay(10000, _connectionCts.Token);
            while (!_isConnected && !timeout.IsCompleted && _webSocket.State == WebSocketState.Open)
            {
                await Task.Delay(100, _connectionCts.Token);
            }

            if (!_isConnected)
            {
                throw new Exception("Failed to identify with OBS");
            }

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
        _connectionCts?.Cancel();

        foreach (var pending in _pendingRequests.Values)
        {
            pending.TrySetCanceled();
        }
        _pendingRequests.Clear();

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

        ConnectedHost = null;
        ObsVersion = null;
        WebSocketVersion = null;
        ConnectionStateChanged?.Invoke(false);
        StatusMessage?.Invoke("Disconnected from OBS");
    }

    public async Task<ObsRequestResponseData?> SendRequestAsync(string requestType, Dictionary<string, object?>? requestData = null, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _webSocket == null)
        {
            return null;
        }

        var requestId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<ObsRequestResponseData>();
        _pendingRequests[requestId] = tcs;

        try
        {
            var request = new ObsMessage<ObsRequestData>
            {
                Op = ObsOpCode.Request,
                D = new ObsRequestData
                {
                    RequestType = requestType,
                    RequestId = requestId,
                    RequestData = requestData,
                }
            };

            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);

            // Wait for response with timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, timeoutCts.Token));
            if (completedTask == tcs.Task)
            {
                return await tcs.Task;
            }

            return null;
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken, string? password)
    {
        var buffer = new byte[65536];
        var messageBuffer = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    StatusMessage?.Invoke("WebSocket closed by OBS");
                    break;
                }

                messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (result.EndOfMessage)
                {
                    var message = messageBuffer.ToString();
                    messageBuffer.Clear();

                    await ProcessMessageAsync(message, password);
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

    private async Task ProcessMessageAsync(string message, string? password)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var op = doc.RootElement.GetProperty("op").GetInt32();

            switch (op)
            {
                case ObsOpCode.Hello:
                    await HandleHelloAsync(doc.RootElement.GetProperty("d"), password);
                    break;

                case ObsOpCode.Identified:
                    HandleIdentified(doc.RootElement.GetProperty("d"));
                    break;

                case ObsOpCode.Event:
                    HandleEvent(doc.RootElement.GetProperty("d"));
                    break;

                case ObsOpCode.RequestResponse:
                    HandleRequestResponse(doc.RootElement.GetProperty("d"));
                    break;
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Error processing message: {ex.Message}");
        }
    }

    private async Task HandleHelloAsync(JsonElement data, string? password)
    {
        var hello = JsonSerializer.Deserialize<ObsHelloData>(data.GetRawText(), _jsonOptions);
        if (hello == null) return;

        WebSocketVersion = hello.ObsWebSocketVersion;
        StatusMessage?.Invoke($"OBS WebSocket v{hello.ObsWebSocketVersion} (RPC v{hello.RpcVersion})");

        // Build identify message
        var identify = new ObsIdentifyData
        {
            RpcVersion = hello.RpcVersion,
            EventSubscriptions = (int)ObsEventSubscription.All,
        };

        // Handle authentication if required
        if (hello.Authentication != null)
        {
            if (string.IsNullOrEmpty(password))
            {
                ErrorOccurred?.Invoke("OBS requires authentication but no password provided");
                await DisconnectAsync();
                return;
            }

            identify.Authentication = GenerateAuthString(password, hello.Authentication.Salt, hello.Authentication.Challenge);
        }

        // Send identify
        var identifyMessage = new ObsMessage<ObsIdentifyData>
        {
            Op = ObsOpCode.Identify,
            D = identify,
        };

        var json = JsonSerializer.Serialize(identifyMessage, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket!.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _connectionCts?.Token ?? CancellationToken.None);
    }

    private void HandleIdentified(JsonElement data)
    {
        var identified = JsonSerializer.Deserialize<ObsIdentifiedData>(data.GetRawText(), _jsonOptions);
        StatusMessage?.Invoke($"Identified with OBS (RPC v{identified?.NegotiatedRpcVersion})");

        _isConnected = true;
        ConnectionStateChanged?.Invoke(true);

        // Get OBS version
        _ = Task.Run(async () =>
        {
            var response = await SendRequestAsync(ObsRequestTypes.GetVersion);
            if (response?.ResponseData != null)
            {
                if (response.ResponseData.TryGetValue("obsVersion", out var version))
                {
                    ObsVersion = version?.ToString();
                }
            }
        });
    }

    private void HandleEvent(JsonElement data)
    {
        var eventData = JsonSerializer.Deserialize<ObsEventData>(data.GetRawText(), _jsonOptions);
        if (eventData == null) return;

        // Fire generic event
        OnEvent?.Invoke(eventData.EventType, eventData.EventData);

        // Fire typed events
        var eventJson = eventData.EventData != null ? JsonSerializer.Serialize(eventData.EventData, _jsonOptions) : "{}";

        try
        {
            switch (eventData.EventType)
            {
                case ObsEventTypes.CurrentProgramSceneChanged:
                    var sceneChanged = JsonSerializer.Deserialize<SceneChangedEventData>(eventJson, _jsonOptions);
                    if (sceneChanged != null) OnCurrentProgramSceneChanged?.Invoke(sceneChanged);
                    break;

                case ObsEventTypes.CurrentPreviewSceneChanged:
                    var previewChanged = JsonSerializer.Deserialize<SceneChangedEventData>(eventJson, _jsonOptions);
                    if (previewChanged != null) OnCurrentPreviewSceneChanged?.Invoke(previewChanged);
                    break;

                case ObsEventTypes.SceneItemEnableStateChanged:
                    var itemEnabled = JsonSerializer.Deserialize<SceneItemEnableStateChangedEventData>(eventJson, _jsonOptions);
                    if (itemEnabled != null) OnSceneItemEnableStateChanged?.Invoke(itemEnabled);
                    break;

                case ObsEventTypes.InputMuteStateChanged:
                    var muteChanged = JsonSerializer.Deserialize<InputMuteStateChangedEventData>(eventJson, _jsonOptions);
                    if (muteChanged != null) OnInputMuteStateChanged?.Invoke(muteChanged);
                    break;

                case ObsEventTypes.SourceFilterEnableStateChanged:
                    var filterEnabled = JsonSerializer.Deserialize<SourceFilterEnableStateChangedEventData>(eventJson, _jsonOptions);
                    if (filterEnabled != null) OnSourceFilterEnableStateChanged?.Invoke(filterEnabled);
                    break;

                case ObsEventTypes.StreamStateChanged:
                    var streamState = JsonSerializer.Deserialize<StreamStateChangedEventData>(eventJson, _jsonOptions);
                    if (streamState != null) OnStreamStateChanged?.Invoke(streamState);
                    break;

                case ObsEventTypes.RecordStateChanged:
                    var recordState = JsonSerializer.Deserialize<RecordStateChangedEventData>(eventJson, _jsonOptions);
                    if (recordState != null) OnRecordStateChanged?.Invoke(recordState);
                    break;
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Error deserializing event {eventData.EventType}: {ex.Message}");
        }
    }

    private void HandleRequestResponse(JsonElement data)
    {
        var response = JsonSerializer.Deserialize<ObsRequestResponseData>(data.GetRawText(), _jsonOptions);
        if (response == null) return;

        if (_pendingRequests.TryRemove(response.RequestId, out var tcs))
        {
            tcs.TrySetResult(response);
        }
    }

    private static string GenerateAuthString(string password, string salt, string challenge)
    {
        // OBS WebSocket 5.x authentication:
        // 1. Concatenate password + salt
        // 2. SHA256 hash the result
        // 3. Base64 encode the hash
        // 4. Concatenate base64 + challenge
        // 5. SHA256 hash that result
        // 6. Base64 encode the final hash

        using var sha256 = SHA256.Create();

        // Step 1-3: hash(password + salt) -> base64
        var passwordSalt = password + salt;
        var passwordSaltBytes = Encoding.UTF8.GetBytes(passwordSalt);
        var passwordSaltHash = sha256.ComputeHash(passwordSaltBytes);
        var base64Secret = Convert.ToBase64String(passwordSaltHash);

        // Step 4-6: hash(base64Secret + challenge) -> base64
        var secretChallenge = base64Secret + challenge;
        var secretChallengeBytes = Encoding.UTF8.GetBytes(secretChallenge);
        var finalHash = sha256.ComputeHash(secretChallengeBytes);
        return Convert.ToBase64String(finalHash);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }

    // Convenience methods for common operations

    public async Task<List<SceneInfo>> GetSceneListAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(ObsRequestTypes.GetSceneList, cancellationToken: cancellationToken);
        if (response?.RequestStatus.Result != true || response.ResponseData == null)
        {
            return new List<SceneInfo>();
        }

        var json = JsonSerializer.Serialize(response.ResponseData, _jsonOptions);
        var data = JsonSerializer.Deserialize<SceneListResponse>(json, _jsonOptions);
        return data?.Scenes ?? new List<SceneInfo>();
    }

    public async Task<string?> GetCurrentProgramSceneAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(ObsRequestTypes.GetCurrentProgramScene, cancellationToken: cancellationToken);
        if (response?.RequestStatus.Result != true || response.ResponseData == null)
        {
            return null;
        }

        if (response.ResponseData.TryGetValue("currentProgramSceneName", out var sceneName))
        {
            return sceneName?.ToString();
        }
        return null;
    }

    public async Task<bool> SetCurrentProgramSceneAsync(string sceneName, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(ObsRequestTypes.SetCurrentProgramScene,
            new Dictionary<string, object?> { ["sceneName"] = sceneName },
            cancellationToken);
        return response?.RequestStatus.Result == true;
    }

    public async Task<List<SceneItemInfo>> GetSceneItemListAsync(string sceneName, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(ObsRequestTypes.GetSceneItemList,
            new Dictionary<string, object?> { ["sceneName"] = sceneName },
            cancellationToken);

        if (response?.RequestStatus.Result != true || response.ResponseData == null)
        {
            return new List<SceneItemInfo>();
        }

        if (response.ResponseData.TryGetValue("sceneItems", out var items) && items is JsonElement element)
        {
            return JsonSerializer.Deserialize<List<SceneItemInfo>>(element.GetRawText(), _jsonOptions) ?? new List<SceneItemInfo>();
        }

        return new List<SceneItemInfo>();
    }

    public async Task<int?> GetSceneItemIdAsync(string sceneName, string sourceName, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(ObsRequestTypes.GetSceneItemId,
            new Dictionary<string, object?>
            {
                ["sceneName"] = sceneName,
                ["sourceName"] = sourceName,
            },
            cancellationToken);

        if (response?.RequestStatus.Result != true || response.ResponseData == null)
        {
            return null;
        }

        if (response.ResponseData.TryGetValue("sceneItemId", out var id))
        {
            if (id is JsonElement element && element.TryGetInt32(out var intId))
            {
                return intId;
            }
            if (id is int intValue)
            {
                return intValue;
            }
        }

        return null;
    }

    public async Task<bool> SetSceneItemEnabledAsync(string sceneName, int sceneItemId, bool enabled, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(ObsRequestTypes.SetSceneItemEnabled,
            new Dictionary<string, object?>
            {
                ["sceneName"] = sceneName,
                ["sceneItemId"] = sceneItemId,
                ["sceneItemEnabled"] = enabled,
            },
            cancellationToken);
        return response?.RequestStatus.Result == true;
    }

    public async Task<bool?> GetSceneItemEnabledAsync(string sceneName, int sceneItemId, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(ObsRequestTypes.GetSceneItemEnabled,
            new Dictionary<string, object?>
            {
                ["sceneName"] = sceneName,
                ["sceneItemId"] = sceneItemId,
            },
            cancellationToken);

        if (response?.RequestStatus.Result != true || response.ResponseData == null)
        {
            return null;
        }

        if (response.ResponseData.TryGetValue("sceneItemEnabled", out var enabled))
        {
            if (enabled is JsonElement element)
            {
                return element.GetBoolean();
            }
            if (enabled is bool boolValue)
            {
                return boolValue;
            }
        }

        return null;
    }

    public async Task<bool> SetSourceFilterEnabledAsync(string sourceName, string filterName, bool enabled, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(ObsRequestTypes.SetSourceFilterEnabled,
            new Dictionary<string, object?>
            {
                ["sourceName"] = sourceName,
                ["filterName"] = filterName,
                ["filterEnabled"] = enabled,
            },
            cancellationToken);
        return response?.RequestStatus.Result == true;
    }

    public async Task<List<FilterInfo>> GetSourceFilterListAsync(string sourceName, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(ObsRequestTypes.GetSourceFilterList,
            new Dictionary<string, object?> { ["sourceName"] = sourceName },
            cancellationToken);

        if (response?.RequestStatus.Result != true || response.ResponseData == null)
        {
            return new List<FilterInfo>();
        }

        if (response.ResponseData.TryGetValue("filters", out var filters) && filters is JsonElement element)
        {
            return JsonSerializer.Deserialize<List<FilterInfo>>(element.GetRawText(), _jsonOptions) ?? new List<FilterInfo>();
        }

        return new List<FilterInfo>();
    }

    public async Task<bool?> GetSourceFilterEnabledAsync(string sourceName, string filterName, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(ObsRequestTypes.GetSourceFilter,
            new Dictionary<string, object?>
            {
                ["sourceName"] = sourceName,
                ["filterName"] = filterName,
            },
            cancellationToken);

        if (response?.RequestStatus.Result != true || response.ResponseData == null)
        {
            return null;
        }

        if (response.ResponseData.TryGetValue("filterEnabled", out var enabled))
        {
            if (enabled is JsonElement element)
            {
                return element.GetBoolean();
            }
            if (enabled is bool boolValue)
            {
                return boolValue;
            }
        }

        return null;
    }

    public async Task<bool> ToggleStreamAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(ObsRequestTypes.ToggleStream, cancellationToken: cancellationToken);
        return response?.RequestStatus.Result == true;
    }

    public async Task<bool> StartStreamAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(ObsRequestTypes.StartStream, cancellationToken: cancellationToken);
        return response?.RequestStatus.Result == true;
    }

    public async Task<bool> StopStreamAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(ObsRequestTypes.StopStream, cancellationToken: cancellationToken);
        return response?.RequestStatus.Result == true;
    }

    public async Task<bool> ToggleRecordAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(ObsRequestTypes.ToggleRecord, cancellationToken: cancellationToken);
        return response?.RequestStatus.Result == true;
    }

    public async Task<bool> StartRecordAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(ObsRequestTypes.StartRecord, cancellationToken: cancellationToken);
        return response?.RequestStatus.Result == true;
    }

    public async Task<bool> StopRecordAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(ObsRequestTypes.StopRecord, cancellationToken: cancellationToken);
        return response?.RequestStatus.Result == true;
    }

    public async Task<bool> SetInputMuteAsync(string inputName, bool muted, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(ObsRequestTypes.SetInputMute,
            new Dictionary<string, object?>
            {
                ["inputName"] = inputName,
                ["inputMuted"] = muted,
            },
            cancellationToken);
        return response?.RequestStatus.Result == true;
    }

    public async Task<bool> ToggleInputMuteAsync(string inputName, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(ObsRequestTypes.ToggleInputMute,
            new Dictionary<string, object?> { ["inputName"] = inputName },
            cancellationToken);
        return response?.RequestStatus.Result == true;
    }

    public async Task<List<InputInfo>> GetInputListAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(ObsRequestTypes.GetInputList, cancellationToken: cancellationToken);

        if (response?.RequestStatus.Result != true || response.ResponseData == null)
        {
            return new List<InputInfo>();
        }

        if (response.ResponseData.TryGetValue("inputs", out var inputs) && inputs is JsonElement element)
        {
            return JsonSerializer.Deserialize<List<InputInfo>>(element.GetRawText(), _jsonOptions) ?? new List<InputInfo>();
        }

        return new List<InputInfo>();
    }

    public async Task<bool> TriggerHotkeyByNameAsync(string hotkeyName, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(ObsRequestTypes.TriggerHotkeyByName,
            new Dictionary<string, object?> { ["hotkeyName"] = hotkeyName },
            cancellationToken);
        return response?.RequestStatus.Result == true;
    }
}
