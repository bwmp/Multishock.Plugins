using MultiShock.PluginSdk.Flow;
using JoystickIntegration.Services;

namespace JoystickIntegration.Nodes;

public abstract class JoystickEventNodeBase : IFlowTriggerNode
{
    public abstract string TypeId { get; }
    public abstract string DisplayName { get; }
    public string Category => "Joystick.tv";
    public abstract string? Description { get; }
    public string Icon => "gamepad";
    public string? Color => "#7b5cff";

    public IReadOnlyList<FlowPort> InputPorts => [];

    public abstract IReadOnlyList<FlowPort> OutputPorts { get; }

    public virtual IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>();

    public event Func<IFlowNodeInstance, Dictionary<string, object?>, Task>? Triggered;

    private readonly Dictionary<IFlowNodeInstance, IServiceProvider> _serviceProviders = [];

    public virtual Task StartAsync(IFlowNodeInstance instance, IServiceProvider services, CancellationToken cancellationToken)
    {
        _serviceProviders[instance] = services;

        if (services.GetService(typeof(JoystickTriggerManager)) is JoystickTriggerManager triggerManager)
        {
            triggerManager.Register(TypeId, instance, FireTriggerAsync);
        }
        return Task.CompletedTask;
    }

    public virtual Task StopAsync(IFlowNodeInstance instance)
    {
        if (_serviceProviders.TryGetValue(instance, out var services))
        {
            var triggerManager = services.GetService(typeof(JoystickTriggerManager)) as JoystickTriggerManager;
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
