using MultiShock.PluginSdk.Flow;

namespace SteamVROverlay.Nodes;

/// <summary>
/// Shows a notification popup in front of the user while they are in SteamVR.
/// </summary>
public sealed class ShowNotificationNode : IFlowProcessNode
{
    public string TypeId => "steamvroverlay.show_notification";

    public string DisplayName => "Show VR Notification";

    public string Category => FlowNodeCategory.Action;

    public string? Description => "Shows a notification in your SteamVR view";

    public string Icon => "bell";

    public string? Color => "#55AFBC";

    public IReadOnlyList<FlowPort> InputPorts { get; } =
    [
        FlowPort.FlowIn(),
        FlowPort.String("title", "Title"),
        FlowPort.String("message", "Message"),
    ];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        FlowPort.FlowOut(),
        FlowPort.Boolean("success", "Success"),
        FlowPort.String("error", "Error"),
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties { get; } = new Dictionary<string, FlowProperty>
    {
        ["title"] = FlowProperty.String("Title", "MultiShock", "Used when the Title input is not connected"),
        ["message"] = FlowProperty.String("Message", "", "Used when the Message input is not connected", multiline: true),
        ["durationSeconds"] = FlowProperty.Double("Duration (seconds)", 4, 1, 30, 0.5, "How long the notification stays in view"),
        ["buzz"] = FlowProperty.Bool("Vibrate controller", true, "Buzz the controller wearing the wrist panel when the notification appears"),
    };

    public Task<FlowNodeResult> ExecuteAsync(
        IFlowNodeInstance instance,
        FlowExecutionContext context,
        CancellationToken cancellationToken)
    {
        var overlay = OverlayServiceResolver.Resolve(context.Services);
        if (overlay is null)
        {
            return Task.FromResult(Result(false, "SteamVR overlay service not available"));
        }

        var title = context.GetInput<string>("title");
        if (string.IsNullOrWhiteSpace(title))
        {
            title = instance.GetConfig("title", "MultiShock");
        }

        var message = context.GetInput<string>("message");
        if (string.IsNullOrWhiteSpace(message))
        {
            message = instance.GetConfig("message", "");
        }

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(message))
        {
            return Task.FromResult(Result(false, "Nothing to show"));
        }

        var seconds = Math.Clamp(instance.GetConfig("durationSeconds", 4.0), 1.0, 30.0);
        var shown = overlay.ShowNotification(
            title?.Trim() ?? "",
            message?.Trim() ?? "",
            TimeSpan.FromSeconds(seconds),
            instance.GetConfig("buzz", true));

        return Task.FromResult(shown ? Result(true, null) : Result(false, "SteamVR is not connected"));
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config) =>
        new FlowNodeInstance(instanceId, this, config);

    private static FlowNodeResult Result(bool success, string? error) =>
        FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
        {
            ["success"] = success,
            ["error"] = error,
        });
}
