using MultiShock.PluginSdk.Flow;
using StreamElementsIntegration.Services;

namespace StreamElementsIntegration.Nodes;

/// <summary>Base class for all StreamElements trigger nodes; wires (un)registration to the trigger manager.</summary>
public abstract class StreamElementsEventNodeBase : IFlowTriggerNode
{
    public abstract string TypeId { get; }
    public abstract string DisplayName { get; }
    public string Category => FlowNodeCategory.Trigger;
    public abstract string? Description { get; }
    public virtual string Icon => "zap";
    public virtual string? Color => "#2e97ff"; // StreamElements blue

    public IReadOnlyList<FlowPort> InputPorts => [];
    public abstract IReadOnlyList<FlowPort> OutputPorts { get; }
    public virtual IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>();

    public event Func<IFlowNodeInstance, Dictionary<string, object?>, Task>? Triggered;

    private readonly Dictionary<IFlowNodeInstance, IServiceProvider> _serviceProviders = [];

    public virtual Task StartAsync(IFlowNodeInstance instance, IServiceProvider services, CancellationToken cancellationToken)
    {
        _serviceProviders[instance] = services;
        if (services.GetService(typeof(StreamElementsTriggerManager)) is StreamElementsTriggerManager mgr)
            mgr.Register(TypeId, instance, FireTriggerAsync);
        return Task.CompletedTask;
    }

    public virtual Task StopAsync(IFlowNodeInstance instance)
    {
        if (_serviceProviders.TryGetValue(instance, out var services))
        {
            if (services.GetService(typeof(StreamElementsTriggerManager)) is StreamElementsTriggerManager mgr)
                mgr.Unregister(TypeId, instance);
            _serviceProviders.Remove(instance);
        }
        return Task.CompletedTask;
    }

    protected Task FireTriggerAsync(IFlowNodeInstance instance, Dictionary<string, object?> outputs) =>
        Triggered?.Invoke(instance, outputs) ?? Task.CompletedTask;

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config) =>
        new FlowNodeInstance(instanceId, this, config);

    // Shared output ports reused by the concrete nodes.
    protected static FlowPort TriggeredPort => new() { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow };
    protected static FlowPort NamePort => FlowPort.String("name", "Name");
    protected static FlowPort MessagePort => FlowPort.String("message", "Message");
    protected static FlowPort AmountPort => FlowPort.Number("amount", "Amount");
    protected static FlowPort CurrencyPort => FlowPort.String("currency", "Currency");
    protected static FlowPort ProviderPort => FlowPort.String("provider", "Provider");

    protected static FlowProperty MinAmountProperty(string label) =>
        FlowProperty.Double(label, 0, 0, 1_000_000, description: "Ignore events below this amount.");
}
