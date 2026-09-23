using MultiShock.PluginSdk.Flow;
using SteamVROverlay.Services;

namespace SteamVROverlay.Nodes;

/// <summary>
/// Overrides the wrist overlay's automatic show/hide behaviour from a flow.
/// </summary>
public sealed class SetOverlayVisibilityNode : IFlowProcessNode
{
    public string TypeId => "steamvroverlay.set_visibility";

    public string DisplayName => "Set Wrist Overlay Visibility";

    public string Category => FlowNodeCategory.Action;

    public string? Description => "Shows, hides, or restores automatic visibility of the SteamVR wrist overlay";

    public string Icon => "eye";

    public string? Color => "#55AFBC";

    public IReadOnlyList<FlowPort> InputPorts { get; } =
    [
        FlowPort.FlowIn(),
    ];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        FlowPort.FlowOut(),
        FlowPort.Boolean("success", "Success"),
        FlowPort.String("error", "Error"),
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties { get; } = new Dictionary<string, FlowProperty>
    {
        ["mode"] = FlowProperty.Select("Mode",
        [
            new FlowPropertyOption { Value = "auto", Label = "Automatic (wrist gaze)" },
            new FlowPropertyOption { Value = "show", Label = "Always shown" },
            new FlowPropertyOption { Value = "hide", Label = "Hidden" },
        ], "auto", "How the overlay decides when to be visible"),
    };

    public Task<FlowNodeResult> ExecuteAsync(
        IFlowNodeInstance instance,
        FlowExecutionContext context,
        CancellationToken cancellationToken)
    {
        var overlay = OverlayServiceResolver.Resolve(context.Services);
        if (overlay is null)
        {
            return Task.FromResult(FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["success"] = false,
                ["error"] = "SteamVR overlay service not available",
            }));
        }

        overlay.VisibilityMode = instance.GetConfig("mode", "auto") switch
        {
            "show" => OverlayVisibilityMode.ForceShown,
            "hide" => OverlayVisibilityMode.ForceHidden,
            _ => OverlayVisibilityMode.Auto
        };

        return Task.FromResult(FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
        {
            ["success"] = true,
            ["error"] = null,
        }));
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config) =>
        new FlowNodeInstance(instanceId, this, config);
}
