using MultiShock.PluginSdk.Flow;
using OBSIntegration.Services;

namespace OBSIntegration.Nodes;

/// <summary>
/// Base class for OBS event trigger nodes.
/// </summary>
public abstract class ObsEventNodeBase : IFlowTriggerNode
{
    public abstract string TypeId { get; }
    public abstract string DisplayName { get; }
    public string Category => FlowNodeCategory.Trigger;
    public abstract string? Description { get; }
    public string Icon => "monitor";
    public string? Color => "#302E70";

    public IReadOnlyList<FlowPort> InputPorts => [];

    public abstract IReadOnlyList<FlowPort> OutputPorts { get; }

    public virtual IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>();

    public event Func<IFlowNodeInstance, Dictionary<string, object?>, Task>? Triggered;

    private readonly Dictionary<IFlowNodeInstance, IServiceProvider> _serviceProviders = [];

    public virtual Task StartAsync(IFlowNodeInstance instance, IServiceProvider services, CancellationToken cancellationToken)
    {
        // Store service provider for later use
        _serviceProviders[instance] = services;

        // Get the trigger manager from DI
        if (services.GetService(typeof(ObsTriggerManager)) is ObsTriggerManager triggerManager)
        {
            triggerManager.Register(TypeId, instance, FireTriggerAsync);
        }
        return Task.CompletedTask;
    }

    public virtual Task StopAsync(IFlowNodeInstance instance)
    {
        if (_serviceProviders.TryGetValue(instance, out var services))
        {
            var triggerManager = services.GetService(typeof(ObsTriggerManager)) as ObsTriggerManager;
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
