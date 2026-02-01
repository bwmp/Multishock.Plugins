using Emgu.CV;
using ImageDetection.Services;
using MultiShock.PluginSdk.Flow;

namespace ImageDetection.Nodes;

/// <summary>
/// Process node that captures a screenshot.
/// </summary>
public sealed class TakeScreenshotNode : IFlowProcessNode
{
    public string TypeId => "imagedetection.takescreenshot";
    public string DisplayName => "Take Screenshot";
    public string Category => "Image Detection";
    public string? Description => "Captures a screenshot from the configured monitor or window.";
    public string Icon => "camera";
    public string? Color => "#8b5cf6";

    public IReadOnlyList<FlowPort> InputPorts { get; } =
    [
        FlowPort.FlowIn(),
        FlowPort.Number("monitorIndex", "Monitor Index"),
    ];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        FlowPort.FlowOut(),
        FlowPort.Any("screenshot", "Screenshot"),
        FlowPort.Number("width", "Width"),
        FlowPort.Number("height", "Height"),
        FlowPort.Boolean("success", "Success"),
        FlowPort.String("error", "Error"),
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["monitorIndex"] = FlowProperty.Int("Monitor Index", 1),
        ["useGlobalConfig"] = FlowProperty.Bool("Use Global Config", true),
    };

    public Task<FlowNodeResult> ExecuteAsync(
        IFlowNodeInstance instance,
        FlowExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var captureService = context.Services.GetService(typeof(ScreenCaptureService)) as ScreenCaptureService;
            if (captureService == null)
            {
                return Task.FromResult(FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["screenshot"] = null,
                    ["width"] = 0,
                    ["height"] = 0,
                    ["success"] = false,
                    ["error"] = "Screen capture service not available",
                }));
            }

            // Get monitor index from input or property
            var monitorIndex = context.GetInput<int?>("monitorIndex")
                ?? instance.GetConfig("monitorIndex", 1);

            var useGlobalConfig = instance.GetConfig("useGlobalConfig", true);

            Mat screenshot;
            if (useGlobalConfig)
            {
                screenshot = captureService.CaptureScreen();
            }
            else
            {
                screenshot = captureService.CaptureMonitor(monitorIndex);
            }

            return Task.FromResult(FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["screenshot"] = screenshot,
                ["width"] = screenshot.Width,
                ["height"] = screenshot.Height,
                ["success"] = true,
                ["error"] = "",
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["screenshot"] = null,
                ["width"] = 0,
                ["height"] = 0,
                ["success"] = false,
                ["error"] = ex.Message,
            }));
        }
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config)
    {
        return new FlowNodeInstance(instanceId, this, config);
    }
}
