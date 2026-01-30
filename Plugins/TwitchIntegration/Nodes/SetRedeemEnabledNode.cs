using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MultiShock.PluginSdk.Flow;
using Microsoft.Extensions.Logging;
using TwitchIntegration.Services;

namespace TwitchIntegration.Nodes;

public sealed class SetRedeemEnabledNode : IFlowProcessNode
{
    private static List<FlowPropertyOption> _cachedRewardOptions = [];
    private static DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly SemaphoreSlim _cacheLock = new(1, 1);
    
    public string TypeId => "twitch.setredeemenabled";
    public string DisplayName => "Set Redeem Enabled";
    public string Category => "Twitch";
    public string? Description => "Enables, disables, or toggles a channel point reward";
    public string Icon => "twitch";
    public string? Color => "#9146FF";

    public IReadOnlyList<FlowPort> InputPorts { get; } =
    [
        FlowPort.FlowIn(),
        FlowPort.String("rewardId", "Reward ID", ""),
    ];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        FlowPort.FlowOut(),
        FlowPort.Boolean("success", "Success"),
        FlowPort.String("error", "Error"),
        FlowPort.Boolean("isEnabled", "Is Enabled"),
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["rewardId"] = FlowProperty.Select("Reward",
            _cachedRewardOptions.Count > 0 ? _cachedRewardOptions : 
            [
                new FlowPropertyOption { Value = "", Label = "Loading rewards..." }
            ],
            "", 
            "The channel point reward to control (can be overridden by input port)"),
        ["action"] = FlowProperty.Select("Action",
        [
            new FlowPropertyOption { Value = "on", Label = "Enable" },
            new FlowPropertyOption { Value = "off", Label = "Disable" },
            new FlowPropertyOption { Value = "toggle", Label = "Toggle" },
        ], "on", "Whether to enable, disable, or toggle the reward"),
    };

    public async Task<FlowNodeResult> ExecuteAsync(
        IFlowNodeInstance instance,
        FlowExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            // Refresh reward cache if needed
            await RefreshRewardCacheIfNeededAsync(context, cancellationToken);
            
            // Get reward ID from input port or property
            var rewardId = context.GetInput<string>("rewardId");
            if (string.IsNullOrEmpty(rewardId))
            {
                rewardId = instance.GetConfig("rewardId", "");
            }

            if (string.IsNullOrEmpty(rewardId))
            {
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = "No reward ID specified",
                    ["isEnabled"] = false,
                });
            }

            // Get action from property
            var action = instance.GetConfig("action", "on");

            // Get Twitch service from context
            if (context.Services.GetService(typeof(TwitchEventSubService)) is not TwitchEventSubService twitchService || !twitchService.IsConnected)
            {
                TwitchIntegration.Logger?.LogWarning("SetRedeemEnabled: Twitch not connected");
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = "Twitch not connected",
                    ["isEnabled"] = false,
                });
            }

            var userId = twitchService.CurrentUserId;
            if (string.IsNullOrEmpty(userId))
            {
                TwitchIntegration.Logger?.LogWarning("SetRedeemEnabled: User ID not available");
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = "User ID not available",
                    ["isEnabled"] = false,
                });
            }

            // Get auth service for token
            if (context.Services.GetService(typeof(TwitchAuthService)) is not TwitchAuthService authService)
            {
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = "Auth service not available",
                    ["isEnabled"] = false,
                });
            }

            var token = authService.StoredToken;
            if (string.IsNullOrEmpty(token))
            {
                TwitchIntegration.Logger?.LogWarning("SetRedeemEnabled: No OAuth token available");
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = "No OAuth token available",
                    ["isEnabled"] = false,
                });
            }

            var baseUrl = authService.UseLocalCli
                ? TwitchConstants.HelixApiBaseUrlLocal
                : TwitchConstants.HelixApiBaseUrl;

            using var httpClient = new HttpClient();

            // Determine the new is_paused value
            bool isPaused;
            if (action == "toggle")
            {
                // First, get the current state of the reward
                var currentState = await GetRewardStateAsync(httpClient, baseUrl, userId, rewardId, token, cancellationToken);
                if (currentState == null)
                {
                    TwitchIntegration.Logger?.LogError("SetRedeemEnabled: Failed to get current reward state for reward ID: {RewardId}", rewardId);
                    return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                    {
                        ["success"] = false,
                        ["error"] = "Failed to get current reward state",
                        ["isEnabled"] = false,
                    });
                }
                isPaused = !currentState.Value; // Toggle: if currently paused (disabled), set to not paused (enabled)
            }
            else
            {
                // is_paused = true means disabled, is_paused = false means enabled
                isPaused = action == "off";
            }

            // Update the reward
            var updateUrl = $"{baseUrl}/channel_points/custom_rewards?broadcaster_id={userId}&id={rewardId}";

            var requestBody = new { is_paused = isPaused };
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Patch, updateUrl)
            {
                Content = content
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Client-Id", TwitchConstants.ClientId);

            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                TwitchIntegration.Logger?.LogError("SetRedeemEnabled: Failed to update reward {RewardId}: {StatusCode} - {Error}", rewardId, response.StatusCode, errorContent);
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = $"Failed to update reward: {response.StatusCode} - {errorContent}",
                    ["isEnabled"] = false,
                });
            }

            var isEnabled = !isPaused;
            TwitchIntegration.Logger?.LogInformation("SetRedeemEnabled: Successfully updated reward {RewardId} to {State}", rewardId, isEnabled ? "enabled" : "disabled");
            return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["success"] = true,
                ["error"] = "",
                ["isEnabled"] = isEnabled,
            });
        }
        catch (Exception ex)
        {
            TwitchIntegration.Logger?.LogError(ex, "SetRedeemEnabled: Exception during execution");
            return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["success"] = false,
                ["error"] = ex.Message,
                ["isEnabled"] = false,
            });
        }
    }

    private static async Task RefreshRewardCacheIfNeededAsync(FlowExecutionContext context, CancellationToken cancellationToken)
    {
        // Check if cache is still valid (refresh every 5 minutes)
        if (_cachedRewardOptions.Count > 0 && DateTime.UtcNow < _cacheExpiry)
        {
            return;
        }

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_cachedRewardOptions.Count > 0 && DateTime.UtcNow < _cacheExpiry)
            {
                return;
            }

            // Get services
            if (context.Services.GetService(typeof(TwitchEventSubService)) is not TwitchEventSubService twitchService || !twitchService.IsConnected)
            {
                return;
            }

            if (context.Services.GetService(typeof(TwitchAuthService)) is not TwitchAuthService authService)
            {
                return;
            }

            var userId = twitchService.CurrentUserId;
            var token = authService.StoredToken;
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token))
            {
                return;
            }

            var baseUrl = authService.UseLocalCli
                ? TwitchConstants.HelixApiBaseUrlLocal
                : TwitchConstants.HelixApiBaseUrl;

            // Fetch only manageable rewards from Twitch API
            using var httpClient = new HttpClient();
            var url = $"{baseUrl}/channel_points/custom_rewards?broadcaster_id={userId}&only_manageable_rewards_by_id=true";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Client-Id", TwitchConstants.ClientId);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                TwitchIntegration.Logger?.LogError("SetRedeemEnabled: Failed to refresh reward cache: {StatusCode}", response.StatusCode);
                return;
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseContent);

            if (doc.RootElement.TryGetProperty("data", out var dataArray))
            {
                var newOptions = new List<FlowPropertyOption>();
                foreach (var reward in dataArray.EnumerateArray())
                {
                    var id = reward.GetProperty("id").GetString();
                    var title = reward.GetProperty("title").GetString();
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(title))
                    {
                        newOptions.Add(new FlowPropertyOption
                        {
                            Value = id,
                            Label = title
                        });
                    }
                }

                if (newOptions.Count > 0)
                {
                    _cachedRewardOptions = newOptions;
                    _cacheExpiry = DateTime.UtcNow.AddMinutes(5);
                    Console.WriteLine($"[SetRedeemEnabledNode] Successfully cached {newOptions.Count} manageable rewards");
                }
                else
                {
                    // No manageable rewards found - show helpful message
                    _cachedRewardOptions = [new FlowPropertyOption { Value = "", Label = "No manageable rewards - create rewards through the plugin" }];
                    _cacheExpiry = DateTime.UtcNow.AddMinutes(5);
                }
            }
        }
        catch (Exception ex)
        {
            TwitchIntegration.Logger?.LogError(ex, "SetRedeemEnabled: Exception during reward cache refresh");
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private static async Task<bool?> GetRewardStateAsync(
        HttpClient httpClient,
        string baseUrl,
        string userId,
        string rewardId,
        string token,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{baseUrl}/channel_points/custom_rewards?broadcaster_id={userId}&id={rewardId}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Client-Id", TwitchConstants.ClientId);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                TwitchIntegration.Logger?.LogError("SetRedeemEnabled: Failed to get reward state for {RewardId}: {StatusCode}", rewardId, response.StatusCode);
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseContent);

            if (doc.RootElement.TryGetProperty("data", out var dataArray) &&
                dataArray.GetArrayLength() > 0)
            {
                var reward = dataArray[0];
                if (reward.TryGetProperty("is_paused", out var isPausedProp))
                {
                    return isPausedProp.GetBoolean();
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config)
    {
        return new FlowNodeInstance(instanceId, this, config);
    }
}
