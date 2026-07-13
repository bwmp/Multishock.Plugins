using MultiShock.PluginSdk.Flow;

namespace RandomNodes.Nodes;

public sealed class RandomNumberNode : IFlowProcessNode
{
    public string TypeId => "randomnodes.random_number";
    public string DisplayName => "Random Number";
    public string Category => "Random / Utility";
    public string? Description => "Generates a random number between min and max";
    public string Icon => "dice-5";
    public string? Color => "#f59e0b";

    public IReadOnlyList<FlowPort> InputPorts { get; } = [FlowPort.FlowIn(), FlowPort.Number("min", "Min"), FlowPort.Number("max", "Max")];
    public IReadOnlyList<FlowPort> OutputPorts { get; } = [FlowPort.FlowOut(), FlowPort.Number("value", "Value"), FlowPort.Number("integer", "Integer")];
    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["min"] = FlowProperty.String("Min", "0", "Minimum value"),
        ["max"] = FlowProperty.String("Max", "100", "Maximum value"),
    };

    public Task<FlowNodeResult> ExecuteAsync(IFlowNodeInstance instance, FlowExecutionContext context, CancellationToken cancellationToken)
    {
        var min = context.Inputs.ContainsKey("min") ? context.GetInput<double>("min") : instance.GetConfig("min", 0d);
        var max = context.Inputs.ContainsKey("max") ? context.GetInput<double>("max") : instance.GetConfig("max", 100d);
        if (max < min) (min, max) = (max, min);

        var value = Random.Shared.NextDouble() * (max - min) + min;
        return Task.FromResult(FlowNodeResult.ActivatePort("done", new Dictionary<string, object?> { ["value"] = value, ["integer"] = (int)Math.Round(value) }));
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config) => new FlowNodeInstance(instanceId, this, config);
}
