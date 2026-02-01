using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;
using ImageDetection.Models;
using Microsoft.Extensions.Logging;

namespace ImageDetection.Services;

/// <summary>
/// Service for capturing screenshots from monitors or windows.
/// Uses P/Invoke directly to avoid System.Drawing.Common dependency.
/// </summary>
public class ScreenCaptureService
{
    private readonly ILogger? _logger;
    private CaptureConfig _config = new();

    public ScreenCaptureService(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Current capture configuration.
    /// </summary>
    public CaptureConfig Config
    {
        get => _config;
        set => _config = value ?? new CaptureConfig();
    }

    /// <summary>
    /// Captures a screenshot based on the current configuration.
    /// </summary>
    public Mat CaptureScreen()
    {
        return _config.SourceType switch
        {
            CaptureSourceType.Monitor => CaptureMonitor(_config.MonitorIndex),
            CaptureSourceType.Window => CaptureWindow(_config.WindowTitle, _config.WindowHandle),
            _ => CaptureMonitor(1)
        };
    }

    /// <summary>
    /// Captures a specific monitor.
    /// </summary>
    /// <param name="monitorIndex">1-based monitor index (1 = primary).</param>
    public Mat CaptureMonitor(int monitorIndex = 1)
    {
        try
        {
            var monitors = GetMonitors();

            if (monitors.Count == 0)
            {
                var fallback = GetPrimaryBounds();
                return CaptureRegion(fallback.Left, fallback.Top, fallback.Width, fallback.Height);
            }

            if (monitorIndex < 1 || monitorIndex > monitors.Count)
            {
                _logger?.LogWarning("Monitor index {Index} out of range, using primary", monitorIndex);
                monitorIndex = 1;
            }

            var monitor = monitors[monitorIndex - 1];
            return CaptureRegion(monitor.Position.X, monitor.Position.Y, monitor.Resolution.Width, monitor.Resolution.Height);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to capture monitor {Index}", monitorIndex);
            throw;
        }
    }

    /// <summary>
    /// Captures a specific window by title.
    /// </summary>
    public Mat CaptureWindow(string? windowTitle, IntPtr? windowHandle = null)
    {
        try
        {
            IntPtr hwnd = windowHandle ?? IntPtr.Zero;

            if (hwnd == IntPtr.Zero && !string.IsNullOrEmpty(windowTitle))
            {
                hwnd = FindWindow(null, windowTitle);
            }

            if (hwnd == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Window not found: {windowTitle}");
            }

            if (!GetWindowRect(hwnd, out RECT rect))
            {
                throw new InvalidOperationException("Failed to get window rectangle");
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("Window has invalid dimensions");
            }

            return CaptureRegion(rect.Left, rect.Top, width, height);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to capture window: {Title}", windowTitle);
            throw;
        }
    }

    /// <summary>
    /// Captures a specific region of the screen using GDI.
    /// </summary>
    public Mat CaptureRegion(int x, int y, int width, int height)
    {
        IntPtr hdcScreen = IntPtr.Zero;
        IntPtr hdcMem = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr hOld = IntPtr.Zero;

        try
        {
            hdcScreen = GetDC(IntPtr.Zero);
            if (hdcScreen == IntPtr.Zero)
                throw new InvalidOperationException("Failed to get screen DC");

            hdcMem = CreateCompatibleDC(hdcScreen);
            if (hdcMem == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create compatible DC");

            hBitmap = CreateCompatibleBitmap(hdcScreen, width, height);
            if (hBitmap == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create compatible bitmap");

            hOld = SelectObject(hdcMem, hBitmap);

            if (!BitBlt(hdcMem, 0, 0, width, height, hdcScreen, x, y, SRCCOPY))
                throw new InvalidOperationException("BitBlt failed");

            return HBitmapToMat(hBitmap, width, height);
        }
        finally
        {
            if (hOld != IntPtr.Zero && hdcMem != IntPtr.Zero)
                SelectObject(hdcMem, hOld);
            if (hBitmap != IntPtr.Zero)
                DeleteObject(hBitmap);
            if (hdcMem != IntPtr.Zero)
                DeleteDC(hdcMem);
            if (hdcScreen != IntPtr.Zero)
                ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    /// <summary>
    /// Converts an HBITMAP to Emgu.CV Mat.
    /// </summary>
    private Mat HBitmapToMat(IntPtr hBitmap, int width, int height)
    {
        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // Negative for top-down DIB
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB,
                biSizeImage = 0,
                biXPelsPerMeter = 0,
                biYPelsPerMeter = 0,
                biClrUsed = 0,
                biClrImportant = 0
            }
        };

        int stride = ((width * 32 + 31) / 32) * 4;
        int bufferSize = stride * height;
        byte[] pixelData = new byte[bufferSize];

        IntPtr hdc = GetDC(IntPtr.Zero);
        try
        {
            int result = GetDIBits(hdc, hBitmap, 0, (uint)height, pixelData, ref bmi, DIB_RGB_COLORS);
            if (result == 0)
                throw new InvalidOperationException("GetDIBits failed");
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }

        var mat = new Mat(height, width, DepthType.Cv8U, 4);
        Marshal.Copy(pixelData, 0, mat.DataPointer, bufferSize);

        return mat;
    }

    /// <summary>
    /// Applies region filtering to a screenshot.
    /// </summary>
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

    /// <summary>
    /// Applies a custom rectangular region mask.
    /// </summary>
    private Mat ApplyCustomRegion(Mat screenshot, ScreenRegion region)
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
        return new Mat(screenshot, roi);
    }

    /// <summary>
    /// Applies 3x3 grid section masking.
    /// Disabled sections are blacked out.
    /// </summary>
    private Mat ApplyGridSections(Mat screenshot, GridSections? sections)
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

    /// <summary>
    /// Gets information about all available monitors.
    /// </summary>
    public List<MonitorInfo> GetMonitors()
    {
        try
        {
            var monitors = new List<MonitorInfo>();

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
            {
                var info = new MONITORINFOEX
                {
                    cbSize = Marshal.SizeOf<MONITORINFOEX>()
                };

                if (GetMonitorInfo(hMonitor, ref info))
                {
                    var bounds = info.rcMonitor;
                    monitors.Add(new MonitorInfo
                    {
                        Index = monitors.Count + 1,
                        Name = info.szDevice ?? string.Empty,
                        IsPrimary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                        Resolution = new Resolution(bounds.Width, bounds.Height),
                        Position = new Models.Point(bounds.Left, bounds.Top)
                    });
                }

                return true;
            }, IntPtr.Zero);

            if (monitors.Count == 0)
            {
                var fallback = GetPrimaryBounds();
                monitors.Add(new MonitorInfo
                {
                    Index = 1,
                    Name = "Primary",
                    IsPrimary = true,
                    Resolution = new Resolution(fallback.Width, fallback.Height),
                    Position = new Models.Point(fallback.Left, fallback.Top)
                });
            }

            return monitors;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to enumerate monitors");

            var fallback = GetPrimaryBounds();
            return new List<MonitorInfo>
            {
                new MonitorInfo
                {
                    Index = 1,
                    Name = "Primary",
                    IsPrimary = true,
                    Resolution = new Resolution(fallback.Width, fallback.Height),
                    Position = new Models.Point(fallback.Left, fallback.Top)
                }
            };
        }
    }

    /// <summary>
    /// Gets the current monitor resolution.
    /// </summary>
    public Resolution GetCurrentMonitorResolution()
    {
        var monitors = GetMonitors();
        if (monitors.Count == 0)
        {
            var fallback = GetPrimaryBounds();
            return new Resolution(fallback.Width, fallback.Height);
        }

        var index = Math.Clamp(_config.MonitorIndex - 1, 0, monitors.Count - 1);
        var monitor = monitors[index];
        return monitor.Resolution;
    }

    /// <summary>
    /// Gets a list of visible windows.
    /// </summary>
    public List<WindowInfo> GetWindows()
    {
        var windows = new List<WindowInfo>();

        EnumWindows((hwnd, lParam) =>
        {
            if (!IsWindowVisible(hwnd)) return true;

            int length = GetWindowTextLength(hwnd);
            if (length == 0) return true;

            var builder = new System.Text.StringBuilder(length + 1);
            GetWindowText(hwnd, builder, builder.Capacity);
            var title = builder.ToString();

            if (string.IsNullOrWhiteSpace(title)) return true;

            string processName = "";
            try
            {
                GetWindowThreadProcessId(hwnd, out uint processId);
                var process = System.Diagnostics.Process.GetProcessById((int)processId);
                processName = process.ProcessName;
            }
            catch { }

            windows.Add(new WindowInfo
            {
                Handle = hwnd,
                Title = title,
                ProcessName = processName,
                IsVisible = true
            });

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    #region Native Methods and Structures

    private const uint MONITORINFOF_PRIMARY = 0x00000001;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const uint SRCCOPY = 0x00CC0020;
    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // GDI32
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
        IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines,
        [Out] byte[] lpvBits, ref BITMAPINFO lpbi, uint uUsage);

    // User32
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    // Structures
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public uint[] bmiColors;
    }

    private static RECT GetPrimaryBounds()
    {
        var width = GetSystemMetrics(SM_CXSCREEN);
        var height = GetSystemMetrics(SM_CYSCREEN);
        return new RECT
        {
            Left = 0,
            Top = 0,
            Right = Math.Max(0, width),
            Bottom = Math.Max(0, height)
        };
    }

    #endregion
}
