using MultiShock.PluginSdk.Flow;
using TwitchIntegration.Services;
using Microsoft.Extensions.Logging;

namespace TwitchIntegration.Nodes;

public sealed class SetConfigRedeemEnabledNode : IFlowProcessNode
{
    private static List<FlowPropertyOption> _cachedRewardOptions = [];
    private static DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly SemaphoreSlim _cacheLock = new(1, 1);
    
    public string TypeId => "twitch.setconfigredeemenabled";
    public string DisplayName => "Set Config Redeem Enabled";
    public string Category => "Twitch";
    public string? Description => "Enables or disables a channel point reward in the redeem config";
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
            "The channel point reward to control in config (can be overridden by input port)"),
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

            // Get RedeemConfigService from context
            if (context.Services.GetService(typeof(RedeemConfigService)) is not RedeemConfigService redeemService)
            {
                TwitchIntegration.Logger?.LogWarning("SetConfigRedeemEnabled: Redeem config service not available");
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = "Redeem config service not available",
                    ["isEnabled"] = false,
                });
            }

            // Find the redeem in config
            var redeem = redeemService.Config.Redeems.FirstOrDefault(r => r.RewardId == rewardId);
            if (redeem == null)
            {
                TwitchIntegration.Logger?.LogWarning("SetConfigRedeemEnabled: Reward {RewardId} not found in config", rewardId);
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = "Reward not found in config",
                    ["isEnabled"] = false,
                });
            }

            // Determine the new enabled state
            bool isEnabled;
            if (action == "toggle")
            {
                isEnabled = !redeem.Enabled;
            }
            else
            {
                isEnabled = action == "on";
            }

            // Update the reward in config
            redeemService.SetRedeemEnabled(rewardId, isEnabled);
            
            TwitchIntegration.Logger?.LogInformation("SetConfigRedeemEnabled: Successfully updated reward {RewardId} to {State}", rewardId, isEnabled ? "enabled" : "disabled");
            return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["success"] = true,
                ["error"] = "",
                ["isEnabled"] = isEnabled,
            });
        }
        catch (Exception ex)
        {
            TwitchIntegration.Logger?.LogError(ex, "SetConfigRedeemEnabled: Exception during execution");
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

            // Get RedeemConfigService from context
            if (context.Services.GetService(typeof(RedeemConfigService)) is not RedeemConfigService redeemService)
            {
                return;
            }

            // Build options from config redeems
            var newOptions = new List<FlowPropertyOption>();
            foreach (var redeem in redeemService.Config.Redeems)
            {
                if (!string.IsNullOrEmpty(redeem.RewardId) && !string.IsNullOrEmpty(redeem.RewardTitle))
                {
                    newOptions.Add(new FlowPropertyOption
                    {
                        Value = redeem.RewardId,
                        Label = redeem.RewardTitle
                    });
                }
            }

            if (newOptions.Count > 0)
            {
                _cachedRewardOptions = newOptions;
                _cacheExpiry = DateTime.UtcNow.AddMinutes(5);
                TwitchIntegration.Logger?.LogDebug("SetConfigRedeemEnabled: Successfully cached {Count} rewards", newOptions.Count);
            }
        }
        catch (Exception ex)
        {
            TwitchIntegration.Logger?.LogError(ex, "SetConfigRedeemEnabled: Exception during reward cache refresh");
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config)
    {
        return new FlowNodeInstance(instanceId, this, config);
    }
}
