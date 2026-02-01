using Emgu.CV;
using Emgu.CV.CvEnum;
using ImageDetection.Algorithms;
using ImageDetection.Models;
using ImageDetection.Services;
using MultiShock.PluginSdk.Flow;

namespace ImageDetection.Nodes;

/// <summary>
/// Process node that performs image detection on a screenshot.
/// </summary>
public sealed class DetectImageNode : IFlowProcessNode
{
    public string TypeId => "imagedetection.detect";
    public string DisplayName => "Detect Image";
    public string Category => "Image Detection";
    public string? Description => "Detects a template image within a screenshot.";
    public string Icon => "scan";
    public string? Color => "#8b5cf6";

    public IReadOnlyList<FlowPort> InputPorts { get; } =
    [
        FlowPort.FlowIn(),
        FlowPort.Any("screenshot", "Screenshot"),
        FlowPort.String("templatePath", "Template Path"),
    ];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        FlowPort.FlowOut(),
        FlowPort.Boolean("found", "Found"),
        FlowPort.Number("confidence", "Confidence"),
        FlowPort.Number("matchX", "Match X"),
        FlowPort.Number("matchY", "Match Y"),
        FlowPort.String("error", "Error"),
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["templatePath"] = FlowProperty.String("Template Image Path", ""),
        ["threshold"] = FlowProperty.Double("Match Threshold", 0.8, 0.0, 1.0),
        ["algorithm"] = FlowProperty.String("Algorithm", "template-matching"),
    };

    public Task<FlowNodeResult> ExecuteAsync(
        IFlowNodeInstance instance,
        FlowExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get screenshot from input
            var screenshot = context.GetInput<Mat>("screenshot");
            if (screenshot == null || screenshot.IsEmpty)
            {
                return Task.FromResult(CreateErrorResult("No screenshot provided"));
            }

            // Get template path from input or property
            var templatePath = context.GetInput<string>("templatePath");
            if (string.IsNullOrEmpty(templatePath))
            {
                templatePath = instance.GetConfig("templatePath", "");
            }

            if (string.IsNullOrEmpty(templatePath))
            {
                return Task.FromResult(CreateErrorResult("No template image path specified"));
            }

            if (!File.Exists(templatePath))
            {
                return Task.FromResult(CreateErrorResult($"Template image not found: {templatePath}"));
            }

            // Load template image
            using var template = CvInvoke.Imread(templatePath, ImreadModes.Unchanged);
            if (template.IsEmpty)
            {
                return Task.FromResult(CreateErrorResult("Failed to load template image"));
            }

            // Get algorithm
            var algorithmRegistry = context.Services.GetService(typeof(AlgorithmRegistry)) as AlgorithmRegistry;
            var algorithmId = instance.GetConfig("algorithm", "template-matching");
            var algorithm = algorithmRegistry?.Get(algorithmId ?? "template-matching") ?? new TemplateMatchingAlgorithm();

            // Get threshold
            var threshold = instance.GetConfig("threshold", 0.8);

            // Perform detection
            var result = algorithm.Detect(screenshot, template, threshold, cancellationToken);

            return Task.FromResult(FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["found"] = result.Found,
                ["confidence"] = result.Confidence,
                ["matchX"] = result.MatchLocation?.X ?? 0,
                ["matchY"] = result.MatchLocation?.Y ?? 0,
                ["error"] = result.Error ?? "",
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(CreateErrorResult(ex.Message));
        }
    }

    private static FlowNodeResult CreateErrorResult(string error)
    {
        return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
        {
            ["found"] = false,
            ["confidence"] = 0.0,
            ["matchX"] = 0,
            ["matchY"] = 0,
            ["error"] = error,
        });
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config)
    {
        return new FlowNodeInstance(instanceId, this, config);
    }
}
