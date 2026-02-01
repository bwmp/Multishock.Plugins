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
