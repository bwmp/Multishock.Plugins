using MultiShock.PluginSdk.Flow;
using ThroneIntegration.Services;

namespace ThroneIntegration.Nodes;

/// <summary>
/// Base class for Throne event trigger nodes.
/// </summary>
public abstract class ThroneEventNodeBase : IFlowTriggerNode
{

    public abstract string TypeId { get; }
    public abstract string DisplayName { get; }
    public static string Category => FlowNodeCategory.Trigger;
    public abstract string? Description { get; }
    public static string Icon => "gift";
    public static string? Color => "#8b5cf6";

    public IReadOnlyList<FlowPort> InputPorts => [];

    public abstract IReadOnlyList<FlowPort> OutputPorts { get; }

    public virtual IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>();

    public event Func<IFlowNodeInstance, Dictionary<string, object?>, Task>? Triggered;

    private readonly Dictionary<IFlowNodeInstance, IServiceProvider> _serviceProviders = [];

    public virtual Task StartAsync(IFlowNodeInstance instance, IServiceProvider services, CancellationToken cancellationToken)
    {
        _serviceProviders[instance] = services;

        if (services.GetService(typeof(ThroneTriggerManager)) is ThroneTriggerManager triggerManager)
        {
            triggerManager.Register(TypeId, instance, FireTriggerAsync);
        }
        return Task.CompletedTask;
    }

    public virtual Task StopAsync(IFlowNodeInstance instance)
    {
        if (_serviceProviders.TryGetValue(instance, out var services))
        {
            if (services.GetService(typeof(ThroneTriggerManager)) is ThroneTriggerManager triggerManager)
            {
                triggerManager.Unregister(TypeId, instance);
            }
            _serviceProviders.Remove(instance);
        }
        return Task.CompletedTask;
    }

    protected Task FireTriggerAsync(IFlowNodeInstance instance, Dictionary<string, object?> outputs)
    {
        return Triggered?.Invoke(instance, outputs) ?? Task.CompletedTask;
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config)
    {
        return new FlowNodeInstance(instanceId, this, config);
    }
}
