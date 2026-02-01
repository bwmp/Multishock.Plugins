namespace ImageDetection.Models;

/// <summary>
/// Configuration for screen capture.
/// </summary>
public class CaptureConfig
{
    /// <summary>
    /// Type of capture source.
    /// </summary>
    public CaptureSourceType SourceType { get; set; } = CaptureSourceType.Monitor;

    /// <summary>
    /// Monitor index (1-based, 0 = primary). Used when SourceType is Monitor.
    /// </summary>
    public int MonitorIndex { get; set; } = 1;

    /// <summary>
    /// Window title to capture. Used when SourceType is Window.
    /// </summary>
    public string? WindowTitle { get; set; }

    /// <summary>
    /// Window handle (optional, takes precedence over WindowTitle).
    /// </summary>
    public IntPtr? WindowHandle { get; set; }

    /// <summary>
    /// Delay between captures in milliseconds.
    /// </summary>
    public int CaptureDelayMs { get; set; } = 100;

    /// <summary>
    /// Whether to include the cursor in captures.
    /// </summary>
    public bool IncludeCursor { get; set; } = false;
}

/// <summary>
/// Type of capture source.
/// </summary>
public enum CaptureSourceType
{
    /// <summary>
    /// Capture an entire monitor.
    /// </summary>
    Monitor,

    /// <summary>
    /// Capture a specific window.
    /// </summary>
    Window
}

/// <summary>
/// Information about an available monitor.
/// </summary>
public class MonitorInfo
{
    /// <summary>
    /// Monitor index (1-based).
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Display name/identifier.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is the primary monitor.
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// Monitor resolution.
    /// </summary>
    public Resolution Resolution { get; set; } = new();

    /// <summary>
    /// Monitor position (top-left corner).
    /// </summary>
    public Point Position { get; set; }
}

/// <summary>
/// Information about an available window.
/// </summary>
public class WindowInfo
{
    /// <summary>
    /// Window handle.
    /// </summary>
    public IntPtr Handle { get; set; }

    /// <summary>
    /// Window title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Process name that owns the window.
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the window is currently visible.
    /// </summary>
    public bool IsVisible { get; set; }
}
