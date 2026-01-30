using MultiShock.PluginSdk.Flow;
using TwitchIntegration.Models;

namespace TwitchIntegration.Services;

public class TwitchTriggerManager
{
    private readonly TwitchEventSubService _eventSubService;
    private readonly Dictionary<string, List<TriggerRegistration>> _registrations = [];
    private readonly object _lock = new();

    public TwitchTriggerManager(TwitchEventSubService eventSubService)
    {
        _eventSubService = eventSubService;

        // Subscribe to all events
        _eventSubService.OnCheer += HandleCheer;
        _eventSubService.OnSubscribe += HandleSubscribe;
        _eventSubService.OnSubscriptionGift += HandleSubscriptionGift;
        _eventSubService.OnSubscriptionMessage += HandleSubscriptionMessage;
        _eventSubService.OnFollow += HandleFollow;
        _eventSubService.OnHypeTrainBegin += HandleHypeTrainBegin;
        _eventSubService.OnHypeTrainProgress += HandleHypeTrainProgress;
        _eventSubService.OnHypeTrainEnd += HandleHypeTrainEnd;
        _eventSubService.OnRaid += HandleRaid;
        _eventSubService.OnChannelPointRedemption += HandleChannelPointRedemption;
        _eventSubService.OnChatMessage += HandleChatMessage;
    }

    public void Register(string eventType, IFlowNodeInstance instance, Func<IFlowNodeInstance, Dictionary<string, object?>, Task> callback)
    {
        lock (_lock)
        {
            if (!_registrations.ContainsKey(eventType))
            {
                _registrations[eventType] = [];
            }
            _registrations[eventType].Add(new TriggerRegistration(instance, callback));
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

    private async Task FireEvent(string eventType, Dictionary<string, object?> outputs, Func<IFlowNodeInstance, bool>? filter = null)
    {
        List<TriggerRegistration> registrations;
        lock (_lock)
        {
            if (!_registrations.TryGetValue(eventType, out var list))
            {
                return;
            }
            registrations = list.ToList();
        }

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
    }

    private void HandleCheer(CheerEvent e)
    {
        _ = FireEvent("twitch.cheer", new Dictionary<string, object?>
        {
            ["userName"] = e.IsAnonymous ? "Anonymous" : e.UserName,
            ["bits"] = e.Bits,
            ["message"] = e.Message,
            ["isAnonymous"] = e.IsAnonymous,
        }, instance =>
        {
            var minBits = instance.GetConfig("minBits", 0);
            return e.Bits >= minBits;
        });
    }

    private void HandleSubscribe(SubscribeEvent e)
    {
        _ = FireEvent("twitch.subscribe", new Dictionary<string, object?>
        {
            ["userName"] = e.UserName,
            ["tier"] = e.TierNumber,
            ["isGift"] = e.IsGift,
        }, instance =>
        {
            var tierFilter = instance.GetConfig("tierFilter", "any");
            if (tierFilter == "any") return true;
            return int.TryParse(tierFilter, out var tier) && e.TierNumber == tier;
        });
    }

    private void HandleSubscriptionGift(SubscriptionGiftEvent e)
    {
        _ = FireEvent("twitch.subscription_gift", new Dictionary<string, object?>
        {
            ["userName"] = e.IsAnonymous ? "Anonymous" : e.UserName,
            ["count"] = e.Total,
            ["tier"] = e.TierNumber,
            ["totalGifted"] = e.CumulativeTotal ?? e.Total,
            ["isAnonymous"] = e.IsAnonymous,
        }, instance =>
        {
            var minGifts = instance.GetConfig("minGifts", 1);
            return e.Total >= minGifts;
        });
    }

    private void HandleSubscriptionMessage(SubscriptionMessageEvent e)
    {
        _ = FireEvent("twitch.subscription_message", new Dictionary<string, object?>
        {
            ["userName"] = e.UserName,
            ["message"] = e.Message.Text,
            ["tier"] = e.TierNumber,
            ["months"] = e.CumulativeMonths,
            ["streak"] = e.StreakMonths ?? 0,
        });
    }

    private void HandleFollow(FollowEvent e)
    {
        _ = FireEvent("twitch.follow", new Dictionary<string, object?>
        {
            ["userName"] = e.UserName,
            ["userId"] = e.UserId,
        });
    }

    private void HandleHypeTrainBegin(HypeTrainBeginEvent e)
    {
        _ = FireEvent("twitch.hype_train_begin", new Dictionary<string, object?>
        {
            ["level"] = e.Level,
            ["total"] = e.Total,
            ["goal"] = e.Goal,
        });
    }

    private int _lastHypeTrainLevel = 0;

    private void HandleHypeTrainProgress(HypeTrainProgressEvent e)
    {
        var isLevelUp = e.Level > _lastHypeTrainLevel;
        _lastHypeTrainLevel = e.Level;

        _ = FireEvent("twitch.hype_train_progress", new Dictionary<string, object?>
        {
            ["level"] = e.Level,
            ["progress"] = e.Progress,
            ["goal"] = e.Goal,
            ["total"] = e.Total,
        }, instance =>
        {
            var onlyLevelUp = instance.GetConfig("onLevelUp", false);
            return !onlyLevelUp || isLevelUp;
        });
    }

    private void HandleHypeTrainEnd(HypeTrainEndEvent e)
    {
        _lastHypeTrainLevel = 0;

        _ = FireEvent("twitch.hype_train_end", new Dictionary<string, object?>
        {
            ["level"] = e.Level,
            ["total"] = e.Total,
        });
    }

    private void HandleRaid(RaidEvent e)
    {
        _ = FireEvent("twitch.raid", new Dictionary<string, object?>
        {
            ["raiderName"] = e.FromBroadcasterUserName,
            ["viewers"] = e.Viewers,
        }, instance =>
        {
            var minViewers = instance.GetConfig("minViewers", 0);
            return e.Viewers >= minViewers;
        });
    }

    private void HandleChannelPointRedemption(ChannelPointRedemptionEvent e)
    {
        _ = FireEvent("twitch.channel_point_redemption", new Dictionary<string, object?>
        {
            ["userName"] = e.UserName,
            ["rewardTitle"] = e.Reward.Title,
            ["rewardId"] = e.Reward.Id,
            ["cost"] = e.Reward.Cost,
            ["userInput"] = e.UserInput,
        }, instance =>
        {
            var rewardFilter = instance.GetConfig("rewardFilter", "");
            if (string.IsNullOrEmpty(rewardFilter)) return true;
            return e.Reward.Title.Contains(rewardFilter, StringComparison.OrdinalIgnoreCase);
        });
    }

    private void HandleChatMessage(ChatMessageEvent e)
    {
        _ = FireEvent("twitch.chatmessage", new Dictionary<string, object?>
        {
            ["userName"] = e.UserLogin,
            ["userId"] = e.UserId,
            ["message"] = e.Message.Text,
            ["displayName"] = e.UserName,
            ["isModerator"] = e.IsModerator,
            ["isSubscriber"] = e.IsSubscriber,
            ["isVip"] = e.IsVip,
        }, instance =>
        {
            var filterText = instance.GetConfig("filterText", "");
            if (string.IsNullOrEmpty(filterText)) return true;
            
            var caseSensitive = instance.GetConfig("caseSensitive", false);
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return e.Message.Text.Contains(filterText, comparison);
        });
    }

    public Task FireCheerBracketEvent(string userName, int bits, string message, bool isAnonymous, string sectionName, string sectionKeyword, int bracketBits, int intensity, double duration)
    {
        return FireEvent("twitch.cheer_bracket", new Dictionary<string, object?>
        {
            ["userName"] = userName,
            ["bits"] = bits,
            ["message"] = message,
            ["isAnonymous"] = isAnonymous,
            ["sectionName"] = sectionName,
            ["bracketBits"] = bracketBits,
            ["intensity"] = intensity,
            ["duration"] = duration,
        }, instance =>
        {
            var filterKeyword = instance.GetConfig("sectionKeyword", "");
            var minBracketBits = instance.GetConfig("minBracketBits", 0);

            var keywordMatch = string.IsNullOrEmpty(filterKeyword) || 
                               sectionKeyword.Equals(filterKeyword, StringComparison.OrdinalIgnoreCase);
            var bracketMatch = bracketBits >= minBracketBits;

            return keywordMatch && bracketMatch;
        });
    }

    public Task FireSubscriptionBracketEvent(string userName, int giftCount, int tier, int totalGifted, bool isAnonymous, string tierName, int bracketCount, int intensity, double duration)
    {
        return FireEvent("twitch.subscription_bracket", new Dictionary<string, object?>
        {
            ["userName"] = userName,
            ["giftCount"] = giftCount,
            ["tier"] = tier,
            ["totalGifted"] = totalGifted,
            ["isAnonymous"] = isAnonymous,
            ["tierName"] = tierName,
            ["bracketCount"] = bracketCount,
            ["intensity"] = intensity,
            ["duration"] = duration,
        }, instance =>
        {
            var tierFilter = instance.GetConfig("tierFilter", "any");
            var minBracketCount = instance.GetConfig("minBracketCount", 0);

            var tierMatch = tierFilter == "any" || 
                            (int.TryParse(tierFilter, out var filterTier) && tier == filterTier);
            var bracketMatch = bracketCount >= minBracketCount;

            return tierMatch && bracketMatch;
        });
    }

    private record TriggerRegistration(IFlowNodeInstance Instance, Func<IFlowNodeInstance, Dictionary<string, object?>, Task> Callback);
}
