namespace HeartRateIntegration.Models;

public sealed record HeartRateMeasurement(
    int BeatsPerMinute,
    bool? SensorContactDetected,
    int? EnergyExpendedKilojoules,
    IReadOnlyList<double> RrIntervalsMilliseconds,
    DateTimeOffset TimestampUtc);

public sealed record HeartRateDevice(ulong Address, string Name, string ProfileId, short SignalStrength);

public enum HeartRateConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting
}

public sealed record HeartRateSnapshot(
    HeartRateConnectionState State,
    HeartRateDevice? Device,
    HeartRateMeasurement? Latest,
    string? Error)
{
    public bool HasFreshReading(DateTimeOffset now) =>
        State == HeartRateConnectionState.Connected &&
        Latest is { BeatsPerMinute: > 0, SensorContactDetected: not false } &&
        now >= Latest.TimestampUtc && now - Latest.TimestampUtc < TimeSpan.FromSeconds(10);
}
