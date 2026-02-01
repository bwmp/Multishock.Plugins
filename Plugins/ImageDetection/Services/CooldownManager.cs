using ImageDetection.Models;
using Microsoft.Extensions.Logging;

namespace ImageDetection.Services;

/// <summary>
/// Manages cooldown state for detected images.
/// Supports standard, continuous, and image-reset cooldown types.
/// </summary>
public class CooldownManager
{
    private readonly ILogger? _logger;
    private readonly Dictionary<string, CooldownState> _states = [];
    private readonly Dictionary<string, HashSet<string>> _resetTriggers = []; // resetImagePath -> [imagesToReset]
    private readonly object _lock = new();

    public CooldownManager(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks if an action can be triggered for the given image.
    /// Returns true if cooldown has expired or not on cooldown.
    /// </summary>
    public bool CanTrigger(string imagePath, CooldownConfig config)
    {
        lock (_lock)
        {
            var state = GetOrCreateState(imagePath);

            switch (config.Type)
            {
                case CooldownType.Standard:
                    return !state.IsOnCooldown || state.HasCooldownExpired(config);

                case CooldownType.Continuous:
                    // For continuous, we check if enough time has passed since last detection
                    if (!state.IsOnCooldown) return true;
                    var elapsed = DateTime.UtcNow - state.LastDetectionTime;
                    return elapsed.TotalSeconds >= config.DurationSeconds;

                case CooldownType.ImageReset:
                    // Can only trigger if not on cooldown (reset happens via another image)
                    return !state.IsOnCooldown || state.HasCooldownExpired(config);

                default:
                    return true;
            }
        }
    }

    /// <summary>
    /// Records that a detection occurred (updates timing for continuous cooldown).
    /// </summary>
    public void RecordDetection(string imagePath, CooldownConfig config)
    {
        lock (_lock)
        {
            var state = GetOrCreateState(imagePath);
            state.MarkDetected();

            // For continuous cooldown, each detection resets the timer
            if (config.Type == CooldownType.Continuous && state.IsOnCooldown)
            {
                state.LastTriggerTime = DateTime.UtcNow;
            }

            CheckForResets(imagePath);
        }
    }

    /// <summary>
    /// Records that an action was triggered, starting the cooldown.
    /// </summary>
    public void RecordTrigger(string imagePath, CooldownConfig config)
    {
        lock (_lock)
        {
            var state = GetOrCreateState(imagePath);
            state.MarkTriggered();

            if (config.Type == CooldownType.ImageReset && !string.IsNullOrEmpty(config.ResetImagePath))
            {
                RegisterResetTrigger(config.ResetImagePath, imagePath);
            }
        }
    }

    /// <summary>
    /// Resets the cooldown for a specific image.
    /// </summary>
    public void ResetCooldown(string imagePath)
    {
        lock (_lock)
        {
            if (_states.TryGetValue(imagePath, out var state))
            {
                state.Reset();
                _logger?.LogDebug("Reset cooldown for: {ImagePath}", imagePath);
            }
        }
    }

    /// <summary>
    /// Resets all cooldowns.
    /// </summary>
    public void ResetAll()
    {
        lock (_lock)
        {
            foreach (var state in _states.Values)
            {
                state.Reset();
            }
            _resetTriggers.Clear();
            _logger?.LogDebug("Reset all cooldowns");
        }
    }

    /// <summary>
    /// Gets cooldown info for an image.
    /// </summary>
    public CooldownInfo GetCooldownInfo(string imagePath, CooldownConfig config)
    {
        lock (_lock)
        {
            var state = GetOrCreateState(imagePath);

            var info = new CooldownInfo
            {
                ImagePath = imagePath,
                IsOnCooldown = state.IsOnCooldown,
                LastTriggerTime = state.LastTriggerTime,
                LastDetectionTime = state.LastDetectionTime
            };

            if (state.IsOnCooldown)
            {
                var elapsed = DateTime.UtcNow - state.LastTriggerTime;
                info.RemainingSeconds = Math.Max(0, config.DurationSeconds - elapsed.TotalSeconds);
                info.CooldownProgress = Math.Min(1.0, elapsed.TotalSeconds / config.DurationSeconds);
            }

            return info;
        }
    }

    /// <summary>
    /// Removes cooldown state for an image (e.g., when image is deleted).
    /// </summary>
    public void RemoveState(string imagePath)
    {
        lock (_lock)
        {
            _states.Remove(imagePath);

            foreach (var triggers in _resetTriggers.Values)
            {
                triggers.Remove(imagePath);
            }

            _resetTriggers.Remove(imagePath);
        }
    }

    private CooldownState GetOrCreateState(string imagePath)
    {
        if (!_states.TryGetValue(imagePath, out var state))
        {
            state = new CooldownState { ImagePath = imagePath };
            _states[imagePath] = state;
        }
        return state;
    }

    private void RegisterResetTrigger(string resetImagePath, string imageToReset)
    {
        if (!_resetTriggers.TryGetValue(resetImagePath, out var imagesToReset))
        {
            imagesToReset = [];
            _resetTriggers[resetImagePath] = imagesToReset;
        }
        imagesToReset.Add(imageToReset);
    }

    private void CheckForResets(string detectedImagePath)
    {
        if (_resetTriggers.TryGetValue(detectedImagePath, out var imagesToReset))
        {
            foreach (var imagePath in imagesToReset)
            {
                if (_states.TryGetValue(imagePath, out var state))
                {
                    state.Reset();
                    _logger?.LogDebug("Reset cooldown for {ImagePath} (triggered by {ResetImage})",
                        imagePath, detectedImagePath);
                }
            }
            imagesToReset.Clear();
        }
    }
}

/// <summary>
/// Information about an image's cooldown state.
/// </summary>
public class CooldownInfo
{
    public string ImagePath { get; set; } = string.Empty;
    public bool IsOnCooldown { get; set; }
    public DateTime LastTriggerTime { get; set; }
    public DateTime LastDetectionTime { get; set; }
    public double RemainingSeconds { get; set; }
    public double CooldownProgress { get; set; }
}
