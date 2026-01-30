using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;

namespace TwitchIntegration.Services;

public class RedeemConfigService : IDisposable
{
    private const string ConfigFileName = "redeem-config.json";

    private readonly IPluginHost _pluginHost;
    private readonly IDeviceActions _deviceActions;
    private readonly TwitchEventSubService _eventSubService;
    private readonly TwitchAuthService _authService;
    private readonly HttpClient _httpClient = new();
    private readonly string _configPath;
    private readonly object _roundRobinLock = new();
    private readonly Dictionary<string, int> _roundRobinIndices = new();

    private bool _isDisposed;
    private bool _isFetching;
    private RedeemConfigRoot _config = new();

    public event Action? ConfigChanged;

    public RedeemConfigRoot Config => _config;

    public bool IsFetchingRedeems => _isFetching;

    public RedeemConfigService(
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

        _eventSubService.OnChannelPointRedemption += HandleChannelPointRedemption;
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        _eventSubService.OnChannelPointRedemption -= HandleChannelPointRedemption;
        _httpClient.Dispose();
        _isDisposed = true;
    }

    public void SetEnabled(bool enabled)
    {
        _config.Enabled = enabled;
        SaveConfig();
    }

    public void UpdateRedeem(RedeemConfig redeem)
    {
        var existing = _config.Redeems.FirstOrDefault(r => r.Id == redeem.Id);
        if (existing != null)
        {
            var index = _config.Redeems.IndexOf(existing);
            _config.Redeems[index] = redeem;
            SaveConfig();
        }
    }

    public void SetRedeemEnabled(string redeemId, bool enabled)
    {
        var redeem = _config.Redeems.FirstOrDefault(r => r.Id == redeemId);
        if (redeem != null)
        {
            redeem.Enabled = enabled;
            SaveConfig();
        }
    }

    public void UpdateRedeemIntensity(string redeemId, int intensity)
    {
        var redeem = _config.Redeems.FirstOrDefault(r => r.Id == redeemId);
        if (redeem != null)
        {
            redeem.Intensity = Math.Clamp(intensity, 1, 100);
            SaveConfig();
        }
    }

    public void UpdateRedeemDuration(string redeemId, double duration)
    {
        var redeem = _config.Redeems.FirstOrDefault(r => r.Id == redeemId);
        if (redeem != null)
        {
            redeem.Duration = Math.Clamp(duration, 0.1, 15.0);
            SaveConfig();
        }
    }

    public void UpdateRedeemMode(string redeemId, SelectionMode mode)
    {
        var redeem = _config.Redeems.FirstOrDefault(r => r.Id == redeemId);
        if (redeem != null)
        {
            redeem.Mode = mode;
            SaveConfig();
        }
    }

    public void UpdateRedeemCommandType(string redeemId, string commandType)
    {
        var redeem = _config.Redeems.FirstOrDefault(r => r.Id == redeemId);
        if (redeem != null)
        {
            redeem.CommandType = commandType;
            SaveConfig();
        }
    }

    public void UpdateRedeemShockers(string redeemId, List<string> shockerIds)
    {
        var redeem = _config.Redeems.FirstOrDefault(r => r.Id == redeemId);
        if (redeem != null)
        {
            redeem.SelectedShockerIds = shockerIds;
            SaveConfig();
        }
    }

    public async Task<RedeemFetchResult> FetchRedeemsAsync(CancellationToken cancellationToken = default)
    {
        if (_isFetching)
        {
            return new RedeemFetchResult(false, "Already fetching rewards", 0, 0);
        }

        var token = _authService.StoredToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            return new RedeemFetchResult(false, "No OAuth token. Connect Twitch first.", 0, 0);
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
                    return new RedeemFetchResult(false, validation.Error ?? "Unable to validate token", 0, 0);
                }

                userId = validation.UserId;
                userName = validation.UserName;
            }

            var baseUrl = _authService.UseLocalCli ? TwitchConstants.HelixApiBaseUrlLocal : TwitchConstants.HelixApiBaseUrl;
            var cursor = (string?)null;
            var allRewards = new List<TwitchReward>();

            do
            {
                var url = $"{baseUrl}/channel_points/custom_rewards?broadcaster_id={userId}";
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
                    return new RedeemFetchResult(false, $"Twitch API error: {(int)response.StatusCode} {response.ReasonPhrase}. {reason}", 0, 0);
                }

                var payload = await response.Content.ReadAsStringAsync(cancellationToken);
                var page = JsonSerializer.Deserialize<RewardListResponse>(payload);
                if (page?.Data == null || page.Data.Count == 0)
                {
                    break;
                }

                allRewards.AddRange(page.Data);
                cursor = page.Pagination?.Cursor;
            }
            while (!string.IsNullOrEmpty(cursor) && !cancellationToken.IsCancellationRequested);

            var added = 0;
            var updated = 0;

            // Check which rewards are manageable by this app
            var manageableRewardIds = await FetchManageableRewardIdsAsync(userId, token, baseUrl, cancellationToken);

            foreach (var reward in allRewards)
            {
                var isManageable = manageableRewardIds.Contains(reward.Id);
                var existing = _config.Redeems.FirstOrDefault(r => r.RewardId == reward.Id);
                if (existing != null)
                {
                    if (existing.RewardTitle != reward.Title || existing.Cost != reward.Cost || existing.IsManageable != isManageable)
                    {
                        existing.RewardTitle = reward.Title;
                        existing.Cost = reward.Cost;
                        existing.IsManageable = isManageable;
                        updated++;
                    }
                }
                else
                {
                    _config.Redeems.Add(new RedeemConfig
                    {
                        RewardId = reward.Id,
                        RewardTitle = reward.Title,
                        Cost = reward.Cost,
                        IsManageable = isManageable,
                        Enabled = false,
                        Intensity = 50,
                        Duration = 1.0,
                        Mode = SelectionMode.All,
                        CommandType = "Shock"
                    });
                    added++;
                }
            }

            // Remove rewards that no longer exist on Twitch
            var fetchedIds = allRewards.Select(r => r.Id).ToHashSet();
            _config.Redeems.RemoveAll(r => !fetchedIds.Contains(r.RewardId));

            _config.LastFetchedUtc = DateTime.UtcNow;
            SaveConfig();

            var userLabel = string.IsNullOrEmpty(userName) ? "Twitch" : userName;
            return new RedeemFetchResult(true, $"Fetched {allRewards.Count} rewards for {userLabel}", added, updated);
        }
        catch (OperationCanceledException)
        {
            return new RedeemFetchResult(false, "Reward fetch cancelled", 0, 0);
        }
        catch (Exception ex)
        {
            return new RedeemFetchResult(false, $"Reward fetch failed: {ex.Message}", 0, 0);
        }
        finally
        {
            _isFetching = false;
        }
    }

    private async Task<HashSet<string>> FetchManageableRewardIdsAsync(string userId, string token, string baseUrl, CancellationToken cancellationToken)
    {
        var manageableIds = new HashSet<string>();
        try
        {
            // Query with only_manageable_rewards_by_id parameter to get rewards we can manage
            var url = $"{baseUrl}/channel_points/custom_rewards?broadcaster_id={userId}&only_manageable_rewards_by_id=true";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Client-Id", TwitchConstants.ClientId);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadAsStringAsync(cancellationToken);
                var page = JsonSerializer.Deserialize<RewardListResponse>(payload);
                if (page?.Data != null)
                {
                    foreach (var reward in page.Data)
                    {
                        manageableIds.Add(reward.Id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            TwitchIntegration.Logger?.LogWarning(ex, "Failed to fetch manageable rewards");
        }

        return manageableIds;
    }

    public async Task<RewardOperationResult> CreateRewardAsync(string title, int cost, string? prompt = null, string? backgroundColor = null, CancellationToken cancellationToken = default)
    {
        var token = _authService.StoredToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            return new RewardOperationResult(false, "No OAuth token available", null);
        }

        var userId = _eventSubService.CurrentUserId;
        if (string.IsNullOrEmpty(userId))
        {
            return new RewardOperationResult(false, "User ID not available", null);
        }

        try
        {
            var baseUrl = _authService.UseLocalCli ? TwitchConstants.HelixApiBaseUrlLocal : TwitchConstants.HelixApiBaseUrl;
            var url = $"{baseUrl}/channel_points/custom_rewards?broadcaster_id={userId}";

            var requestBody = new
            {
                title,
                cost,
                prompt = prompt ?? "",
                is_enabled = true,
                background_color = backgroundColor
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Client-Id", TwitchConstants.ClientId);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new RewardOperationResult(false, $"Failed to create reward: {response.StatusCode} - {responseContent}", null);
            }

            var result = JsonSerializer.Deserialize<RewardOperationResponse>(responseContent);
            if (result?.Data == null || result.Data.Count == 0)
            {
                return new RewardOperationResult(false, "No reward data returned", null);
            }

            var createdReward = result.Data[0];

            // Add to config
            _config.Redeems.Add(new RedeemConfig
            {
                RewardId = createdReward.Id,
                RewardTitle = createdReward.Title,
                Cost = createdReward.Cost,
                IsManageable = true,
                Enabled = false,
                Intensity = 50,
                Duration = 1.0,
                Mode = SelectionMode.All,
                CommandType = "Shock"
            });

            SaveConfig();

            return new RewardOperationResult(true, "Reward created successfully", createdReward.Id);
        }
        catch (Exception ex)
        {
            TwitchIntegration.Logger?.LogError(ex, "Failed to create reward");
            return new RewardOperationResult(false, $"Exception: {ex.Message}", null);
        }
    }

    public async Task<RewardOperationResult> UpdateRewardAsync(string rewardId, string? title = null, int? cost = null, string? prompt = null, string? backgroundColor = null, bool? isEnabled = null, bool? isPaused = null, CancellationToken cancellationToken = default)
    {
        var token = _authService.StoredToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            return new RewardOperationResult(false, "No OAuth token available", null);
        }

        var userId = _eventSubService.CurrentUserId;
        if (string.IsNullOrEmpty(userId))
        {
            return new RewardOperationResult(false, "User ID not available", null);
        }

        var redeemConfig = _config.Redeems.FirstOrDefault(r => r.RewardId == rewardId);
        if (redeemConfig == null || !redeemConfig.IsManageable)
        {
            return new RewardOperationResult(false, "Reward not found or not manageable", null);
        }

        try
        {
            var baseUrl = _authService.UseLocalCli ? TwitchConstants.HelixApiBaseUrlLocal : TwitchConstants.HelixApiBaseUrl;
            var url = $"{baseUrl}/channel_points/custom_rewards?broadcaster_id={userId}&id={rewardId}";

            var requestBody = new Dictionary<string, object?>();
            if (title != null) requestBody["title"] = title;
            if (cost.HasValue) requestBody["cost"] = cost.Value;
            if (prompt != null) requestBody["prompt"] = prompt;
            if (backgroundColor != null) requestBody["background_color"] = backgroundColor;
            if (isEnabled.HasValue) requestBody["is_enabled"] = isEnabled.Value;
            if (isPaused.HasValue) requestBody["is_paused"] = isPaused.Value;

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Client-Id", TwitchConstants.ClientId);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new RewardOperationResult(false, $"Failed to update reward: {response.StatusCode} - {responseContent}", null);
            }

            var result = JsonSerializer.Deserialize<RewardOperationResponse>(responseContent);
            if (result?.Data != null && result.Data.Count > 0)
            {
                var updatedReward = result.Data[0];
                redeemConfig.RewardTitle = updatedReward.Title;
                redeemConfig.Cost = updatedReward.Cost;
                SaveConfig();
            }

            return new RewardOperationResult(true, "Reward updated successfully", rewardId);
        }
        catch (Exception ex)
        {
            TwitchIntegration.Logger?.LogError(ex, "Failed to update reward");
            return new RewardOperationResult(false, $"Exception: {ex.Message}", null);
        }
    }

    public async Task<RewardOperationResult> DeleteRewardAsync(string rewardId, CancellationToken cancellationToken = default)
    {
        var token = _authService.StoredToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            return new RewardOperationResult(false, "No OAuth token available", null);
        }

        var userId = _eventSubService.CurrentUserId;
        if (string.IsNullOrEmpty(userId))
        {
            return new RewardOperationResult(false, "User ID not available", null);
        }

        var redeemConfig = _config.Redeems.FirstOrDefault(r => r.RewardId == rewardId);
        if (redeemConfig == null || !redeemConfig.IsManageable)
        {
            return new RewardOperationResult(false, "Reward not found or not manageable", null);
        }

        try
        {
            var baseUrl = _authService.UseLocalCli ? TwitchConstants.HelixApiBaseUrlLocal : TwitchConstants.HelixApiBaseUrl;
            var url = $"{baseUrl}/channel_points/custom_rewards?broadcaster_id={userId}&id={rewardId}";

            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Client-Id", TwitchConstants.ClientId);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new RewardOperationResult(false, $"Failed to delete reward: {response.StatusCode} - {responseContent}", null);
            }

            // Remove from config
            _config.Redeems.RemoveAll(r => r.RewardId == rewardId);
            SaveConfig();

            return new RewardOperationResult(true, "Reward deleted successfully", null);
        }
        catch (Exception ex)
        {
            TwitchIntegration.Logger?.LogError(ex, "Failed to delete reward");
            return new RewardOperationResult(false, $"Exception: {ex.Message}", null);
        }
    }

    private void HandleChannelPointRedemption(ChannelPointRedemptionEvent redemption)
    {
        if (!_config.Enabled) return;

        var rewardId = redemption.Reward?.Id;
        if (string.IsNullOrEmpty(rewardId)) return;

        var redeemConfig = _config.Redeems.FirstOrDefault(r => r.RewardId == rewardId);
        if (redeemConfig == null || !redeemConfig.Enabled) return;

        if (!redeemConfig.SelectedShockerIds.Any()) return;

        var intensity = Math.Clamp(redeemConfig.Intensity, 1, 100);
        var duration = Math.Clamp(redeemConfig.Duration, 0.1, 15.0);

        ExecuteAction(redeemConfig, intensity, duration);
    }

    private void ExecuteAction(RedeemConfig redeemConfig, int intensity, double duration)
    {
        var commandType = redeemConfig.CommandType switch
        {
            "Shock" => CommandType.Shock,
            "Vibrate" => CommandType.Vibrate,
            "Beep" => CommandType.Beep,
            _ => CommandType.Vibrate
        };

        var shockerIds = GetShockerIdsForExecution(redeemConfig);
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

    private List<string> GetShockerIdsForExecution(RedeemConfig redeemConfig)
    {
        var availableIds = redeemConfig.SelectedShockerIds
            .Where(IsShockerLoaded)
            .ToList();

        if (!availableIds.Any()) return [];

        return redeemConfig.Mode switch
        {
            SelectionMode.All => availableIds,
            SelectionMode.Random => [availableIds[Random.Shared.Next(availableIds.Count)]],
            SelectionMode.RoundRobin => [GetNextRoundRobinShocker(redeemConfig.Id, availableIds)],
            _ => availableIds
        };
    }

    private string GetNextRoundRobinShocker(string redeemId, List<string> availableIds)
    {
        lock (_roundRobinLock)
        {
            if (!_roundRobinIndices.TryGetValue(redeemId, out var index) || index >= availableIds.Count)
            {
                index = 0;
            }

            var result = availableIds[index];
            _roundRobinIndices[redeemId] = (index + 1) % availableIds.Count;
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
                _config = JsonSerializer.Deserialize<RedeemConfigRoot>(json) ?? CreateDefaultConfig();
            }
            else
            {
                _config = CreateDefaultConfig();
                SaveConfig();
            }
        }
        catch (Exception ex)
        {
            TwitchIntegration.Logger?.LogError(ex, "Failed to load redeem config");
            _config = CreateDefaultConfig();
        }
    }

    private void SaveConfig()
    {
        try
        {
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
            ConfigChanged?.Invoke();
        }
        catch (Exception ex)
        {
            TwitchIntegration.Logger?.LogError(ex, "Failed to save redeem config");
        }
    }

    private static RedeemConfigRoot CreateDefaultConfig()
    {
        return new RedeemConfigRoot
        {
            Enabled = true,
            Redeems = new List<RedeemConfig>()
        };
    }

    private sealed class RewardListResponse
    {
        [JsonPropertyName("data")]
        public List<TwitchReward> Data { get; set; } = new();

        [JsonPropertyName("pagination")]
        public Pagination? Pagination { get; set; }
    }

    private sealed class TwitchReward
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("cost")]
        public int Cost { get; set; }

        [JsonPropertyName("is_enabled")]
        public bool IsEnabled { get; set; } = true;

        [JsonPropertyName("is_paused")]
        public bool IsPaused { get; set; } = false;

        [JsonPropertyName("prompt")]
        public string? Prompt { get; set; }

        [JsonPropertyName("background_color")]
        public string? BackgroundColor { get; set; }
    }

    private sealed class Pagination
    {
        [JsonPropertyName("cursor")]
        public string? Cursor { get; set; }
    }

    private sealed class RewardOperationResponse
    {
        [JsonPropertyName("data")]
        public List<TwitchReward> Data { get; set; } = new();
    }
}

public record RedeemFetchResult(bool Success, string Message, int AddedCount, int UpdatedCount);

public record RewardOperationResult(bool Success, string Message, string? RewardId);
