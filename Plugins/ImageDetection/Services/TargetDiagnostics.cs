using ImageDetection.Models;

namespace ImageDetection.Services;

/// <summary>
/// Result of a one-shot "Test now" run against a single target.
/// </summary>
public class TargetTestResult
{
    /// <summary>Whether the test itself ran (capture + pipeline succeeded).</summary>
    public bool Success { get; set; }

    /// <summary>For template targets: whether the match passed the threshold.</summary>
    public bool Found { get; set; }

    /// <summary>For template targets: best match confidence.</summary>
    public double? Confidence { get; set; }

    /// <summary>For template targets: the threshold that was tested against.</summary>
    public double? Threshold { get; set; }

    /// <summary>For meter targets: the fill percentage read from the region.</summary>
    public double? MeterPercent { get; set; }

    /// <summary>For OCR targets: the raw text read from the region.</summary>
    public string? OcrText { get; set; }

    /// <summary>For OCR targets: keyword/number parsed from the text, if any.</summary>
    public string? OcrParsed { get; set; }

    /// <summary>Human-readable outcome ("Match found (93.1% >= 80%)", error reason, ...).</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>PNG data URI of the capture with the match/region drawn on it.</summary>
    public string? PreviewDataUri { get; set; }

    public TimeSpan Duration { get; set; }

    public static TargetTestResult Fail(string message) => new() { Success = false, Message = message };
}

/// <summary>
/// Live per-target status collected by the detection loop.
/// </summary>
public class TargetHealth
{
    /// <summary>When the target was last processed by the loop.</summary>
    public DateTime LastCheckedUtc { get; set; }

    /// <summary>When the target last matched / produced an event.</summary>
    public DateTime? LastDetectedUtc { get; set; }

    /// <summary>Template targets: confidence of the most recent check.</summary>
    public double? LastConfidence { get; set; }

    /// <summary>Meter targets: most recent fill percentage.</summary>
    public double? LastMeterPercent { get; set; }

    /// <summary>OCR targets: most recent text read.</summary>
    public string? LastOcrText { get; set; }

    /// <summary>Most recent processing error, cleared on the next good pass.</summary>
    public string? LastError { get; set; }

    /// <summary>Seconds until the target can trigger again, 0 when off cooldown.</summary>
    public double CooldownRemainingSeconds { get; set; }

    public TargetHealth Clone() => (TargetHealth)MemberwiseClone();
}

/// <summary>
/// Detects target configurations that silently do nothing at runtime, so the
/// UI can badge them instead of leaving the user guessing.
/// </summary>
public static class TargetValidator
{
    public static List<string> GetIssues(DetectionImage image, bool templateFileExists, bool ocrAvailable)
    {
        var issues = new List<string>();

        switch (image.TargetType)
        {
            case DetectionTargetType.Template when !templateFileExists:
                issues.Add("Template image file is missing");
                break;

            case DetectionTargetType.Meter or DetectionTargetType.Ocr
                when image.Region.Type != RegionType.Custom || image.Region.CustomRegion == null:
                issues.Add("Needs a screen region (set Region to Custom and pick one)");
                break;
        }

        if (image.TargetType == DetectionTargetType.Ocr && !ocrAvailable)
        {
            issues.Add("Windows OCR is unavailable on this system");
        }

        if (image.Action.Enabled && image.Action.ShockerIds.Count == 0)
        {
            issues.Add("Action is enabled but no shockers are selected");
        }

        return issues;
    }
}
