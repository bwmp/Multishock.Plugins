using System;

namespace ImageDetection.Models;

/// <summary>
/// Represents a single detection event.
/// </summary>
public class DetectionEvent
{
    /// <summary>
    /// Unique identifier for this detection.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// When the detection occurred.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// The module that contains the detected image.
    /// </summary>
    public string ModuleId { get; set; } = "";

    /// <summary>
    /// The module display name.
    /// </summary>
    public string ModuleName { get; set; } = "";

    /// <summary>
    /// The detected image configuration.
    /// </summary>
    public string ImageId { get; set; } = "";

    /// <summary>
    /// The image display name.
    /// </summary>
    public string ImageName { get; set; } = "";

    /// <summary>
    /// Detection confidence score (0.0 to 1.0).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Detection location (X coordinate).
    /// </summary>
    public int LocationX { get; set; }

    /// <summary>
    /// Detection location (Y coordinate).
    /// </summary>
    public int LocationY { get; set; }

    /// <summary>
    /// Whether an action was triggered.
    /// </summary>
    public bool ActionTriggered { get; set; }

    /// <summary>
    /// The type of action triggered (if any).
    /// </summary>
    public string? ActionType { get; set; }

    /// <summary>
    /// Screenshot at the time of detection (stored as base64 or path).
    /// </summary>
    public string? ScreenshotPath { get; set; }

    /// <summary>
    /// Thumbnail of the detected region.
    /// </summary>
    public string? ThumbnailPath { get; set; }

    /// <summary>
    /// Time taken for this detection.
    /// </summary>
    public TimeSpan DetectionTime { get; set; }

    /// <summary>
    /// Whether this detection was during cooldown (no action taken).
    /// </summary>
    public bool WasInCooldown { get; set; }

    /// <summary>
    /// Human-readable time ago string.
    /// </summary>
    public string TimeAgo
    {
        get
        {
            var diff = DateTime.Now - Timestamp;
            if (diff.TotalSeconds < 60) return $"{(int)diff.TotalSeconds}s ago";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            return $"{(int)diff.TotalDays}d ago";
        }
    }
}
