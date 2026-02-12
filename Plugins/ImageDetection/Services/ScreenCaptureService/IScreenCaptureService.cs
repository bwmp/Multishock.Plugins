using Emgu.CV;
using ImageDetection.Models;

namespace ImageDetection.Services;

/// <summary>
/// Platform-agnostic contract for screen and window capture.
/// Implementations provide OS-specific capture behavior.
/// </summary>
public interface IScreenCaptureService
{
    /// <summary>
    /// Whether screen capture is available on the current platform/runtime.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Optional reason when <see cref="IsSupported"/> is false.
    /// </summary>
    string? UnsupportedReason { get; }

    /// <summary>
    /// Current capture configuration.
    /// </summary>
    CaptureConfig Config { get; set; }

    /// <summary>
    /// Captures a screenshot based on the current configuration.
    /// </summary>
    Mat CaptureScreen();

    /// <summary>
    /// Captures a specific monitor.
    /// </summary>
    Mat CaptureMonitor(int monitorIndex = 1);

    /// <summary>
    /// Captures a specific window.
    /// </summary>
    Mat CaptureWindow(string? windowTitle, string? windowId = null);

    /// <summary>
    /// Applies region filtering to a screenshot.
    /// </summary>
    Mat ApplyRegionFilter(Mat screenshot, RegionConfig regionConfig);

    /// <summary>
    /// Gets information about all available monitors.
    /// </summary>
    List<MonitorInfo> GetMonitors();

    /// <summary>
    /// Gets the current monitor resolution.
    /// </summary>
    Resolution GetCurrentMonitorResolution();

    /// <summary>
    /// Gets a list of visible windows.
    /// </summary>
    List<WindowInfo> GetWindows();

    /// <summary>
    /// Gets information about the current foreground window.
    /// </summary>
    WindowInfo? GetForegroundWindowInfo();

    /// <summary>
    /// Checks whether the required process/title is currently focused.
    /// </summary>
    bool IsRequiredWindowFocused(string? requiredProcessName, string? requiredTitle);
}
