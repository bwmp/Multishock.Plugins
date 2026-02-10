namespace ImageDetection.Models;

/// <summary>
/// Type of detection target.
/// </summary>
public enum DetectionTargetType
{
    /// <summary>
    /// Template matching (existing behavior). Requires an image file.
    /// </summary>
    Template = 0,

    /// <summary>
    /// Meter/healthbar detection. Uses region + fill analysis, no template image needed.
    /// </summary>
    Meter = 1
}

/// <summary>
/// Fill direction of a meter/healthbar.
/// </summary>
public enum MeterFillDirection
{
    /// <summary>
    /// Bar fills from left to right (most common).
    /// </summary>
    LeftToRight = 0,

    /// <summary>
    /// Bar fills from right to left.
    /// </summary>
    RightToLeft = 1,

    /// <summary>
    /// Bar fills from bottom to top.
    /// </summary>
    BottomToTop = 2,

    /// <summary>
    /// Bar fills from top to bottom.
    /// </summary>
    TopToBottom = 3
}

/// <summary>
/// Type of meter value change event.
/// </summary>
public enum MeterChangeType
{
    /// <summary>
    /// Generic value change (increase or decrease).
    /// </summary>
    Changed,

    /// <summary>
    /// Value decreased past threshold (damage taken).
    /// </summary>
    DamageTaken,

    /// <summary>
    /// Value increased past threshold (healing/recovery).
    /// </summary>
    Healed
}

/// <summary>
/// How the action intensity is determined when a meter change is detected.
/// </summary>
public enum MeterIntensityMode
{
    /// <summary>
    /// Damage % is scaled against the configured max intensity.
    /// e.g. 30% HP loss with max 80 → intensity 24.
    /// </summary>
    Scaled = 0,

    /// <summary>
    /// Damage % is used directly as the intensity, capped at a maximum.
    /// e.g. 30% HP loss with cap 25 → intensity 25.
    /// </summary>
    Direct = 1,

    /// <summary>
    /// Always uses the configured intensity regardless of damage amount.
    /// </summary>
    Fixed = 2
}

/// <summary>
/// Configuration for meter/healthbar detection on a target.
/// </summary>
public class MeterDetectionConfig
{
    /// <summary>
    /// Whether meter tracking is enabled for this target.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Direction the bar fills.
    /// </summary>
    public MeterFillDirection Direction { get; set; } = MeterFillDirection.LeftToRight;

    /// <summary>
    /// Minimum percent change required to emit an event (filters noise).
    /// </summary>
    public double MinDeltaPercent { get; set; } = 2.0;

    /// <summary>
    /// Number of frames to average for smoothing (reduces jitter).
    /// </summary>
    public int SmoothingFrames { get; set; } = 3;

    /// <summary>
    /// Minimum time between emitted events in milliseconds.
    /// </summary>
    public int EventCooldownMs { get; set; } = 300;

    /// <summary>
    /// Whether to only emit events when the value decreases (damage-only mode).
    /// </summary>
    public bool DecreasesOnly { get; set; } = true;

    /// <summary>
    /// How action intensity is determined on meter changes.
    /// </summary>
    public MeterIntensityMode IntensityMode { get; set; } = MeterIntensityMode.Scaled;

    /// <summary>
    /// If enabled, this meter only updates while a specific window is focused.
    /// </summary>
    public bool RequireFocusedWindow { get; set; } = false;

    /// <summary>
    /// Optional required focused window process name.
    /// </summary>
    public string? RequiredFocusWindowProcess { get; set; }

    /// <summary>
    /// Optional required focused window title text.
    /// Uses case-insensitive contains matching.
    /// </summary>
    public string? RequiredFocusWindowTitle { get; set; }

    /// <summary>
    /// Whether to use a color hint for bar detection.
    /// </summary>
    public bool UseColorHint { get; set; } = false;

    /// <summary>
    /// Optional color hint for the filled portion of the bar (HSV range).
    /// </summary>
    public HsvRange? ColorHint { get; set; }

    /// <summary>
    /// Path to a saved screenshot of the selected region (for visual preview).
    /// </summary>
    public string? RegionPreviewPath { get; set; }
}

/// <summary>
/// HSV color range for color-based bar detection.
/// </summary>
public class HsvRange
{
    /// <summary>
    /// Minimum hue (0-180 in OpenCV convention).
    /// </summary>
    public int HueMin { get; set; }

    /// <summary>
    /// Maximum hue (0-180).
    /// </summary>
    public int HueMax { get; set; }

    /// <summary>
    /// Minimum saturation (0-255).
    /// </summary>
    public int SatMin { get; set; } = 50;

    /// <summary>
    /// Maximum saturation (0-255).
    /// </summary>
    public int SatMax { get; set; } = 255;

    /// <summary>
    /// Minimum value/brightness (0-255).
    /// </summary>
    public int ValMin { get; set; } = 50;

    /// <summary>
    /// Maximum value/brightness (0-255).
    /// </summary>
    public int ValMax { get; set; } = 255;

    /// <summary>
    /// Predefined color hints for common healthbar colors.
    /// </summary>
    public static HsvRange Green => new() { HueMin = 35, HueMax = 85, SatMin = 50, ValMin = 50 };
    public static HsvRange Red => new() { HueMin = 0, HueMax = 10, SatMin = 50, ValMin = 50 };
    public static HsvRange Blue => new() { HueMin = 100, HueMax = 130, SatMin = 50, ValMin = 50 };
    public static HsvRange Yellow => new() { HueMin = 20, HueMax = 35, SatMin = 50, ValMin = 50 };
    public static HsvRange White => new() { HueMin = 0, HueMax = 180, SatMin = 0, SatMax = 30, ValMin = 200 };
}

/// <summary>
/// Result of a single meter value reading.
/// </summary>
public class MeterSample
{
    /// <summary>
    /// The module this sample belongs to.
    /// </summary>
    public string ModuleId { get; set; } = string.Empty;

    /// <summary>
    /// The target this sample belongs to.
    /// </summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// Current meter fill percentage (0-100).
    /// </summary>
    public double CurrentPercent { get; set; }

    /// <summary>
    /// When this sample was taken.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A value change event emitted by the analyzer when a significant change is detected.
/// </summary>
public class ValueChangeEvent
{
    /// <summary>
    /// The module this event belongs to.
    /// </summary>
    public string ModuleId { get; set; } = string.Empty;

    /// <summary>
    /// The target this event belongs to.
    /// </summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the target.
    /// </summary>
    public string TargetName { get; set; } = string.Empty;

    /// <summary>
    /// Current meter fill percentage (0-100).
    /// </summary>
    public double CurrentPercent { get; set; }

    /// <summary>
    /// Previous meter fill percentage (0-100).
    /// </summary>
    public double PreviousPercent { get; set; }

    /// <summary>
    /// Change amount (current - previous). Negative = decrease.
    /// </summary>
    public double DeltaPercent { get; set; }

    /// <summary>
    /// Whether the value decreased.
    /// </summary>
    public bool IsDecrease { get; set; }

    /// <summary>
    /// Classification of the change.
    /// </summary>
    public MeterChangeType ChangeType { get; set; }

    /// <summary>
    /// When this event occurred.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
