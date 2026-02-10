using ImageDetection.Models;
using Microsoft.Extensions.Logging;

namespace ImageDetection.Services;

/// <summary>
/// Stateful analyzer that tracks meter values over time and emits change events
/// when significant changes are detected. Applies smoothing and debouncing.
/// </summary>
public class ValueChangeAnalyzerService
{
    private readonly ILogger? _logger;
    private readonly Dictionary<string, MeterState> _states = new();
    private readonly object _lock = new();

    public ValueChangeAnalyzerService(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Processes a new meter sample and returns a change event if a significant
    /// change was detected, or null if the change was below threshold / in cooldown.
    /// </summary>
    public ValueChangeEvent? Process(string moduleId, DetectionImage target, double currentPercent, DateTime timestampUtc)
    {
        var key = $"{moduleId}/{target.Id}";
        var config = target.Meter;

        lock (_lock)
        {
            if (!_states.TryGetValue(key, out var state))
            {
                state = new MeterState();
                _states[key] = state;
            }

            state.RecentValues.Enqueue(currentPercent);
            while (state.RecentValues.Count > config.SmoothingFrames)
            {
                state.RecentValues.Dequeue();
            }

            var smoothedValue = state.RecentValues.Average();

            if (!state.HasBaseline)
            {
                state.LastStableValue = smoothedValue;
                state.HasBaseline = true;
                return null;
            }

            var delta = smoothedValue - state.LastStableValue;
            var absDelta = Math.Abs(delta);

            if (absDelta < config.MinDeltaPercent)
            {
                return null;
            }

            var isDecrease = delta < 0;

            // If decreases-only mode, ignore increases but only update baseline
            // for significant increases (genuine heals). Small upward jitter is
            // ignored to prevent the baseline from drifting up due to noise.
            if (config.DecreasesOnly && !isDecrease)
            {
                // Only re-baseline on significant increases (likely a real heal)
                if (absDelta >= config.MinDeltaPercent * 2)
                {
                    state.LastStableValue = smoothedValue;
                }
                return null;
            }

            var timeSinceLastEvent = timestampUtc - state.LastEventTime;
            if (timeSinceLastEvent.TotalMilliseconds < config.EventCooldownMs)
            {
                return null;
            }

            var changeType = isDecrease
                ? MeterChangeType.DamageTaken
                : MeterChangeType.Healed;

            var evt = new ValueChangeEvent
            {
                ModuleId = moduleId,
                TargetId = target.Id,
                TargetName = target.Name,
                CurrentPercent = Math.Round(smoothedValue, 1),
                PreviousPercent = Math.Round(state.LastStableValue, 1),
                DeltaPercent = Math.Round(delta, 1),
                IsDecrease = isDecrease,
                ChangeType = changeType,
                Timestamp = timestampUtc
            };

            state.LastStableValue = smoothedValue;
            state.LastEventTime = timestampUtc;

            _logger?.LogDebug("Meter {Key}: {ChangeType} delta={Delta:F1}% ({Previous:F1}% -> {Current:F1}%)",
                key, changeType, delta, evt.PreviousPercent, evt.CurrentPercent);

            return evt;
        }
    }

    /// <summary>
    /// Resets the state for a specific target (e.g., when config changes).
    /// </summary>
    public void ResetTarget(string moduleId, string targetId)
    {
        var key = $"{moduleId}/{targetId}";
        lock (_lock)
        {
            _states.Remove(key);
        }
    }

    /// <summary>
    /// Resets all analyzer state.
    /// </summary>
    public void ResetAll()
    {
        lock (_lock)
        {
            _states.Clear();
        }
    }

    /// <summary>
    /// Gets the current smoothed value for a target, or null if no data.
    /// </summary>
    public double? GetCurrentValue(string moduleId, string targetId)
    {
        var key = $"{moduleId}/{targetId}";
        lock (_lock)
        {
            if (_states.TryGetValue(key, out var state) && state.HasBaseline)
            {
                return state.RecentValues.Count > 0 ? state.RecentValues.Average() : state.LastStableValue;
            }
            return null;
        }
    }

    private class MeterState
    {
        public Queue<double> RecentValues { get; } = new();
        public double LastStableValue { get; set; }
        public bool HasBaseline { get; set; }
        public DateTime LastEventTime { get; set; } = DateTime.MinValue;
    }
}
