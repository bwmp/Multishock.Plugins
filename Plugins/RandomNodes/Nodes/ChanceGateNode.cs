using MultiShock.PluginSdk.Flow;

namespace RandomNodes.Nodes;

public sealed class ChanceGateNode : IFlowProcessNode
{
    public string TypeId => "randomnodes.chance_gate";
    public string DisplayName => "Chance Gate";
    public string Category => "Random / Utility";
    public string? Description => "Routes flow based on a random percentage roll";
    public string Icon => "percent";
    public string? Color => "#22c55e";

    public IReadOnlyList<FlowPort> InputPorts { get; } = [FlowPort.FlowIn(), FlowPort.Number("chancePercent", "Chance %")];
    public IReadOnlyList<FlowPort> OutputPorts { get; } = [FlowPort.FlowOut("success", "Success"), FlowPort.FlowOut("failure", "Failure"), FlowPort.Number("roll", "Roll")];
    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty> { ["chancePercent"] = FlowProperty.String("Chance %", "50", "0-100 chance to take the Success output") };

    public Task<FlowNodeResult> ExecuteAsync(IFlowNodeInstance instance, FlowExecutionContext context, CancellationToken cancellationToken)
    {
        var chance = context.Inputs.ContainsKey("chancePercent") ? context.GetInput<double>("chancePercent") : instance.GetConfig("chancePercent", 50d);
        chance = Math.Clamp(chance, 0, 100);
        var roll = Random.Shared.NextDouble() * 100;
        return Task.FromResult(FlowNodeResult.ActivatePort(roll <= chance ? "success" : "failure", new Dictionary<string, object?> { ["roll"] = roll }));
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config) => new FlowNodeInstance(instanceId, this, config);
}
