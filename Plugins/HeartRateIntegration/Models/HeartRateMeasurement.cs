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

    public string StatusText(DateTimeOffset now) => State switch
    {
        HeartRateConnectionState.Disconnected => "Connect a monitor to start tracking.",
        HeartRateConnectionState.Connecting => "Connecting to the monitor…",
        HeartRateConnectionState.Reconnecting => "Waiting to reconnect…",
        _ when Latest is null => "Connected. Waiting for the first reading…",
        _ when Latest.SensorContactDetected == false => "Poor sensor contact. Adjust the strap and moisten the electrodes.",
        _ when !HasFreshReading(now) => "No fresh heart-rate reading. Check the strap and connection.",
        _ => $"Updated {Latest.TimestampUtc.ToLocalTime():HH:mm:ss}"
    };
}
