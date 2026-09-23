using MultiShock.PluginSdk.Flow;
using FanslyIntegration.Services;

namespace FanslyIntegration.Nodes;

public abstract class FanslyEventNodeBase : IFlowTriggerNode
{
    public abstract string TypeId { get; }
    public abstract string DisplayName { get; }
    public string Category => "Fansly";
    public abstract string? Description { get; }
    public string Icon => "heart";
    public string? Color => "#1683fb";

    public IReadOnlyList<FlowPort> InputPorts => [];

    public abstract IReadOnlyList<FlowPort> OutputPorts { get; }

    public virtual IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>();

    public event Func<IFlowNodeInstance, Dictionary<string, object?>, Task>? Triggered;

    private readonly Dictionary<IFlowNodeInstance, IServiceProvider> _serviceProviders = [];

    public virtual Task StartAsync(IFlowNodeInstance instance, IServiceProvider services, CancellationToken cancellationToken)
    {
        _serviceProviders[instance] = services;

        if (services.GetService(typeof(FanslyTriggerManager)) is FanslyTriggerManager triggerManager)
        {
            triggerManager.Register(TypeId, instance, FireTriggerAsync);
        }
        return Task.CompletedTask;
    }

    public virtual Task StopAsync(IFlowNodeInstance instance)
    {
        if (_serviceProviders.TryGetValue(instance, out var services))
        {
            var triggerManager = services.GetService(typeof(FanslyTriggerManager)) as FanslyTriggerManager;
            triggerManager?.Unregister(TypeId, instance);
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
