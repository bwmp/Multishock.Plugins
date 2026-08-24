using System.IO.Compression;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TikTokIntegration.Protocol;

public sealed class TikTokRoomInfo
{
    public string RoomId { get; init; } = "";
    public int Status { get; init; }
    public string Title { get; init; } = "";
    public string OwnerNickname { get; init; } = "";

    /// <summary>TikTok room status 2 = live, 4 = ended.</summary>
    public bool IsLive => Status == 2;
}

public sealed class TikTokGiftInfo
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int DiamondCount { get; init; }
    public int GiftType { get; init; }
    public string ImageUrl { get; init; } = "";
}

/// <summary>
/// Connects to a TikTok LIVE room the same way TikTok's web viewer does:
/// resolve the room id from the streamer's live page, perform a signed initial
/// im/fetch request (signed via a community sign server), then upgrade to the
/// webcast websocket and decode the protobuf event stream.
/// </summary>
public sealed class TikTokLiveClient : IAsyncDisposable
{
    private static readonly Regex RoomIdJsonRegex = new("roomId\":\"([0-9]+)\"", RegexOptions.Compiled);
    private static readonly Regex RoomIdMetaRegex = new("room_id=([0-9]+)", RegexOptions.Compiled);

    private readonly TikTokSignProvider _signProvider;
    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _http;
    private readonly Dictionary<string, string> _clientParams = TikTokConstants.CreateDefaultClientParams();
    private readonly object _sendLock = new();

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Timer? _pingTimer;
    private readonly string _userAgent = TikTokConstants.DefaultUserAgent;
    private readonly string? _sessionId;
    private bool _disconnectReported;

    public string UniqueId { get; }

    public string? RoomId { get; private set; }

    public TikTokRoomInfo? Room { get; private set; }

    public IReadOnlyList<TikTokGiftInfo> AvailableGifts { get; private set; } = [];

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public event Action<WebcastChat>? ChatReceived;
    public event Action<WebcastGift>? GiftReceived;
    public event Action<WebcastLike>? LikeReceived;
    public event Action<WebcastSocial>? SocialReceived;
    public event Action<WebcastMember>? MemberJoined;
    public event Action<WebcastSubNotify>? SubscribeReceived;
    public event Action<WebcastEnvelope>? EnvelopeReceived;
    public event Action<int>? ViewerCountChanged;
    public event Action? StreamEnded;
    public event Action<string>? Disconnected;
    public event Action<string>? StatusMessage;

    public TikTokLiveClient(string uniqueId, TikTokSignProvider signProvider, string? sessionId = null)
    {
        UniqueId = NormalizeUniqueId(uniqueId);
        _signProvider = signProvider;

        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _userAgent);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/json,application/protobuf");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", TikTokConstants.WebUrl);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", TikTokConstants.Origin);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");

        _sessionId = sessionId;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            SetCookie("sessionid", sessionId);
            SetCookie("sessionid_ss", sessionId);
            SetCookie("sid_tt", sessionId);
        }
    }

    public static string NormalizeUniqueId(string uniqueId)
    {
        return uniqueId
            .Replace("https://www.tiktok.com/", "", StringComparison.OrdinalIgnoreCase)
            .Replace("/live", "", StringComparison.OrdinalIgnoreCase)
            .Replace("@", "")
            .Trim();
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            throw new TikTokLiveException("Already connected");
        }

        _disconnectReported = false;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _cts.Token;

        StatusMessage?.Invoke($"Looking up room for @{UniqueId}...");
        await ResolveRoomIdAsync(ct);

        StatusMessage?.Invoke($"Fetching room info (room {RoomId})...");
        Room = await FetchRoomInfoAsync(ct);
        if (Room is { IsLive: false })
        {
            throw new UserOfflineException($"@{UniqueId} is not live right now (room status {Room.Status})");
        }

        try
        {
            AvailableGifts = await FetchGiftListAsync(ct);
            StatusMessage?.Invoke($"Loaded {AvailableGifts.Count} available gifts");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Gift catalog is informational only; connection can proceed without it
            StatusMessage?.Invoke($"Could not load gift catalog: {ex.Message}");
        }

        StatusMessage?.Invoke("Requesting signed webcast access from sign server...");
        var initialResponse = await FetchInitialRoomDataAsync(ct);

        if (string.IsNullOrEmpty(initialResponse.WsUrl))
        {
            throw new TikTokLiveException("TikTok did not offer a websocket upgrade. Try again in a moment.");
        }

        StatusMessage?.Invoke("Connecting to webcast websocket...");
        await ConnectWebSocketAsync(initialResponse, ct);

        // Deliver events that arrived with the initial fetch (room stats etc.)
        ProcessResponse(initialResponse);

        StatusMessage?.Invoke("Connected to TikTok LIVE");
    }

    public async Task DisconnectAsync(string reason = "Disconnected")
    {
        _pingTimer?.Dispose();
        _pingTimer = null;
        _cts?.Cancel();

        if (_ws != null)
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", closeCts.Token);
                }
            }
            catch { }
            finally
            {
                _ws.Dispose();
                _ws = null;
            }
        }

        ReportDisconnected(reason);
    }

    private void ReportDisconnected(string reason)
    {
        if (_disconnectReported)
        {
            return;
        }
        _disconnectReported = true;
        Disconnected?.Invoke(reason);
    }

    // ========== HTTP: room id / room info / gifts ==========

    private async Task ResolveRoomIdAsync(CancellationToken ct)
    {
        string? roomId = null;
        string? html = null;

        try
        {
            html = await _http.GetStringAsync($"{TikTokConstants.WebUrl}@{Uri.EscapeDataString(UniqueId)}/live", ct);
            var match = RoomIdJsonRegex.Match(html);
            if (!match.Success)
            {
                match = RoomIdMetaRegex.Match(html);
            }
            if (match.Success)
            {
                roomId = match.Groups[1].Value;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"Live page lookup failed ({ex.Message}), trying API fallback...");
        }

        if (roomId == null)
        {
            roomId = await ResolveRoomIdViaApiAsync(ct);
        }

        if (string.IsNullOrEmpty(roomId) || roomId == "0")
        {
            var looksValid = html?.Contains("\"og:url\"") == true;
            throw new UserOfflineException(looksValid
                ? $"@{UniqueId} is not live right now."
                : $"Could not find a live room for @{UniqueId}. They may be offline, the username may be wrong, or TikTok may be blocking requests from this network.");
        }

        RoomId = roomId;
        _clientParams["room_id"] = roomId;
    }

    private async Task<string?> ResolveRoomIdViaApiAsync(CancellationToken ct)
    {
        var query = BuildQuery(_clientParams) + $"&uniqueId={Uri.EscapeDataString(UniqueId)}&sourceType=54";
        var json = await _http.GetStringAsync($"{TikTokConstants.WebUrl}api-live/user/room/?{query}", ct);

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var data) &&
            data.TryGetProperty("user", out var user) &&
            user.TryGetProperty("roomId", out var roomId))
        {
            return roomId.ValueKind switch
            {
                JsonValueKind.String => roomId.GetString(),
                JsonValueKind.Number => roomId.GetRawText(),
                _ => null,
            };
        }

        return null;
    }

    private async Task<TikTokRoomInfo?> FetchRoomInfoAsync(CancellationToken ct)
    {
        try
        {
            var json = await _http.GetStringAsync($"{TikTokConstants.WebcastUrl}room/info/?{BuildQuery(_clientParams)}", ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var data))
            {
                return null;
            }

            return new TikTokRoomInfo
            {
                RoomId = RoomId ?? "",
                Status = data.TryGetProperty("status", out var status) ? status.GetInt32() : 0,
                Title = data.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                OwnerNickname = data.TryGetProperty("owner", out var owner) &&
                                owner.TryGetProperty("nickname", out var nickname)
                    ? nickname.GetString() ?? ""
                    : "",
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Room info is a pre-flight check; if it fails we still attempt the connection
            StatusMessage?.Invoke($"Room info lookup failed: {ex.Message}");
            return null;
        }
    }

    private async Task<IReadOnlyList<TikTokGiftInfo>> FetchGiftListAsync(CancellationToken ct)
    {
        var json = await _http.GetStringAsync($"{TikTokConstants.WebcastUrl}gift/list/?{BuildQuery(_clientParams)}", ct);
        using var doc = JsonDocument.Parse(json);

        var gifts = new List<TikTokGiftInfo>();
        if (doc.RootElement.TryGetProperty("data", out var data) &&
            data.TryGetProperty("gifts", out var giftArray) &&
            giftArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var gift in giftArray.EnumerateArray())
            {
                var imageUrl = "";
                if (gift.TryGetProperty("image", out var image) &&
                    image.TryGetProperty("url_list", out var urls) &&
                    urls.ValueKind == JsonValueKind.Array && urls.GetArrayLength() > 0)
                {
                    imageUrl = urls[0].GetString() ?? "";
                }

                gifts.Add(new TikTokGiftInfo
                {
                    Id = gift.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                    Name = gift.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    DiamondCount = gift.TryGetProperty("diamond_count", out var diamonds) ? diamonds.GetInt32() : 0,
                    GiftType = gift.TryGetProperty("type", out var type) ? type.GetInt32() : 0,
                    ImageUrl = imageUrl,
                });
            }
        }

        return gifts.OrderBy(g => g.DiamondCount).ThenBy(g => g.Name).ToList();
    }

    // ========== Initial signed fetch + websocket ==========

    private async Task<WebcastResponse> FetchInitialRoomDataAsync(CancellationToken ct)
    {
        // The sign server proxies the signed im/fetch to TikTok and returns the resulting
        // WebcastResponse protobuf directly, along with the TikTok cookies to reuse on the ws.
        var signed = await _signProvider.FetchConnectAsync(RoomId!, _userAgent, _sessionId, ct);

        ApplyTikTokCookies(signed.TikTokCookies);

        var response = WebcastDecoder.DecodeResponse(signed.Payload);

        if (!string.IsNullOrEmpty(response.Cursor))
        {
            _clientParams["cursor"] = response.Cursor;
        }
        if (!string.IsNullOrEmpty(response.InternalExt))
        {
            _clientParams["internal_ext"] = response.InternalExt;
        }

        return response;
    }

    private void ApplyTikTokCookies(string? cookieHeader)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            return;
        }

        // Header is a standard "name=value; name2=value2" cookie string bound to .tiktok.com
        foreach (var part in cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var name = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim();
            if (name.Length == 0)
            {
                continue;
            }

            try
            {
                SetCookie(name, value);
            }
            catch
            {
                // Skip malformed cookies; the ws handshake tolerates missing optional ones
            }
        }
    }

    private async Task ConnectWebSocketAsync(WebcastResponse initialResponse, CancellationToken ct)
    {
        var wsParams = new Dictionary<string, string>(_clientParams)
        {
            ["compress"] = "gzip",
        };
        foreach (var (name, value) in initialResponse.WsParams)
        {
            wsParams[name] = value;
        }

        var wsUri = new Uri($"{initialResponse.WsUrl}?{BuildQuery(wsParams)}&version_code={TikTokConstants.WebcastVersionCode}");

        _ws = new ClientWebSocket();
        _ws.Options.Cookies = _cookies;
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
        _ws.Options.SetRequestHeader("User-Agent", _userAgent);
        _ws.Options.SetRequestHeader("Origin", TikTokConstants.Origin);

        await _ws.ConnectAsync(wsUri, ct);

        _receiveTask = Task.Run(() => ReceiveLoopAsync(ct), CancellationToken.None);
        _pingTimer = new Timer(_ => SendPing(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        using var messageStream = new MemoryStream();
        var reason = "Connection closed by TikTok";

        try
        {
            while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                messageStream.Write(buffer, 0, result.Count);

                if (result.EndOfMessage)
                {
                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        HandleFrame(messageStream.ToArray());
                    }
                    messageStream.SetLength(0);
                }
            }
        }
        catch (OperationCanceledException)
        {
            reason = "Disconnected";
        }
        catch (Exception ex)
        {
            reason = $"Websocket error: {ex.Message}";
        }
        finally
        {
            _pingTimer?.Dispose();
            _pingTimer = null;
            ReportDisconnected(reason);
        }
    }

    private void HandleFrame(byte[] data)
    {
        try
        {
            var frame = WebcastDecoder.DecodeWebsocketFrame(data);

            if (frame.Id > 0)
            {
                SendBinary(WebcastDecoder.EncodeAck(frame.Id));
            }

            if (frame.Type != "msg" || frame.Payload.Length == 0)
            {
                return;
            }

            var payload = MaybeGunzip(frame.Payload);
            var response = WebcastDecoder.DecodeResponse(payload);

            if (!string.IsNullOrEmpty(response.Cursor))
            {
                _clientParams["cursor"] = response.Cursor;
            }
            if (!string.IsNullOrEmpty(response.InternalExt))
            {
                _clientParams["internal_ext"] = response.InternalExt;
            }

            ProcessResponse(response);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"Failed to decode webcast frame: {ex.Message}");
        }
    }

    private void ProcessResponse(WebcastResponse response)
    {
        foreach (var message in response.Messages)
        {
            try
            {
                switch (message.Type)
                {
                    case "WebcastChatMessage":
                        ChatReceived?.Invoke(WebcastDecoder.DecodeChat(message.Binary));
                        break;
                    case "WebcastGiftMessage":
                        GiftReceived?.Invoke(WebcastDecoder.DecodeGift(message.Binary));
                        break;
                    case "WebcastLikeMessage":
                        LikeReceived?.Invoke(WebcastDecoder.DecodeLike(message.Binary));
                        break;
                    case "WebcastSocialMessage":
                        SocialReceived?.Invoke(WebcastDecoder.DecodeSocial(message.Binary));
                        break;
                    case "WebcastMemberMessage":
                        MemberJoined?.Invoke(WebcastDecoder.DecodeMember(message.Binary));
                        break;
                    case "WebcastSubNotifyMessage":
                        SubscribeReceived?.Invoke(WebcastDecoder.DecodeSubNotify(message.Binary));
                        break;
                    case "WebcastEnvelopeMessage":
                        EnvelopeReceived?.Invoke(WebcastDecoder.DecodeEnvelope(message.Binary));
                        break;
                    case "WebcastRoomUserSeqMessage":
                        ViewerCountChanged?.Invoke(WebcastDecoder.DecodeRoomUserSeq(message.Binary).ViewerCount);
                        break;
                    case "WebcastControlMessage":
                        var control = WebcastDecoder.DecodeControl(message.Binary);
                        if (control.Action == 3)
                        {
                            StreamEnded?.Invoke();
                            _ = DisconnectAsync("Stream ended");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"Failed to decode {message.Type}: {ex.Message}");
            }
        }
    }

    private void SendPing()
    {
        SendBinary(WebcastDecoder.PingFrame);
    }

    private void SendBinary(byte[] data)
    {
        var ws = _ws;
        if (ws?.State != WebSocketState.Open)
        {
            return;
        }

        // ClientWebSocket allows only one outstanding send; serialize them
        lock (_sendLock)
        {
            try
            {
                ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch
            {
                // Send failures surface via the receive loop
            }
        }
    }

    private static byte[] MaybeGunzip(byte[] data)
    {
        if (data.Length > 2 && data[0] == 0x1F && data[1] == 0x8B && data[2] == 0x08)
        {
            using var input = new MemoryStream(data);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }
        return data;
    }

    private void SetCookie(string name, string value)
    {
        _cookies.Add(new Cookie(name, value, "/", ".tiktok.com"));
    }

    private static string BuildQuery(Dictionary<string, string> parameters) =>
        string.Join("&", parameters.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _http.Dispose();
        _cts?.Dispose();
    }
}
