using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk.Flow;
using StreamElementsIntegration.Models;

namespace StreamElementsIntegration.Services;

/// <summary>
/// Routes normalized StreamElements events to the flow trigger node instances that registered for them.
/// Node TypeIds are "streamelements.&lt;type&gt;" so they map directly onto <see cref="StreamElementsEvent.Type"/>.
/// Amount-bearing nodes (tip/cheer) honor a per-node "minAmount" config filter.
/// </summary>
public class StreamElementsTriggerManager
{
    private readonly ILogger? _logger;
    private readonly Dictionary<string, List<TriggerRegistration>> _registrations = [];
    private readonly object _lock = new();

    public StreamElementsTriggerManager(StreamElementsConnectionService connection, ILogger<StreamElementsTriggerManager>? logger = null)
    {
        _logger = logger;
        connection.OnEvent += HandleEvent;
    }

    public void Register(string eventTypeId, IFlowNodeInstance instance, Func<IFlowNodeInstance, Dictionary<string, object?>, Task> callback)
    {
        lock (_lock)
        {
            if (!_registrations.TryGetValue(eventTypeId, out var list))
                _registrations[eventTypeId] = list = [];
            list.Add(new TriggerRegistration(instance, callback));
        }
    }

    public void Unregister(string eventTypeId, IFlowNodeInstance instance)
    {
        lock (_lock)
        {
            if (_registrations.TryGetValue(eventTypeId, out var list))
                list.RemoveAll(r => r.Instance.InstanceId == instance.InstanceId);
        }
    }

    private void HandleEvent(StreamElementsEvent e)
    {
        var typeId = "streamelements." + e.Type;
        var outputs = new Dictionary<string, object?>
        {
            ["name"] = e.Name,
            ["message"] = e.Message,
            ["amount"] = e.Amount,
            ["currency"] = e.Currency,
            ["tier"] = e.Tier,
            ["gifted"] = e.Gifted,
            ["sender"] = e.Sender,
            ["item"] = e.Item,
            ["provider"] = e.Provider,
            ["isTest"] = e.IsTest,
        };

        // Only tip/cheer expose a meaningful monetary/bit amount to filter on.
        var amountFiltered = e.Type is StreamElementsEventType.Tip or StreamElementsEventType.Cheer;

        _ = FireEvent(typeId, outputs, amountFiltered
            ? instance => e.Amount >= instance.GetConfig<double>("minAmount", 0)
            : null);
    }

    private async Task FireEvent(string typeId, Dictionary<string, object?> outputs, Func<IFlowNodeInstance, bool>? filter)
    {
        List<TriggerRegistration> registrations;
        lock (_lock)
        {
            if (!_registrations.TryGetValue(typeId, out var list) || list.Count == 0) return;
            registrations = list.ToList();
        }

        foreach (var reg in registrations)
        {
            try
            {
                if (filter == null || filter(reg.Instance))
                    await reg.Callback(reg.Instance, outputs);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error firing StreamElements trigger {TypeId} for {InstanceId}", typeId, reg.Instance.InstanceId);
            }
        }
    }

    private record TriggerRegistration(IFlowNodeInstance Instance, Func<IFlowNodeInstance, Dictionary<string, object?>, Task> Callback);
}
