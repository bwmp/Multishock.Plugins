using HeartRateIntegration.Models;
using MultiShock.PluginSdk.Flow;

namespace HeartRateIntegration.Nodes;

internal static class HeartRateOutputs
{
    public static IReadOnlyList<FlowPort> Ports { get; } =
    [
        FlowPort.Number("bpm", "Heart Rate (BPM)"),
        FlowPort.String("deviceName", "Monitor"),
        FlowPort.String("timestamp", "Timestamp (UTC)"),
        FlowPort.Boolean("contactSupported", "Contact Supported"),
        FlowPort.Boolean("contactDetected", "Contact Detected"),
        FlowPort.Number("rrIntervalMs", "Last RR Interval (ms)"),
        FlowPort.Boolean("hasRrInterval", "Has RR Interval")
    ];

    public static Dictionary<string, object?> From(HeartRateMeasurement reading, HeartRateDevice? device) => new()
    {
        ["bpm"] = reading.BeatsPerMinute,
        ["deviceName"] = device?.Name ?? string.Empty,
        ["timestamp"] = reading.TimestampUtc.ToString("O"),
        ["contactSupported"] = reading.SensorContactDetected.HasValue,
        ["contactDetected"] = reading.SensorContactDetected == true,
        ["rrIntervalMs"] = reading.RrIntervalsMilliseconds.LastOrDefault(),
        ["hasRrInterval"] = reading.RrIntervalsMilliseconds.Count > 0
    };
}
