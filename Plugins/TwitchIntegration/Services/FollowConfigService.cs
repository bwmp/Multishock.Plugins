using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;

namespace TwitchIntegration.Services;

public class FollowConfigService : IDisposable
{
    private const string ConfigFileName = "follow-config.json";

    private readonly IPluginHost _pluginHost;
    private readonly IDeviceActions _deviceActions;
    private readonly TwitchEventSubService _eventSubService;
    private readonly TwitchAuthService _authService;
    private readonly HttpClient _httpClient = new();
    private readonly string _configPath;
    private readonly object _roundRobinLock = new();
    private readonly HashSet<string> _knownFollowerIds = new();

    private int _roundRobinIndex;
    private bool _isDisposed;
    private bool _isFetching;
    private bool _hasFetchedThisSession;
    private FollowConfig _config = new();

    public event Action? ConfigChanged;

    public FollowConfig Config => _config;

    public bool IsFetchingFollowers => _isFetching;

    public int KnownFollowerCount => _knownFollowerIds.Count;

    public FollowConfigService(
        IPluginHost pluginHost,
        IDeviceActions deviceActions,
        TwitchEventSubService eventSubService,
        TwitchAuthService authService)
    {
        _pluginHost = pluginHost;
        _deviceActions = deviceActions;
        _eventSubService = eventSubService;
        _authService = authService;

        var dataPath = _pluginHost.GetPluginDataPath("com.multishock.twitchintegration");
        _configPath = Path.Combine(dataPath, ConfigFileName);

        LoadConfig();

        _eventSubService.OnFollow += HandleFollowEvent;
        _eventSubService.ConnectionStateChanged += OnConnectionStateChanged;

        if (_config.FetchFollowersOnStartup)
        {
            _ = Task.Run(async () => await FetchFollowersAsync());
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        _eventSubService.OnFollow -= HandleFollowEvent;
        _eventSubService.ConnectionStateChanged -= OnConnectionStateChanged;
        _httpClient.Dispose();
        _isDisposed = true;
    }

    public void SetEnabled(bool enabled)
    {
        _config.Enabled = enabled;
        SaveConfig();
    }

    public void SetFetchFollowersOnStartup(bool enabled)
    {
        _config.FetchFollowersOnStartup = enabled;
        SaveConfig();
    }

    public void UpdateIntensity(int intensity)
    {
        _config.Intensity = Math.Clamp(intensity, 1, 100);
        SaveConfig();
    }

    public void UpdateDuration(double duration)
    {
        _config.Duration = Math.Clamp(duration, 0.1, 15.0);
        SaveConfig();
    }

    public void UpdateMode(SelectionMode mode)
    {
        _config.Mode = mode;
        SaveConfig();
    }

    public void UpdateCommandType(string commandType)
    {
        _config.CommandType = commandType;
        SaveConfig();
    }

    public void UpdateSelectedShockers(List<string> ids)
    {
        _config.SelectedShockerIds = ids;
        SaveConfig();
    }

    public async Task<FollowFetchResult> FetchFollowersAsync(CancellationToken cancellationToken = default)
    {
        if (_isFetching)
        {
            return new FollowFetchResult(false, "Already fetching followers", 0);
        }

        var token = _authService.StoredToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            return new FollowFetchResult(false, "No OAuth token. Connect Twitch first.", 0);
        }

        _isFetching = true;
        try
        {
            var userId = _eventSubService.CurrentUserId;
            var userName = _eventSubService.CurrentUserName;

            if (string.IsNullOrEmpty(userId))
            {
                var validation = await _eventSubService.ValidateTokenAsync(token);
                if (!validation.IsValid || validation.UserId == null)
                {
                    return new FollowFetchResult(false, validation.Error ?? "Unable to validate token", 0);
                }

                userId = validation.UserId;
                userName = validation.UserName;
            }

            var baseUrl = _authService.UseLocalCli ? TwitchConstants.HelixApiBaseUrlLocal : TwitchConstants.HelixApiBaseUrl;
            var cursor = (string?)null;
            var added = 0;

            do
            {
                var url = $"{baseUrl}/channels/followers?broadcaster_id={userId}&first=100";
                if (!string.IsNullOrEmpty(cursor))
                {
                    url += $"&after={Uri.EscapeDataString(cursor)}";
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Add("Client-Id", TwitchConstants.ClientId);

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var reason = await response.Content.ReadAsStringAsync(cancellationToken);
                    return new FollowFetchResult(false, $"Twitch API error: {(int)response.StatusCode} {response.ReasonPhrase}. {reason}", added);
                }

                var payload = await response.Content.ReadAsStringAsync(cancellationToken);
                var page = JsonSerializer.Deserialize<FollowerListResponse>(payload);
                if (page?.Data == null || page.Data.Count == 0)
                {
                    break;
                }

                foreach (var follower in page.Data)
                {
                    if (_knownFollowerIds.Add(follower.UserId))
                    {
                        added++;
                    }
                }

                cursor = page.Pagination?.Cursor;
            }
            while (!string.IsNullOrEmpty(cursor) && !cancellationToken.IsCancellationRequested);

            _config.KnownFollowerIds = _knownFollowerIds.ToList();
            _config.LastFetchedUtc = DateTime.UtcNow;
            _hasFetchedThisSession = true;
            SaveConfig();

            var userLabel = string.IsNullOrEmpty(userName) ? "Twitch" : userName;
            return new FollowFetchResult(true, $"Fetched {_knownFollowerIds.Count} followers for {userLabel}", added);
        }
        catch (OperationCanceledException)
        {
            return new FollowFetchResult(false, "Follower fetch cancelled", 0);
        }
        catch (Exception ex)
        {
            return new FollowFetchResult(false, $"Follower fetch failed: {ex.Message}", 0);
        }
        finally
        {
            _isFetching = false;
        }
    }

    public FollowResetResult ResetKnownFollowers()
    {
        var removed = _knownFollowerIds.Count;
        _knownFollowerIds.Clear();
        _config.KnownFollowerIds.Clear();
        SaveConfig();
        return new FollowResetResult(removed);
    }

    private void OnConnectionStateChanged(bool connected)
    {
        if (!connected) return;
        if (_hasFetchedThisSession) return;
        if (!_config.FetchFollowersOnStartup) return;

        _ = Task.Run(async () => await FetchFollowersAsync());
    }

    private void HandleFollowEvent(FollowEvent followEvent)
    {
        var userId = !string.IsNullOrEmpty(followEvent.UserId) ? followEvent.UserId : followEvent.UserName;
        if (string.IsNullOrEmpty(userId)) return;

        var added = _knownFollowerIds.Add(userId);
        if (added)
        {
            _config.KnownFollowerIds = _knownFollowerIds.ToList();
            SaveConfig();
        }

        if (!added) return;

        if (!_config.Enabled) return;
        if (!_config.SelectedShockerIds.Any()) return;

        var intensity = Math.Clamp(_config.Intensity, 1, 100);
        var duration = Math.Clamp(_config.Duration, 0.1, 15.0);

        ExecuteAction(intensity, duration, _config.CommandType, _config.Mode);
    }

    private void ExecuteAction(int intensity, double duration, string commandTypeString, SelectionMode mode)
    {
        var commandType = commandTypeString switch
        {
            "Shock" => CommandType.Shock,
            "Vibrate" => CommandType.Vibrate,
            "Beep" => CommandType.Beep,
            _ => CommandType.Vibrate
        };

        var shockerIds = GetShockerIdsForExecution(mode);
        if (!shockerIds.Any()) return;

        var parsedIds = shockerIds
            .Select(id => id.Split(':'))
            .Where(parts => parts.Length == 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _))
            .Select(parts => (deviceId: int.Parse(parts[0]), shockerId: int.Parse(parts[1])))
            .ToList();

        if (!parsedIds.Any()) return;

        var deviceIds = parsedIds.Select(p => p.deviceId).Distinct();
        var shockerIdInts = parsedIds.Select(p => p.shockerId);

        _deviceActions.PerformAction(
            intensity: intensity,
            durationSeconds: duration,
            command: commandType,
            deviceIds: deviceIds,
            shockerIds: shockerIdInts
        );
    }

    private List<string> GetShockerIdsForExecution(SelectionMode mode)
    {
        var availableIds = _config.SelectedShockerIds
            .Where(IsShockerLoaded)
            .ToList();

        if (!availableIds.Any()) return [];

        return mode switch
        {
            SelectionMode.All => availableIds,
            SelectionMode.Random => [availableIds[Random.Shared.Next(availableIds.Count)]],
            SelectionMode.RoundRobin => [GetNextRoundRobinShocker(availableIds)],
            _ => availableIds
        };
    }

    private string GetNextRoundRobinShocker(List<string> availableIds)
    {
        lock (_roundRobinLock)
        {
            if (_roundRobinIndex >= availableIds.Count)
            {
                _roundRobinIndex = 0;
            }

            var result = availableIds[_roundRobinIndex];
            _roundRobinIndex = (_roundRobinIndex + 1) % availableIds.Count;
            return result;
        }
    }

    private bool IsShockerLoaded(string combinedId)
    {
        var parts = combinedId.Split(':');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out var deviceId)) return false;
        if (!int.TryParse(parts[1], out var shockerId)) return false;

        return _deviceActions.IsShockerLoaded(deviceId, shockerId);
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<FollowConfig>(json) ?? CreateDefaultConfig();
            }
            else
            {
                _config = CreateDefaultConfig();
                SaveConfig();
            }
        }
        catch (Exception ex)
        {
            TwitchIntegration.Logger?.LogError(ex, "Failed to load follow config");
            _config = CreateDefaultConfig();
        }

        _knownFollowerIds.Clear();
        foreach (var id in (_config.KnownFollowerIds ?? []).Distinct())
        {
            _knownFollowerIds.Add(id);
        }
    }

    private void SaveConfig()
    {
        try
        {
            _config.KnownFollowerIds = _knownFollowerIds.ToList();
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
            ConfigChanged?.Invoke();
        }
        catch (Exception ex)
        {
            TwitchIntegration.Logger?.LogError(ex, "Failed to save follow config");
        }
    }

    private static FollowConfig CreateDefaultConfig()
    {
        return new FollowConfig
        {
            Enabled = true,
            FetchFollowersOnStartup = true,
            Intensity = 30,
            Duration = 1.0,
            Mode = SelectionMode.All,
            CommandType = "Shock",
            SelectedShockerIds = new List<string>(),
            KnownFollowerIds = new List<string>()
        };
    }

    private sealed class FollowerListResponse
    {
        [JsonPropertyName("data")]
        public List<FollowerUser> Data { get; set; } = new();

        [JsonPropertyName("pagination")]
        public Pagination? Pagination { get; set; }
    }

    private sealed class FollowerUser
    {
        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = string.Empty;
    }

    private sealed class Pagination
    {
        [JsonPropertyName("cursor")]
        public string? Cursor { get; set; }
    }
}

public record FollowFetchResult(bool Success, string Message, int AddedCount);

public record FollowResetResult(int RemovedCount);
