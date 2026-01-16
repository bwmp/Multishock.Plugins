using Emgu.CV;
using MultiShock.PluginSdk.Flow;
using cv2 = Emgu.CV;

namespace ImageDetection.Nodes;

/// <summary>
/// Example flow node for Image Detection plugin.
/// </summary>
public sealed class TakeScreenshot : IFlowProcessNode
{
    public string TypeId => "imagedetection.TakeScreenshot";
    public string DisplayName => "Image Detection Node";
    public string Category => "Image Detection";
    public string? Description => "Example node for Image Detection plugin";
    public string Icon => "zap";
    public string? Color => "#8b5cf6";

    public IReadOnlyList<FlowPort> InputPorts { get; } =
    [
        FlowPort.FlowIn(),
    ];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
                new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.Any(Settings.Output.Image, "Image"),
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties { get; } = new Dictionary<string, FlowProperty>
    {
        [Settings.Properties.Enabled] = FlowProperty.Bool("Enabled", true),
        [Settings.Properties.Interval] = FlowProperty.Int("Interval (ms)", 500),
        [Settings.Properties.FallbackImagePath] = FlowProperty.String("Fallback image path when disabled", string.Empty),
    };

    public Task<FlowNodeResult> ExecuteAsync(
        IFlowNodeInstance instance,
        FlowExecutionContext context,
        CancellationToken cancellationToken)
    {
        var enabled = instance.GetConfig(Settings.Properties.Enabled, true);

        if (!enabled)
        {
            var templateImagePath = instance.GetConfig<string>(Settings.Properties.FallbackImagePath) ?? "";
            if (string.IsNullOrEmpty(templateImagePath))
                throw new ArgumentException($"{this.DisplayName} requires a non empty template image path as input or property");

            if (!File.Exists(templateImagePath))
                throw new ArgumentException($"Template image: {templateImagePath} does not exist");

            using var fallbackImage = CvInvoke.Imread(templateImagePath, cv2.CvEnum.ImreadModes.ColorBgr);

            return Task.FromResult(FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                [Settings.Output.Image] = fallbackImage,
            }));
        }

        /// Take the screenshot

        return Task.FromResult(FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
        {
            [Settings.Output.Image] = null,
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
            public static string Image = "Image";
        }

        public static class Properties
        {
            public static string Interval = "Interval";
            public static string Enabled = "Enabled";
            public static string FallbackImagePath = "FallbackImagePath";
        }
    }
}
