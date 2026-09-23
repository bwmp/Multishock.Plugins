using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk.Flow;
using StreamlabsIntegration.Models;

namespace StreamlabsIntegration.Services;

/// <summary>
/// Routes normalized Streamlabs events to the flow trigger node instances that registered for them.
/// Node TypeIds are "streamlabs.&lt;type&gt;" so they map directly onto <see cref="StreamlabsEvent.Type"/>.
/// Amount-bearing nodes (donation/bits/superchat) honor a per-node "minAmount" config filter.
/// </summary>
public class StreamlabsTriggerManager
{
    private readonly ILogger? _logger;
    private readonly Dictionary<string, List<TriggerRegistration>> _registrations = [];
    private readonly object _lock = new();

    public StreamlabsTriggerManager(StreamlabsConnectionService connection, ILogger<StreamlabsTriggerManager>? logger = null)
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

    private void HandleEvent(StreamlabsEvent e)
    {
        var typeId = "streamlabs." + e.Type;
        var outputs = new Dictionary<string, object?>
        {
            ["name"] = e.Name,
            ["message"] = e.Message,
            ["amount"] = e.Amount,
            ["formattedAmount"] = e.FormattedAmount,
            ["currency"] = e.Currency,
            ["platform"] = e.Platform,
            ["subPlan"] = e.SubPlan,
        };

        // Only donation/bits/superchat expose a meaningful monetary/bit amount to filter on.
        var amountFiltered = e.Type is StreamlabsEventType.Donation or StreamlabsEventType.Bits or StreamlabsEventType.SuperChat;

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
                _logger?.LogError(ex, "Error firing Streamlabs trigger {TypeId} for {InstanceId}", typeId, reg.Instance.InstanceId);
            }
        }
    }

    private record TriggerRegistration(IFlowNodeInstance Instance, Func<IFlowNodeInstance, Dictionary<string, object?>, Task> Callback);
}
