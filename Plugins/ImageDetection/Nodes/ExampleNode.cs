using MultiShock.PluginSdk.Flow;

namespace ImageDetection.Nodes;

/// <summary>
/// Example flow node for Image Detection plugin.
/// </summary>
public sealed class ExampleNode : IFlowProcessNode
{
    public string TypeId => "imagedetection.example";
    public string DisplayName => "Image Detection Node";
    public string Category => FlowNodeCategory.Custom;
    public string? Description => "Example node for Image Detection plugin";
    public string Icon => "zap";
    public string? Color => "#8b5cf6";

    public IReadOnlyList<FlowPort> InputPorts { get; } =
    [
        FlowPort.FlowIn(),
        FlowPort.String("Input", "Input", ""),
    ];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
                new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("output", "Output"),
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties { get; } = new Dictionary<string, FlowProperty>
    {
        ["enabled"] = FlowProperty.Bool("Enabled", true),
    };

    public Task<FlowNodeResult> ExecuteAsync(
        IFlowNodeInstance instance,
        FlowExecutionContext context,
        CancellationToken cancellationToken)
    {
        var input = context.GetInput<string>("input") ?? "";
        var enabled = instance.GetConfig("enabled", true);

        var output = enabled ? input : "";

        return Task.FromResult(FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
        {
            ["output"] = output,
        }));
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config)
    {
        return new FlowNodeInstance(instanceId, this, config);
    }

    private class Settings
    {
        public static class Input
        {
        }

        public static class Output
        {
        }

        public static class Properties
        {
        }
    }
}
