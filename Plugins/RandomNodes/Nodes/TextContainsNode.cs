using MultiShock.PluginSdk.Flow;

namespace RandomNodes.Nodes;

public sealed class TextContainsNode : IFlowProcessNode
{
    public string TypeId => "randomnodes.text_contains";
    public string DisplayName => "Text Contains";
    public string Category => "Random / Utility";
    public string? Description => "Checks whether text contains a value";
    public string Icon => "search";
    public string? Color => "#0ea5e9";

    public IReadOnlyList<FlowPort> InputPorts { get; } = [FlowPort.FlowIn(), FlowPort.String("text", "Text"), FlowPort.String("contains", "Contains")];
    public IReadOnlyList<FlowPort> OutputPorts { get; } = [FlowPort.FlowOut("true", "True"), FlowPort.FlowOut("false", "False"), FlowPort.Boolean("matches", "Matches")];
    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["contains"] = FlowProperty.String("Contains", "", "Text to search for"),
    };

    public Task<FlowNodeResult> ExecuteAsync(IFlowNodeInstance instance, FlowExecutionContext context, CancellationToken cancellationToken)
    {
        var text = context.GetInput<string>("text") ?? string.Empty;
        var contains = context.GetInput<string>("contains");
        if (string.IsNullOrEmpty(contains)) contains = instance.GetConfig("contains", "");
        var matches = !string.IsNullOrEmpty(contains) && text.Contains(contains, StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(FlowNodeResult.ActivatePort(matches ? "true" : "false", new Dictionary<string, object?> { ["matches"] = matches }));
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config) => new FlowNodeInstance(instanceId, this, config);
}
