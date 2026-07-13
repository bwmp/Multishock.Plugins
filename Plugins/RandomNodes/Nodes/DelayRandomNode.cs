using MultiShock.PluginSdk.Flow;

namespace RandomNodes.Nodes;

public sealed class DelayRandomNode : IFlowProcessNode
{
    public string TypeId => "randomnodes.delay_random";
    public string DisplayName => "Random Delay";
    public string Category => "Random / Utility";
    public string? Description => "Waits for a random duration before continuing";
    public string Icon => "timer";
    public string? Color => "#64748b";

    public IReadOnlyList<FlowPort> InputPorts { get; } = [FlowPort.FlowIn(), FlowPort.Number("minMs", "Min ms"), FlowPort.Number("maxMs", "Max ms")];
    public IReadOnlyList<FlowPort> OutputPorts { get; } = [FlowPort.FlowOut(), FlowPort.Number("delayMs", "Delay ms")];
    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty> { ["minMs"] = FlowProperty.String("Min ms", "250"), ["maxMs"] = FlowProperty.String("Max ms", "2000") };

    public async Task<FlowNodeResult> ExecuteAsync(IFlowNodeInstance instance, FlowExecutionContext context, CancellationToken cancellationToken)
    {
        var min = context.Inputs.ContainsKey("minMs") ? context.GetInput<double>("minMs") : instance.GetConfig("minMs", 250d);
        var max = context.Inputs.ContainsKey("maxMs") ? context.GetInput<double>("maxMs") : instance.GetConfig("maxMs", 2000d);
        if (max < min) (min, max) = (max, min);
        var delay = (int)Math.Clamp(Random.Shared.NextDouble() * (max - min) + min, 0, 600_000);
        await Task.Delay(delay, cancellationToken);
        return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?> { ["delayMs"] = delay });
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config) => new FlowNodeInstance(instanceId, this, config);
}
