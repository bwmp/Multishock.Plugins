namespace ImageDetection.Models;

/// <summary>
/// Configuration for detection cooldowns.
/// </summary>
public class CooldownConfig
{
    /// <summary>
    /// Type of cooldown behavior.
    /// </summary>
    public CooldownType Type { get; set; } = CooldownType.Standard;

    /// <summary>
    /// Cooldown duration in seconds.
    /// </summary>
    public double DurationSeconds { get; set; } = 5.0;

    /// <summary>
    /// For ImageReset type: the image that resets this cooldown when detected.
    /// Format: "moduleName/imageName.png"
    /// </summary>
    public string? ResetImagePath { get; set; }
}

/// <summary>
/// Type of cooldown behavior.
/// </summary>
public enum CooldownType
{
    /// <summary>
    /// Standard cooldown - action triggers, then waits for duration before allowing next trigger.
    /// </summary>
    Standard = 0,

    /// <summary>
    /// Continuous cooldown - timer resets on each detection, action only triggers after no detection for duration.
    /// </summary>
    Continuous = 1,

    /// <summary>
    /// Image reset - cooldown is reset when a specific other image is detected.
    /// </summary>
    ImageReset = 2
}

/// <summary>
/// Tracks the cooldown state for a specific image.
/// </summary>
public class CooldownState
{
    /// <summary>
    /// Path/identifier of the image this state tracks.
    /// </summary>
    public string ImagePath { get; set; } = string.Empty;

    /// <summary>
    /// Time of the last action trigger (UTC).
    /// </summary>
    public DateTime LastTriggerTime { get; set; } = DateTime.MinValue;

    /// <summary>
    /// Time of the last detection (UTC), used for continuous cooldown.
    /// </summary>
    public DateTime LastDetectionTime { get; set; } = DateTime.MinValue;

    /// <summary>
    /// Whether the cooldown is currently active (action blocked).
    /// </summary>
    public bool IsOnCooldown { get; set; }

    /// <summary>
    /// Checks if cooldown has expired based on config.
    /// </summary>
    public bool HasCooldownExpired(CooldownConfig config)
    {
        if (!IsOnCooldown) return true;

        var elapsed = DateTime.UtcNow - LastTriggerTime;
        return elapsed.TotalSeconds >= config.DurationSeconds;
    }

    /// <summary>
    /// Resets the cooldown state.
    /// </summary>
    public void Reset()
    {
        IsOnCooldown = false;
        LastTriggerTime = DateTime.MinValue;
        LastDetectionTime = DateTime.MinValue;
    }

    /// <summary>
    /// Marks that an action was triggered.
    /// </summary>
    public void MarkTriggered()
    {
        LastTriggerTime = DateTime.UtcNow;
        IsOnCooldown = true;
    }

    /// <summary>
    /// Marks that detection occurred (for continuous cooldown).
    /// </summary>
    public void MarkDetected()
    {
        LastDetectionTime = DateTime.UtcNow;
    }
}
