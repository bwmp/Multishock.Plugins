using MultiShock.PluginSdk.Flow;

namespace RandomNodes.Nodes;

public sealed class TimeNowNode : IFlowProcessNode
{
    public string TypeId => "randomnodes.time_now";
    public string DisplayName => "Time Now";
    public string Category => "Random / Utility";
    public string? Description => "Outputs the current local time as a simple string plus UTC and Unix time";
    public string Icon => "clock";
    public string? Color => "#8b5cf6";

    public IReadOnlyList<FlowPort> InputPorts { get; } = [FlowPort.FlowIn()];
    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        FlowPort.FlowOut(),
        FlowPort.String("time", "Time"),
        FlowPort.String("local", "Local"),
        FlowPort.String("utc", "UTC"),
        FlowPort.Number("unixSeconds", "Unix Seconds"),
        FlowPort.String("dayOfWeek", "Day Of Week"),
    ];
    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>();

    public Task<FlowNodeResult> ExecuteAsync(IFlowNodeInstance instance, FlowExecutionContext context, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        return Task.FromResult(FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
        {
            ["time"] = now.ToString("h:mm tt"),
            ["local"] = now.ToString("O"),
            ["utc"] = now.UtcDateTime.ToString("O"),
            ["unixSeconds"] = now.ToUnixTimeSeconds(),
            ["dayOfWeek"] = now.DayOfWeek.ToString(),
        }));
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config) => new FlowNodeInstance(instanceId, this, config);
}
