using MultiShock.PluginSdk.Flow;

namespace RandomNodes.Nodes;

public sealed class RandomChoiceNode : IFlowProcessNode
{
    public string TypeId => "randomnodes.random_choice";
    public string DisplayName => "Random Choice";
    public string Category => "Random / Utility";
    public string? Description => "Picks one item from a comma- or line-separated list";
    public string Icon => "shuffle";
    public string? Color => "#ec4899";

    public IReadOnlyList<FlowPort> InputPorts { get; } = [FlowPort.FlowIn(), FlowPort.String("choices", "Choices")];
    public IReadOnlyList<FlowPort> OutputPorts { get; } = [FlowPort.FlowOut(), FlowPort.String("choice", "Choice"), FlowPort.Number("index", "Index"), FlowPort.Number("count", "Count")];
    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty> { ["choices"] = FlowProperty.String("Choices", "one, two, three", "Comma- or line-separated choices", multiline: true) };

    public Task<FlowNodeResult> ExecuteAsync(IFlowNodeInstance instance, FlowExecutionContext context, CancellationToken cancellationToken)
    {
        var raw = context.GetInput<string>("choices");
        if (string.IsNullOrWhiteSpace(raw)) raw = instance.GetConfig("choices", "one, two, three") ?? string.Empty;
        var choices = raw.Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var index = choices.Length == 0 ? -1 : Random.Shared.Next(choices.Length);
        return Task.FromResult(FlowNodeResult.ActivatePort("done", new Dictionary<string, object?> { ["choice"] = index >= 0 ? choices[index] : string.Empty, ["index"] = index, ["count"] = choices.Length }));
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config) => new FlowNodeInstance(instanceId, this, config);
}
