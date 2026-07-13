using MultiShock.PluginSdk.Flow;

namespace RandomNodes.Nodes;

public sealed class FormatTimeNode : IFlowProcessNode
{
    public string TypeId => "randomnodes.format_time";
    public string DisplayName => "Format Time";
    public string Category => "Random / Utility";
    public string? Description => "Formats seconds as mm:ss or a Unix timestamp as a local clock time";
    public string Icon => "clock-3";
    public string? Color => "#14b8a6";

    public IReadOnlyList<FlowPort> InputPorts { get; } =
    [
        FlowPort.FlowIn(),
        FlowPort.Number("value", "Value"),
    ];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        FlowPort.FlowOut(),
        FlowPort.String("formatted", "Formatted"),
        FlowPort.String("timezone", "Timezone"),
        FlowPort.String("error", "Error"),
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["mode"] = FlowProperty.Select("Mode",
        [
            new FlowPropertyOption { Value = "duration", Label = "Seconds to mm:ss" },
            new FlowPropertyOption { Value = "unix", Label = "Unix timestamp to hh:mm AM/PM" },
        ], "duration", "How to format the input value"),
        ["value"] = FlowProperty.String("Value", "0", "Seconds or Unix timestamp. Can be overridden by the input port."),
        ["timezone"] = FlowProperty.Select("Timezone",
        [
            new FlowPropertyOption { Value = "local", Label = "Local time" },
            new FlowPropertyOption { Value = "utc", Label = "UTC" },
            new FlowPropertyOption { Value = "manual", Label = "Manual UTC offset" },
        ], "local", "Timezone used for Unix timestamp formatting"),
        ["utcOffsetHours"] = FlowProperty.String("Manual UTC Offset Hours", "0", "Only used with Manual UTC offset. Example: -5 or 5.5"),
    };

    public Task<FlowNodeResult> ExecuteAsync(IFlowNodeInstance instance, FlowExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            var mode = instance.GetConfig("mode", "duration");
            var value = context.Inputs.ContainsKey("value") ? context.GetInput<double>("value") : instance.GetConfig("value", 0d);

            var timezone = GetTimezoneMode(instance);
            var formatted = mode == "unix"
                ? FormatUnixTimestamp(value, timezone, instance.GetConfig("utcOffsetHours", "0") ?? "0", out timezone)
                : FormatDuration(value);

            return Task.FromResult(FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["formatted"] = formatted,
                ["timezone"] = mode == "unix" ? timezone : string.Empty,
                ["error"] = string.Empty,
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["formatted"] = string.Empty,
                ["timezone"] = string.Empty,
                ["error"] = ex.Message,
            }));
        }
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config) => new FlowNodeInstance(instanceId, this, config);

    private static string FormatDuration(double totalSeconds)
    {
        var seconds = Math.Max(0, (long)Math.Round(totalSeconds));
        var minutes = seconds / 60;
        var remainder = seconds % 60;
        return $"{minutes}:{remainder:00}";
    }

    private static string GetTimezoneMode(IFlowNodeInstance instance)
    {
        var timezone = instance.GetConfig("timezone", string.Empty);
        if (!string.IsNullOrWhiteSpace(timezone))
        {
            return timezone.Trim().ToLowerInvariant();
        }

        // Backwards compatibility with the first version of this node.
        return instance.GetConfig("useUtc", "local") == "utc" ? "utc" : "local";
    }

    private static string FormatUnixTimestamp(double unixSeconds, string timezoneMode, string offsetRaw, out string timezoneName)
    {
        var timestamp = DateTimeOffset.FromUnixTimeSeconds((long)Math.Round(unixSeconds));

        if (timezoneMode == "utc")
        {
            timezoneName = "UTC";
            return timestamp.UtcDateTime.ToString("h:mm tt");
        }

        if (timezoneMode == "manual")
        {
            _ = double.TryParse(offsetRaw, out var offsetHours);
            var offset = TimeSpan.FromHours(Math.Clamp(offsetHours, -14, 14));
            timezoneName = $"UTC{offset.TotalHours:+0.##;-0.##;+0}";
            return timestamp.ToOffset(offset).ToString("h:mm tt");
        }

        var local = timestamp.ToLocalTime();
        timezoneName = TimeZoneInfo.Local.DisplayName;
        return local.ToString("h:mm tt");
    }
}
