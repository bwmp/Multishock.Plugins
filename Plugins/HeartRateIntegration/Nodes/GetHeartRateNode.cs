using HeartRateIntegration.Services;
using MultiShock.PluginSdk;
using MultiShock.PluginSdk.Flow;

namespace HeartRateIntegration.Nodes;

public sealed class GetHeartRateNode : IFlowProcessNode
{
    public string TypeId => "com.multishock.heartrateintegration.current";
    public string DisplayName => "Get Heart Rate";
    public string Category => "Heart Rate";
    public string Description => "Reads the latest heart rate. Unavailable fires when disconnected, stale, or without sensor contact.";
    public string Icon => "heart-pulse";
    public string? Color => null;
    public IReadOnlyList<FlowPort> InputPorts { get; } = [FlowPort.FlowIn()];
    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "done", Name = "Available", Type = FlowPortType.Flow },
        new FlowPort { Id = "unavailable", Name = "Unavailable", Type = FlowPortType.Flow },
        .. HeartRateOutputs.Ports
    ];
    public IReadOnlyDictionary<string, FlowProperty> Properties { get; } = new Dictionary<string, FlowProperty>();

    public Task<FlowNodeResult> ExecuteAsync(IFlowNodeInstance instance, FlowExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var monitor = context.Services.GetService(typeof(HeartRateMonitor)) as HeartRateMonitor
            ?? PluginServiceLocator.GetService<HeartRateMonitor>(HeartRatePlugin.PluginId);
        var reading = monitor?.CurrentReading;
        return Task.FromResult(reading is null
            ? FlowNodeResult.ActivatePort("unavailable", new Dictionary<string, object?>
            {
                ["bpm"] = null,
                ["deviceName"] = monitor?.Snapshot.Device?.Name ?? string.Empty,
                ["timestamp"] = null,
                ["contactSupported"] = false,
                ["contactDetected"] = false,
                ["rrIntervalMs"] = null,
                ["hasRrInterval"] = false
            })
            : FlowNodeResult.ActivatePort("done", HeartRateOutputs.From(reading, monitor?.Snapshot.Device)));
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config) => new FlowNodeInstance(instanceId, this, config);
}
