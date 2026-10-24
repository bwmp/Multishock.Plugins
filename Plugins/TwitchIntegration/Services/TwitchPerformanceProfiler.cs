using System.Diagnostics;

namespace TwitchIntegration.Services;

public sealed class TwitchPerformanceProfiler
{
    private static readonly ActivitySource PerformanceSource = new("MultiShock.Performance.TwitchIntegration");
    private const int MaxOperations = 128;
    private readonly object _lock = new();
    private readonly Dictionary<string, Measurement> _measurements = new(StringComparer.Ordinal);
    private volatile bool _enabled;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public bool IsCapturing => _enabled || PerformanceSource.HasListeners();

    public TimingScope Measure(string operation)
    {
        if (!IsCapturing) return default;
        var activity = PerformanceSource.StartActivity(operation, ActivityKind.Internal,
            parentContext: Activity.Current?.Context ?? default,
            tags: [new KeyValuePair<string, object?>("multishock.owner", TwitchIntegrationPlugin.PluginId)]);

        lock (_lock)
        {
            if (!_enabled) return new TimingScope(null, null, 0, activity);
            var measurement = GetMeasurement(operation);
            measurement.Active++;
            return new TimingScope(this, measurement, Stopwatch.GetTimestamp(), activity);
        }
    }

    public void Record(string operation, TimeSpan elapsed)
    {
        if (!_enabled) return;
        lock (_lock)
        {
            if (_enabled) GetMeasurement(operation).Record(elapsed.TotalMilliseconds);
        }
    }

    public IReadOnlyList<PerformanceMeasurement> Snapshot()
    {
        lock (_lock)
        {
            return _measurements.Select(pair => new PerformanceMeasurement(
                    pair.Key, pair.Value.Count, pair.Value.Active,
                    pair.Value.Count == 0 ? 0 : pair.Value.TotalMilliseconds / pair.Value.Count,
                    pair.Value.MaxMilliseconds, pair.Value.SlowCount))
                .OrderByDescending(row => row.MaxMilliseconds)
                .ThenBy(row => row.Operation, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            // Existing scopes keep their measurement object; their results belong to the old capture.
            _measurements.Clear();
        }
    }

    private Measurement GetMeasurement(string operation)
    {
        if (_measurements.TryGetValue(operation, out var measurement)) return measurement;

        if (_measurements.Count >= MaxOperations - 1) operation = "Other operations";
        if (!_measurements.TryGetValue(operation, out measurement))
        {
            measurement = new Measurement();
            _measurements.Add(operation, measurement);
        }
        return measurement;
    }

    private void Complete(Measurement measurement, long started)
    {
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        lock (_lock)
        {
            measurement.Active--;
            measurement.Record(elapsed);
        }
    }

    internal sealed class Measurement
    {
        public long Count;
        public int Active;
        public double TotalMilliseconds;
        public double MaxMilliseconds;
        public long SlowCount;

        public void Record(double milliseconds)
        {
            Count++;
            TotalMilliseconds += milliseconds;
            MaxMilliseconds = Math.Max(MaxMilliseconds, milliseconds);
            if (milliseconds >= 50) SlowCount++;
        }
    }

    public readonly struct TimingScope : IDisposable
    {
        private readonly TwitchPerformanceProfiler? _profiler;
        private readonly Measurement? _measurement;
        private readonly long _started;
        private readonly Activity? _activity;

        internal TimingScope(TwitchPerformanceProfiler? profiler, Measurement? measurement, long started, Activity? activity)
        {
            _profiler = profiler;
            _measurement = measurement;
            _started = started;
            _activity = activity;
        }

        public void Dispose()
        {
            if (_measurement != null) _profiler!.Complete(_measurement, _started);
            _activity?.Dispose();
        }
    }
}

public sealed record PerformanceMeasurement(
    string Operation, long Count, int Active, double AverageMilliseconds, double MaxMilliseconds, long SlowCount);
