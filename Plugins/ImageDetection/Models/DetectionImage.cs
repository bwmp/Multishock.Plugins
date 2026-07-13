namespace ImageDetection.Models;

/// <summary>
/// Configuration for a single detection target image.
/// </summary>
public class DetectionImage
{
    /// <summary>
    /// Unique identifier for this image (typically the filename).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display name for the image.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Full path to the image file.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Whether this image is enabled for detection.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Detection threshold (0.0 - 1.0). Higher = stricter matching.
    /// </summary>
    public double Threshold { get; set; } = 0.8;

    /// <summary>
    /// ID of the detection algorithm to use.
    /// </summary>
    public string AlgorithmId { get; set; } = "template-matching";

    /// <summary>
    /// Region configuration for this image.
    /// </summary>
    public RegionConfig Region { get; set; } = new();

    /// <summary>
    /// Cooldown configuration for this image.
    /// </summary>
    public CooldownConfig Cooldown { get; set; } = new();

    /// <summary>
    /// Action configuration for this image.
    /// </summary>
    public ActionConfig Action { get; set; } = new();

    /// <summary>
    /// Resolution at which the image was captured (for scaling).
    /// </summary>
    public Resolution CaptureResolution { get; set; } = new(1920, 1080);

    /// <summary>
    /// Whether to auto-resize this image to match the current monitor resolution.
    /// Disable if you want exact pixel matching at the original resolution.
    /// </summary>
    public bool AutoResize { get; set; } = true;

    /// <summary>Use three-channel color matching instead of the faster grayscale path.</summary>
    public bool UseColorMatching { get; set; }

    /// <summary>
    /// Number of consecutive capture frames the template must match before the
    /// trigger fires (1 = fire on the first match). Filters out single-frame
    /// false positives from animations, transitions, and compression artifacts.
    /// </summary>
    public int RequiredConsecutiveMatches { get; set; } = 1;

    /// <summary>
    /// Multi-scale matching configuration. Only used for template targets.
    /// </summary>
    public MultiScaleConfig MultiScale { get; set; } = new();

    /// <summary>
    /// Whether to show a toast notification when this target triggers.
    /// Toasts are additionally rate-limited to one per target per 30 seconds.
    /// </summary>
    public bool ShowDetectionToasts { get; set; } = true;

    /// <summary>Run this target every Nth capture loop. Values below one are treated as one.</summary>
    public int ProcessEveryNthFrame { get; set; } = 1;

    /// <summary>Only process this template while the configured window is focused.</summary>
    public bool RequireFocusedWindow { get; set; }

    public string? RequiredFocusWindowProcess { get; set; }

    public string? RequiredFocusWindowTitle { get; set; }

    /// <summary>
    /// The type of detection target (Template, Meter, or Ocr).
    /// Defaults to Template for backward compatibility.
    /// </summary>
    public DetectionTargetType TargetType { get; set; } = DetectionTargetType.Template;

    /// <summary>
    /// Meter/healthbar detection configuration. Only used when TargetType is Meter.
    /// </summary>
    public MeterDetectionConfig Meter { get; set; } = new();

    /// <summary>
    /// OCR text/number detection configuration. Only used when TargetType is Ocr.
    /// </summary>
    public OcrDetectionConfig Ocr { get; set; } = new();

    /// <summary>
    /// Optional notes/description for this image.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// When the image was added.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the image config was last modified.
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Configuration for multi-scale template matching. When enabled, the template
/// is additionally tried at several scales between MinScale and MaxScale so
/// matching survives in-game UI scale differences that monitor-resolution
/// auto-resize cannot account for.
/// </summary>
public class MultiScaleConfig
{
    /// <summary>
    /// Whether multi-scale matching is enabled. Costs roughly Steps times the
    /// matching time when the template is not found at native scale.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Smallest template scale to try (relative to the loaded template size).
    /// </summary>
    public double MinScale { get; set; } = 0.8;

    /// <summary>
    /// Largest template scale to try.
    /// </summary>
    public double MaxScale { get; set; } = 1.25;

    /// <summary>
    /// Number of scales to try across the range (including 1.0).
    /// </summary>
    public int Steps { get; set; } = 7;
}

/// <summary>
/// Screen resolution for image scaling calculations.
/// </summary>
public class Resolution
{
    public int Width { get; set; }
    public int Height { get; set; }

    public Resolution() { }

    public Resolution(int width, int height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Calculates scale ratios to convert from this resolution to target resolution.
    /// </summary>
    public (double scaleX, double scaleY) GetScaleRatios(Resolution target)
    {
        return ((double)target.Width / Width, (double)target.Height / Height);
    }
}
