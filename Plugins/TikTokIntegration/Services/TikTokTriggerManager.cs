using MultiShock.PluginSdk.Flow;

namespace TikTokIntegration.Services;

/// <summary>
/// Routes TikTok events to registered flow trigger node instances, mirroring the
/// TwitchTriggerManager pattern. Config services call the Fire* methods for bracket/rule
/// events; raw events are wired directly from the connection service.
/// </summary>
public class TikTokTriggerManager
{
    private readonly Dictionary<string, List<TriggerRegistration>> _registrations = [];
    private readonly object _lock = new();

    public TikTokTriggerManager(TikTokConnectionService connection)
    {
        connection.OnGift += e => FireGiftEvent("tiktok.gift", e);
        connection.OnChat += HandleChat;
        connection.OnViewerCount += count => FireEvent("tiktok.viewer_count", new Dictionary<string, object?>
        {
            ["viewerCount"] = count,
        });
        connection.OnStreamEnded += () => FireEvent("tiktok.stream_end", new Dictionary<string, object?>());
    }

    public void Register(string eventType, IFlowNodeInstance instance, Func<IFlowNodeInstance, Dictionary<string, object?>, Task> callback)
    {
        lock (_lock)
        {
            if (!_registrations.TryGetValue(eventType, out var list))
            {
                list = [];
                _registrations[eventType] = list;
            }
            list.Add(new TriggerRegistration(instance, callback));
        }
    }

    public void Unregister(string eventType, IFlowNodeInstance instance)
    {
        lock (_lock)
        {
            if (_registrations.TryGetValue(eventType, out var list))
            {
                list.RemoveAll(r => r.Instance.InstanceId == instance.InstanceId);
            }
        }
    }

    private Task FireEvent(string eventType, Dictionary<string, object?> outputs, Func<IFlowNodeInstance, bool>? filter = null)
    {
        List<TriggerRegistration> registrations;
        lock (_lock)
        {
            if (!_registrations.TryGetValue(eventType, out var list))
            {
                return Task.CompletedTask;
            }
            registrations = list.ToList();
        }

        return Task.Run(async () =>
        {
            foreach (var reg in registrations)
            {
                try
                {
                    if (filter == null || filter(reg.Instance))
                    {
                        await reg.Callback(reg.Instance, outputs);
                    }
                }
                catch
                {
                    // Ignore errors in individual triggers
                }
            }
        });
    }

    private void HandleChat(TikTokChatEvent e)
    {
        _ = FireEvent("tiktok.chat", new Dictionary<string, object?>
        {
            ["userName"] = e.UserName,
            ["displayName"] = e.DisplayName,
            ["message"] = e.Message,
        }, instance =>
        {
            var filterText = instance.GetConfig("filterText", "");
            if (string.IsNullOrEmpty(filterText)) return true;
            var caseSensitive = instance.GetConfig("caseSensitive", false);
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return e.Message.Contains(filterText, comparison);
        });
    }

    public void FireGiftEvent(string eventType, TikTokGiftEvent e)
    {
        _ = FireEvent(eventType, new Dictionary<string, object?>
        {
            ["userName"] = e.UserName,
            ["displayName"] = e.DisplayName,
            ["giftId"] = e.GiftId,
            ["giftName"] = e.GiftName,
            ["coinValue"] = e.DiamondCount,
            ["repeatCount"] = e.RepeatCount,
            ["totalCoins"] = e.TotalCoins,
        }, instance =>
        {
            var minCoins = instance.GetConfig("minCoins", 0);
            var giftName = instance.GetConfig("giftName", "");
            var coinMatch = e.TotalCoins >= minCoins;
            var nameMatch = string.IsNullOrEmpty(giftName) ||
                            e.GiftName.Contains(giftName, StringComparison.OrdinalIgnoreCase);
            return coinMatch && nameMatch;
        });
    }

    public Task FireGiftBracketEvent(TikTokGiftEvent e, int bracketCoins, int intensity, double duration)
    {
        return FireEvent("tiktok.gift_bracket", new Dictionary<string, object?>
        {
            ["userName"] = e.UserName,
            ["displayName"] = e.DisplayName,
            ["giftName"] = e.GiftName,
            ["totalCoins"] = e.TotalCoins,
            ["bracketCoins"] = bracketCoins,
            ["intensity"] = intensity,
            ["duration"] = duration,
        }, instance =>
        {
            var minBracketCoins = instance.GetConfig("minBracketCoins", 0);
            return bracketCoins >= minBracketCoins;
        });
    }

    public Task FireGiftRuleEvent(TikTokGiftEvent e, GiftRule rule, double duration)
    {
        return FireEvent("tiktok.gift_rule", new Dictionary<string, object?>
        {
            ["userName"] = e.UserName,
            ["displayName"] = e.DisplayName,
            ["giftName"] = e.GiftName,
            ["giftId"] = e.GiftId,
            ["totalCoins"] = e.TotalCoins,
            ["repeatCount"] = e.RepeatCount,
            ["intensity"] = rule.Intensity,
            ["duration"] = duration,
        }, instance =>
        {
            var giftName = instance.GetConfig("giftName", "");
            return string.IsNullOrEmpty(giftName) ||
                   e.GiftName.Contains(giftName, StringComparison.OrdinalIgnoreCase);
        });
    }

    public Task FireSocialEvent(string eventType, TikTokSocialEvent e)
    {
        return FireEvent(eventType, new Dictionary<string, object?>
        {
            ["userName"] = e.UserName,
            ["displayName"] = e.DisplayName,
        });
    }

    public Task FireSubscribeEvent(TikTokSubscribeEvent e)
    {
        return FireEvent("tiktok.subscribe", new Dictionary<string, object?>
        {
            ["userName"] = e.UserName,
            ["displayName"] = e.DisplayName,
            ["subMonth"] = e.SubMonth,
        });
    }

    public Task FireCoinGoalEvent(int goalAmount, int timesReached)
    {
        return FireEvent("tiktok.coin_goal", new Dictionary<string, object?>
        {
            ["goalAmount"] = goalAmount,
            ["timesReached"] = timesReached,
        });
    }

    public Task FireLikeEvent(TikTokLikeEvent e)
    {
        return FireEvent("tiktok.like", new Dictionary<string, object?>
        {
            ["userName"] = e.UserName,
            ["displayName"] = e.DisplayName,
            ["likeCount"] = e.LikeCount,
            ["totalLikeCount"] = e.TotalLikeCount,
        });
    }

    private record TriggerRegistration(IFlowNodeInstance Instance, Func<IFlowNodeInstance, Dictionary<string, object?>, Task> Callback);
}
