using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk.Flow;
using ThroneIntegration.Models;
using ThroneIntegration.Nodes;

namespace ThroneIntegration.Services;

/// <summary>
/// Tracks running Throne trigger nodes and fires them when a gift arrives.
/// </summary>
public class ThroneTriggerManager
{
    private readonly ILogger<ThroneTriggerManager>? _logger;
    private readonly Dictionary<string, List<TriggerRegistration>> _registrations = [];
    private readonly object _lock = new();

    public ThroneTriggerManager(ILogger<ThroneTriggerManager>? logger = null)
    {
        _logger = logger;
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

    public Task FireGiftAsync(ThroneGift gift)
    {
        var outputs = new Dictionary<string, object?>
        {
            ["gifterUsername"] = gift.GifterUsername,
            ["message"] = gift.Message,
            ["type"] = gift.EventType,
            ["itemName"] = gift.ItemName,
            ["itemImage"] = gift.ItemThumbnailUrl,
            ["amount"] = (double)gift.Amount,
            ["currency"] = gift.Currency,
            ["isSurpriseGift"] = gift.IsSurpriseGift,
            ["timestamp"] = (double)gift.ReceivedAt.ToUnixTimeMilliseconds(),
        };

        return FireAsync(ThroneGiftTriggerNode.TypeIdValue, outputs, instance =>
            (gift.EventType != ThroneGift.GiftCrowdfunded
                || instance.GetConfig<bool>(ThroneGiftTriggerNode.IncludeCrowdfundedProperty, false))
            && (double)gift.Amount >= instance.GetConfig<double>(ThroneGiftTriggerNode.MinAmountProperty, 0));
    }

    private async Task FireAsync(string eventType, Dictionary<string, object?> outputs, Func<IFlowNodeInstance, bool> filter)
    {
        List<TriggerRegistration> registrations;
        lock (_lock)
        {
            if (!_registrations.TryGetValue(eventType, out var list) || list.Count == 0)
            {
                return;
            }
            registrations = list.ToList();
        }

        foreach (var reg in registrations)
        {
            try
            {
                if (filter(reg.Instance))
                {
                    await reg.Callback(reg.Instance, outputs);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Throne trigger failed for node instance {InstanceId}", reg.Instance.InstanceId);
            }
        }
    }

    private record TriggerRegistration(IFlowNodeInstance Instance, Func<IFlowNodeInstance, Dictionary<string, object?>, Task> Callback);
}
