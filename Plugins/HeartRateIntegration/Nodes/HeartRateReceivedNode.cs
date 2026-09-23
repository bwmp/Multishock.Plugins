using HeartRateIntegration.Models;
using HeartRateIntegration.Services;
using MultiShock.PluginSdk;
using MultiShock.PluginSdk.Flow;

namespace HeartRateIntegration.Nodes;

public sealed class HeartRateReceivedNode : IFlowTriggerNode
{
    private readonly object _sync = new();
    private readonly Dictionary<IFlowNodeInstance, (HeartRateMonitor Monitor, Func<HeartRateMeasurement, Task> Handler)> _subscriptions = [];

    public string TypeId => "com.multishock.heartrateintegration.received";
    public string DisplayName => "Heart Rate Received";
    public string Category => FlowNodeCategory.Trigger;
    public string Description => "Triggers on each fresh heart-rate reading with valid sensor contact. Use comparison nodes to filter BPM.";
    public string Icon => "heart-pulse";
    public string? Color => null;
    public IReadOnlyList<FlowPort> InputPorts => [];
    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        .. HeartRateOutputs.Ports
    ];
    public IReadOnlyDictionary<string, FlowProperty> Properties { get; } = new Dictionary<string, FlowProperty>();
    public event Func<IFlowNodeInstance, Dictionary<string, object?>, Task>? Triggered;

    public Task StartAsync(IFlowNodeInstance instance, IServiceProvider services, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var monitor = services.GetService(typeof(HeartRateMonitor)) as HeartRateMonitor
            ?? PluginServiceLocator.GetRequiredService<HeartRateMonitor>(HeartRatePlugin.PluginId);

        async Task Received(HeartRateMeasurement reading)
        {
            var handlers = Triggered;
            if (handlers is null || cancellationToken.IsCancellationRequested)
                return;
            lock (_sync)
            {
                if (!_subscriptions.TryGetValue(instance, out var subscription) || subscription.Handler != Received)
                    return;
            }
            foreach (Func<IFlowNodeInstance, Dictionary<string, object?>, Task> handler in handlers.GetInvocationList())
            {
                if (cancellationToken.IsCancellationRequested || monitor.CurrentReading != reading)
                    return;
                await handler(instance, HeartRateOutputs.From(reading, monitor.Snapshot.Device)).ConfigureAwait(false);
            }
        }

        lock (_sync)
        {
            RemoveSubscription(instance);
            _subscriptions[instance] = (monitor, Received);
            monitor.MeasurementReceived += Received;
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(IFlowNodeInstance instance)
    {
        lock (_sync)
            RemoveSubscription(instance);
        return Task.CompletedTask;
    }

    private void RemoveSubscription(IFlowNodeInstance instance)
    {
        if (_subscriptions.Remove(instance, out var subscription))
            subscription.Monitor.MeasurementReceived -= subscription.Handler;
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config) => new FlowNodeInstance(instanceId, this, config);
}
