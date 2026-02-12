using Emgu.CV;
using Emgu.CV.CvEnum;
using ImageDetection.Models;

namespace ImageDetection.Services;

/// <summary>
/// Fallback capture service for unsupported platforms.
/// </summary>
public class NoopScreenCaptureService : IScreenCaptureService
{
    private static readonly Resolution FallbackResolution = new(1920, 1080);
    private CaptureConfig _config = new();

    public bool IsSupported => false;

    public string? UnsupportedReason => "Screen capture is only implemented for Windows in this plugin build.";

    public CaptureConfig Config
    {
        get => _config;
        set => _config = value ?? new CaptureConfig();
    }

    public Mat CaptureScreen() => throw new PlatformNotSupportedException(UnsupportedReason);

    public Mat CaptureMonitor(int monitorIndex = 1) => throw new PlatformNotSupportedException(UnsupportedReason);

    public Mat CaptureWindow(string? windowTitle, string? windowId = null) =>
        throw new PlatformNotSupportedException(UnsupportedReason);

    public Mat ApplyRegionFilter(Mat screenshot, RegionConfig regionConfig)
    {
        if (regionConfig.Type == RegionType.FullScreen)
        {
            return screenshot.Clone();
        }

        if (regionConfig.Type == RegionType.Custom && regionConfig.CustomRegion != null)
        {
            return ApplyCustomRegion(screenshot, regionConfig.CustomRegion);
        }

        if (regionConfig.Type == RegionType.Grid)
        {
            return ApplyGridSections(screenshot, regionConfig.GridSections);
        }

        return screenshot.Clone();
    }

    public List<MonitorInfo> GetMonitors() =>
    [
        new MonitorInfo
        {
            Index = 1,
            Name = "Primary",
            IsPrimary = true,
            Resolution = FallbackResolution,
            Position = new Models.Point(0, 0)
        }
    ];

    public Resolution GetCurrentMonitorResolution() => FallbackResolution;

    public List<WindowInfo> GetWindows() => [];

    public WindowInfo? GetForegroundWindowInfo() => null;

    public bool IsRequiredWindowFocused(string? requiredProcessName, string? requiredTitle)
    {
        if (string.IsNullOrWhiteSpace(requiredProcessName) && string.IsNullOrWhiteSpace(requiredTitle))
            return true;

        return false;
    }

    private static Mat ApplyCustomRegion(Mat screenshot, ScreenRegion region)
    {
        int x = Math.Max(0, Math.Min(region.X, screenshot.Width - 1));
        int y = Math.Max(0, Math.Min(region.Y, screenshot.Height - 1));
        int width = Math.Min(region.Width, screenshot.Width - x);
        int height = Math.Min(region.Height, screenshot.Height - y);

        if (width <= 0 || height <= 0)
        {
            return screenshot.Clone();
        }

        var roi = new System.Drawing.Rectangle(x, y, width, height);
        using var subMat = new Mat(screenshot, roi);
        return subMat.Clone();
    }

    private static Mat ApplyGridSections(Mat screenshot, GridSections? sections)
    {
        if (sections == null || sections.AllSectionsEnabled())
        {
            return screenshot.Clone();
        }

        var result = screenshot.Clone();
        int sectionWidth = screenshot.Width / 3;
        int sectionHeight = screenshot.Height / 3;

        using var mask = new Mat(screenshot.Size, DepthType.Cv8U, 1);
        mask.SetTo(new Emgu.CV.Structure.MCvScalar(0));

        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                if (sections[row, col])
                {
                    int sx = col * sectionWidth;
                    int sy = row * sectionHeight;

                    int w = (col == 2) ? screenshot.Width - sx : sectionWidth;
                    int h = (row == 2) ? screenshot.Height - sy : sectionHeight;

                    var sectionRect = new System.Drawing.Rectangle(sx, sy, w, h);
                    using var sectionMat = new Mat(mask, sectionRect);
                    sectionMat.SetTo(new Emgu.CV.Structure.MCvScalar(255));
                }
            }
        }

        using var maskedResult = new Mat();
        CvInvoke.BitwiseAnd(result, result, maskedResult, mask);

        result.Dispose();
        return maskedResult.Clone();
    }
}
