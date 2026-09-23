using MultiShock.PluginSdk;
using MultiShock.PluginSdk.Flow;
using SteamVROverlay.Overlay;
using SteamVROverlay.Services;

namespace SteamVROverlay.Nodes;

/// <summary>
/// Fires when the user triggers an action from the wrist panel, so flows can react to
/// in-VR input instead of only driving the devices directly.
/// </summary>
public sealed class WristOverlayActionTriggerNode : IFlowTriggerNode
{
    private readonly Dictionary<IFlowNodeInstance, Action<PanelActionRequest>> _subscriptions = [];

    public string TypeId => "steamvroverlay.wrist_action";

    public string DisplayName => "Wrist Overlay Action";

    public string Category => FlowNodeCategory.Trigger;

    public string? Description => "Triggers when an action is used on the SteamVR wrist overlay";

    public string Icon => "glasses";

    public string? Color => "#55AFBC";

    public IReadOnlyList<FlowPort> InputPorts => [];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("command", "Command"),
        FlowPort.Number("intensity", "Intensity"),
        FlowPort.Number("duration", "Duration (seconds)"),
        FlowPort.Number("targetCount", "Target Count"),
        FlowPort.Array("deviceIds", "Device IDs"),
        FlowPort.Array("shockerIds", "Shocker IDs"),
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties { get; } = new Dictionary<string, FlowProperty>
    {
        ["commandFilter"] = FlowProperty.Select("Command",
        [
            new FlowPropertyOption { Value = "any", Label = "Any" },
            new FlowPropertyOption { Value = "vibrate", Label = "Vibrate" },
            new FlowPropertyOption { Value = "shock", Label = "Shock" },
            new FlowPropertyOption { Value = "beep", Label = "Beep" },
            new FlowPropertyOption { Value = "stop", Label = "Stop" },
        ], "any", "Only fire for this overlay action"),
    };

    public event Func<IFlowNodeInstance, Dictionary<string, object?>, Task>? Triggered;

    public Task StartAsync(IFlowNodeInstance instance, IServiceProvider services, CancellationToken cancellationToken)
    {
        var overlay = OverlayServiceResolver.Resolve(services);
        if (overlay is null)
        {
            return Task.CompletedTask;
        }

        void Handler(PanelActionRequest request) => OnAction(instance, request);

        lock (_subscriptions)
        {
            if (_subscriptions.ContainsKey(instance))
            {
                return Task.CompletedTask;
            }

            _subscriptions[instance] = Handler;
        }

        overlay.ActionPerformed += Handler;
        return Task.CompletedTask;
    }

    public Task StopAsync(IFlowNodeInstance instance)
    {
        Action<PanelActionRequest>? handler;
        lock (_subscriptions)
        {
            if (!_subscriptions.Remove(instance, out handler))
            {
                return Task.CompletedTask;
            }
        }

        var overlay = OverlayServiceResolver.Resolve(null);
        if (overlay is not null && handler is not null)
        {
            overlay.ActionPerformed -= handler;
        }

        return Task.CompletedTask;
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config) =>
        new FlowNodeInstance(instanceId, this, config);

    private void OnAction(IFlowNodeInstance instance, PanelActionRequest request)
    {
        var command = request.Command.ToString().ToLowerInvariant();
        var filter = instance.GetConfig("commandFilter", "any");

        if (!string.IsNullOrWhiteSpace(filter) &&
            !string.Equals(filter, "any", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(filter, command, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var outputs = new Dictionary<string, object?>
        {
            ["command"] = command,
            ["intensity"] = request.Intensity,
            ["duration"] = request.DurationSeconds,
            ["targetCount"] = request.Targets.Count,
            ["deviceIds"] = request.Targets.Select(t => t.DeviceId).Distinct().ToArray(),
            ["shockerIds"] = request.Targets.Select(t => t.ShockerId).ToArray(),
        };

        _ = Triggered?.Invoke(instance, outputs);
    }
}

internal static class OverlayServiceResolver
{
    /// <summary>
    /// Resolves the overlay service. The locator is authoritative because a hot-reloaded
    /// plugin builds its own container that the flow engine's provider does not see.
    /// </summary>
    public static WristOverlayService? Resolve(IServiceProvider? services) =>
        PluginServiceLocator.GetService<WristOverlayService>(SteamVROverlayPlugin.PluginId)
        ?? services?.GetService(typeof(WristOverlayService)) as WristOverlayService;
}
