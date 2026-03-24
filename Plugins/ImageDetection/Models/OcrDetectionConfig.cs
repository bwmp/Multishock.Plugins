namespace ImageDetection.Models;

/// <summary>
/// OCR detection mode.
/// </summary>
public enum OcrDetectionMode
{
    /// <summary>
    /// Trigger on keyword/phrase matches.
    /// </summary>
    Keyword = 0,

    /// <summary>
    /// Trigger on parsed numeric value changes.
    /// </summary>
    Number = 1,

    /// <summary>
    /// Trigger on either keyword match or numeric value changes.
    /// </summary>
    KeywordOrNumber = 2
}

/// <summary>
/// Type of OCR change event.
/// </summary>
public enum OcrChangeType
{
    KeywordMatched,
    NumberChanged,
    NumberIncreased,
    NumberDecreased
}

/// <summary>
/// Configuration for OCR-based text/number detection on a target.
/// </summary>
public class OcrDetectionConfig
{
    /// <summary>
    /// Whether OCR detection is enabled for this target.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// OCR trigger mode.
    /// </summary>
    public OcrDetectionMode Mode { get; set; } = OcrDetectionMode.KeywordOrNumber;

    /// <summary>
    /// Keywords/phrases to search for.
    /// </summary>
    public List<string> Keywords { get; set; } = ["Eliminated"];

    /// <summary>
    /// Whether keyword matching is case-insensitive.
    /// </summary>
    public bool IgnoreCase { get; set; } = true;

    /// <summary>
    /// Regex pattern used to parse a number from OCR text.
    /// </summary>
    public string NumberRegex { get; set; } = @"-?\d+(?:\.\d+)?";

    /// <summary>
    /// Minimum number change required to emit an event.
    /// </summary>
    public double MinNumberDelta { get; set; } = 1.0;

    /// <summary>
    /// Number of frames to average for numeric smoothing.
    /// </summary>
    public int SmoothingFrames { get; set; } = 3;

    /// <summary>
    /// Minimum time between emitted events in milliseconds.
    /// </summary>
    public int EventCooldownMs { get; set; } = 300;

    /// <summary>
    /// Optional character whitelist hint for OCR engines that support it.
    /// </summary>
    public string? CharacterWhitelist { get; set; }

    /// <summary>
    /// Scale factor applied before OCR to improve readability.
    /// </summary>
    public double PreprocessScale { get; set; } = 2.0;

    /// <summary>
    /// Whether to apply Otsu binary thresholding before OCR.
    /// </summary>
    public bool UseThresholding { get; set; } = true;

    /// <summary>
    /// Whether to invert colors after thresholding.
    /// </summary>
    public bool InvertColors { get; set; }

    /// <summary>
    /// If enabled, this OCR target only updates while a specific window is focused.
    /// </summary>
    public bool RequireFocusedWindow { get; set; }

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
    /// Whether action intensity should scale with numeric delta.
    /// </summary>
    public bool ScaleActionWithNumberDelta { get; set; } = true;

    /// <summary>
    /// Delta value that maps to max action intensity when scaling is enabled.
    /// </summary>
    public double MaxDeltaForMaxIntensity { get; set; } = 25.0;

    /// <summary>
    /// Path to a saved screenshot of the selected OCR region (for visual preview).
    /// </summary>
    public string? RegionPreviewPath { get; set; }
}

/// <summary>
/// A parsed OCR reading for a target.
/// </summary>
public sealed class OcrReading
{
    public string RawText { get; set; } = string.Empty;
    public string NormalizedText { get; set; } = string.Empty;
    public string? MatchedKeyword { get; set; }
    public double? NumberValue { get; set; }
}

/// <summary>
/// A value/keyword event emitted from OCR processing.
/// </summary>
public sealed class OcrDetectionEvent
{
    public string ModuleId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public string RawText { get; set; } = string.Empty;
    public string NormalizedText { get; set; } = string.Empty;
    public string? MatchedKeyword { get; set; }
    public double? CurrentNumber { get; set; }
    public double? PreviousNumber { get; set; }
    public double? DeltaNumber { get; set; }
    public OcrChangeType ChangeType { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
