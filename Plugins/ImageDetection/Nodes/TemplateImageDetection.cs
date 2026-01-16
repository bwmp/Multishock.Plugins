using Emgu.CV;
using MultiShock.PluginSdk.Flow;
using System;
using cv2 = Emgu.CV;

namespace ImageDetection.Nodes;

/// <summary>
/// Example flow node for Image Detection plugin.
/// </summary>
public sealed class TemplateImageDetection : IFlowProcessNode
{
    public string TypeId => "imagedetection.TemplateImageDetection";
    public string DisplayName => "Template image detection";
    public string Category => "Image Detection";
    public string? Description => "Image detection using a template image.";
    public string Icon => "zap";
    public string? Color => "#8b5cf6";

    public IReadOnlyList<FlowPort> InputPorts { get; } =
    [
        FlowPort.FlowIn(),
        FlowPort.String(Settings.Input.TemplateImagePath, "Template image path", string.Empty),
        FlowPort.Any(Settings.Input.Image, "Input image"),
    ];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
                new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.Boolean(Settings.Output.ObjectFound, "Object found"),
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties { get; } = new Dictionary<string, FlowProperty>
    {
        [Settings.Properties.Enabled] = FlowProperty.Bool("Enabled", true),
        [Settings.Properties.TemplateImagePath] = FlowProperty.String("Template image path", string.Empty),
        [Settings.Properties.Threshold] = FlowProperty.Double("Match threshold", 50),
    };

    public Task<FlowNodeResult> ExecuteAsync(
        IFlowNodeInstance instance,
        FlowExecutionContext context,
        CancellationToken cancellationToken)
    {
        var enabled = instance.GetConfig(Settings.Properties.Enabled, true);

        if (!enabled)
        {
            return Task.FromResult(FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                [Settings.Output.ObjectFound] = true,
            }));
        }

        using var templateImage = this.TryGetTemplateImage(instance, context);
        using var inputImage = this.TryGetInputImage(instance, context);
        using var matchResult = new Mat();

        CvInvoke.MatchTemplate(inputImage, templateImage, matchResult, cv2.CvEnum.TemplateMatchingType.CcoeffNormed);

        double minVal = 0;
        double maxVal = 0;
        System.Drawing.Point minLoc = new System.Drawing.Point();
        System.Drawing.Point maxLoc = new System.Drawing.Point();
        CvInvoke.MinMaxLoc(matchResult, ref minVal, ref maxVal, ref minLoc, ref maxLoc);

        var threshold = instance.GetConfig<double>(Settings.Properties.Threshold);
        if (maxVal < threshold)
        {
            return Task.FromResult(FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                [Settings.Output.ObjectFound] = false,
            }));
        }

        return Task.FromResult(FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
        {
            [Settings.Output.ObjectFound] = true,
        }));
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config)
    {
        return new FlowNodeInstance(instanceId, this, config);
    }

    private Mat TryGetTemplateImage(IFlowNodeInstance instance, FlowExecutionContext context)
    {
        var templateImagePathInput = context.GetInput<string>(Settings.Input.TemplateImagePath) ?? "";
        var templateImagePathProperty = instance.GetConfig<string>(Settings.Properties.TemplateImagePath) ?? "";
        var templateImagePath = string.IsNullOrEmpty(templateImagePathInput) ? templateImagePathProperty : templateImagePathInput;
        if (string.IsNullOrEmpty(templateImagePath))
            throw new ArgumentException($"{this.DisplayName} requires a non empty template image path as input or property");

        if (!File.Exists(templateImagePath))
            throw new ArgumentException($"Template image: {templateImagePath} does not exist");

        return CvInvoke.Imread(templateImagePath, cv2.CvEnum.ImreadModes.ColorBgr);
    }

    private Mat TryGetInputImage(IFlowNodeInstance instance, FlowExecutionContext context)
    {
        var inputImage = context.GetInput<Mat>(Settings.Input.Image) ?? new Mat();

        if (inputImage.IsEmpty)
            throw new ArgumentException($"The input image must be of the type {typeof(Mat)}");
        return inputImage;
    }

    private class Settings
    {
        public static class Input
        {
            public static string TemplateImagePath = "TemplateImagePath";
            public static string Image = "Image";
        }

        public static class Output
        {
            public static string ObjectFound = "ObjectFound";
        }

        public static class Properties
        {
            public static string Enabled = "Enabled";
            public static string TemplateImagePath = "TemplateImagePath";
            public static string Threshold = "Threshold";
        }
    }
}
