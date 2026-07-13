using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;
using ImageDetection.Models;
using Microsoft.Extensions.Logging;

namespace ImageDetection.Services;

/// <summary>
/// Windows implementation of <see cref="IScreenCaptureService"/>.
/// Prefers Windows Graphics Capture (captures hardware-accelerated and
/// fullscreen content, ignores overlapping windows) and falls back to
/// Win32/GDI BitBlt when WGC is unavailable or fails.
/// </summary>
public class WindowsScreenCaptureService(ILogger? logger = null) : IScreenCaptureService, IDisposable
{
    private readonly ILogger? _logger = logger;
    private CaptureConfig _config = new();
    private readonly object _captureBufferLock = new();
    private Mat? _captureBuffer;

    private readonly object _wgcLock = new();
    private WgcCaptureSession? _wgcSession;
    private string? _wgcSessionKey;
    private bool _wgcFailed;

    public bool IsSupported => true;

    public string? UnsupportedReason => null;

    /// <summary>
    /// Non-null when the host process is not per-monitor DPI aware, meaning
    /// Windows reports virtualized (scaled) coordinates and captures/regions
    /// will not line up with physical pixels on scaled displays.
    /// </summary>
    public string? DpiWarning { get; } = CheckDpiAwareness(logger);

    /// <summary>
    /// Current capture configuration.
    /// </summary>
    public CaptureConfig Config
    {
        get => _config;
        set
        {
            _config = value ?? new CaptureConfig();

            // Capture source or backend may have changed: drop the cached WGC
            // session and allow Auto mode to retry WGC after an earlier failure.
            lock (_wgcLock)
            {
                ResetWgcSessionLocked();
                _wgcFailed = false;
            }
        }
    }

    /// <summary>
    /// Captures a screenshot based on the current configuration.
    /// </summary>
    public Mat CaptureScreen()
    {
        if (UseWgcBackend())
        {
            try
            {
                return CaptureViaWgc();
            }
            catch (Exception ex) when (_config.Backend == CaptureBackendType.Auto)
            {
                _logger?.LogWarning(ex, "Windows Graphics Capture failed; falling back to GDI capture");
                lock (_wgcLock)
                {
                    ResetWgcSessionLocked();
                    _wgcFailed = true;
                }
            }
        }

        return _config.SourceType switch
        {
            CaptureSourceType.Monitor => CaptureMonitor(_config.MonitorIndex),
            CaptureSourceType.Window => CaptureWindow(_config.WindowTitle, _config.WindowId),
            _ => CaptureMonitor(1)
        };
    }

    private bool UseWgcBackend()
    {
        return _config.Backend switch
        {
            CaptureBackendType.LegacyGdi => false,
            CaptureBackendType.WindowsGraphicsCapture => WgcCaptureSession.IsSupported()
                ? true
                : throw new InvalidOperationException(
                    "Windows Graphics Capture is not supported on this system. Switch the capture backend to Auto or Legacy GDI."),
            _ => !_wgcFailed && WgcCaptureSession.IsSupported()
        };
    }

    private Mat CaptureViaWgc()
    {
        lock (_wgcLock)
        {
            var key = _config.SourceType == CaptureSourceType.Window
                ? $"window:{_config.WindowId}|{_config.WindowTitle}|{_config.IncludeCursor}"
                : $"monitor:{_config.MonitorIndex}|{_config.IncludeCursor}";

            if (_wgcSession is { IsDead: true } || (_wgcSession != null && _wgcSessionKey != key))
            {
                ResetWgcSessionLocked();
            }

            if (_wgcSession == null)
            {
                _wgcSession = CreateWgcSession();
                _wgcSessionKey = key;
            }

            return _wgcSession.CaptureFrame();
        }
    }

    private WgcCaptureSession CreateWgcSession()
    {
        if (_config.SourceType == CaptureSourceType.Window)
        {
            var hwnd = ResolveWindowHandle(_config.WindowTitle, _config.WindowId);
            if (hwnd == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Window not found: {_config.WindowTitle}");
            }

            return WgcCaptureSession.CreateForWindow(hwnd, _config.IncludeCursor, _logger);
        }

        var hMonitor = GetMonitorHandle(_config.MonitorIndex);
        if (hMonitor == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Monitor {_config.MonitorIndex} not found");
        }

        return WgcCaptureSession.CreateForMonitor(hMonitor, _config.IncludeCursor, _logger);
    }

    private void ResetWgcSessionLocked()
    {
        _wgcSession?.Dispose();
        _wgcSession = null;
        _wgcSessionKey = null;
    }

    /// <summary>
    /// Gets the HMONITOR for a 1-based monitor index, using the same
    /// enumeration order as <see cref="GetMonitors"/>.
    /// </summary>
    private static IntPtr GetMonitorHandle(int monitorIndex)
    {
        var handles = new List<IntPtr>();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            handles.Add(hMonitor);
            return true;
        }, IntPtr.Zero);

        if (handles.Count == 0) return IntPtr.Zero;

        var index = Math.Clamp(monitorIndex - 1, 0, handles.Count - 1);
        return handles[index];
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
    public Mat CaptureWindow(string? windowTitle, string? windowId = null)
    {
        try
        {
            IntPtr hwnd = ResolveWindowHandle(windowTitle, windowId);

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

    private static IntPtr ResolveWindowHandle(string? windowTitle, string? windowId)
    {
        IntPtr hwnd = ParseWindowId(windowId);

        if (hwnd == IntPtr.Zero && !string.IsNullOrEmpty(windowTitle))
        {
            hwnd = FindWindow(null, windowTitle);
        }

        return hwnd;
    }

    private static IntPtr ParseWindowId(string? windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId)) return IntPtr.Zero;

        var trimmed = windowId.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..];
        }

        if (long.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out var hexValue))
        {
            return new IntPtr(hexValue);
        }

        if (long.TryParse(trimmed, out var decValue))
        {
            return new IntPtr(decValue);
        }

        return IntPtr.Zero;
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
                _ = SelectObject(hdcMem, hOld);
            if (hBitmap != IntPtr.Zero)
                _ = DeleteObject(hBitmap);
            if (hdcMem != IntPtr.Zero)
                _ = DeleteDC(hdcMem);
            if (hdcScreen != IntPtr.Zero)
                _ = ReleaseDC(IntPtr.Zero, hdcScreen);
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

        lock (_captureBufferLock)
        {
            if (_captureBuffer == null || _captureBuffer.Width != width || _captureBuffer.Height != height)
            {
                _captureBuffer?.Dispose();
                _captureBuffer = new Mat(height, width, DepthType.Cv8U, 4);
            }

            IntPtr hdc = GetDC(IntPtr.Zero);
            try
            {
                int result = GetDIBits(hdc, hBitmap, 0, (uint)height, _captureBuffer.DataPointer, ref bmi, DIB_RGB_COLORS);
                if (result == 0) throw new InvalidOperationException("GetDIBits failed");
            }
            finally { _ = ReleaseDC(IntPtr.Zero, hdc); }

            // Return a lightweight header; the reusable backing Mat remains owned here.
            return new Mat(_captureBuffer, new System.Drawing.Rectangle(0, 0, width, height));
        }
    }

    /// <summary>
    /// Applies region filtering to a screenshot.
    /// </summary>
    public Mat ApplyRegionFilter(Mat screenshot, RegionConfig regionConfig)
    {
        if (regionConfig.Type == RegionType.FullScreen)
        {
            return new Mat(screenshot, new System.Drawing.Rectangle(0, 0, screenshot.Width, screenshot.Height));
        }

        if (regionConfig.Type == RegionType.Custom && regionConfig.CustomRegion != null)
        {
            var region = regionConfig.GetCustomRegionFor(screenshot.Width, screenshot.Height)!;
            return ApplyCustomRegion(screenshot, region);
        }

        if (regionConfig.Type == RegionType.Grid)
        {
            return ApplyGridSections(screenshot, regionConfig.GridSections);
        }

        return new Mat(screenshot, new System.Drawing.Rectangle(0, 0, screenshot.Width, screenshot.Height));
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
            return new Mat(screenshot, new System.Drawing.Rectangle(0, 0, screenshot.Width, screenshot.Height));
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
            return new Mat(screenshot, new System.Drawing.Rectangle(0, 0, screenshot.Width, screenshot.Height));
        }

        var result = new Mat(screenshot.Size, screenshot.Depth, screenshot.NumberOfChannels);
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

        CvInvoke.BitwiseAnd(screenshot, screenshot, result, mask);
        return result;
    }

    public void Dispose()
    {
        lock (_wgcLock)
        {
            ResetWgcSessionLocked();
        }

        lock (_captureBufferLock)
        {
            _captureBuffer?.Dispose();
            _captureBuffer = null;
        }
    }

    /// <summary>
    /// Verifies the process is per-monitor DPI aware. When it is not, Windows
    /// virtualizes coordinates on scaled displays (125%/150% etc.), so captures
    /// come out scaled and saved regions/templates silently stop matching.
    /// The host app manifest declares PerMonitorV2; this catches regressions.
    /// </summary>
    private static string? CheckDpiAwareness(ILogger? logger)
    {
        try
        {
            var context = GetThreadDpiAwarenessContext();
            var awareness = GetAwarenessFromDpiAwarenessContext(context);

            if (awareness == DPI_AWARENESS_PER_MONITOR_AWARE) return null;

            var level = awareness switch
            {
                DPI_AWARENESS_SYSTEM_AWARE => "system DPI aware only",
                DPI_AWARENESS_UNAWARE => "not DPI aware",
                _ => "of unknown DPI awareness"
            };

            var warning =
                $"The host process is {level}. On displays with scaling other than 100%, " +
                "captures are scaled by Windows and detection regions/templates may not line up with the screen.";

            logger?.LogWarning("{DpiWarning}", warning);
            return warning;
        }
        catch
        {
            // GetThreadDpiAwarenessContext requires Windows 10 1607+.
            return null;
        }
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
            return
            [
                new MonitorInfo
                {
                    Index = 1,
                    Name = "Primary",
                    IsPrimary = true,
                    Resolution = new Resolution(fallback.Width, fallback.Height),
                    Position = new Models.Point(fallback.Left, fallback.Top)
                }
            ];
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
            _ = GetWindowText(hwnd, builder, builder.Capacity);
            var title = builder.ToString();

            if (string.IsNullOrWhiteSpace(title)) return true;

            string processName = "";
            try
            {
                _ = GetWindowThreadProcessId(hwnd, out uint processId);
                var process = System.Diagnostics.Process.GetProcessById((int)processId);
                processName = process.ProcessName;
            }
            catch { }

            windows.Add(new WindowInfo
            {
                Id = $"0x{hwnd.ToInt64():X}",
                Title = title,
                ProcessName = processName,
                IsVisible = true
            });

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    /// <summary>
    /// Gets the currently focused foreground window info.
    /// </summary>
    public WindowInfo? GetForegroundWindowInfo()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            int length = GetWindowTextLength(hwnd);
            var builder = new System.Text.StringBuilder(Math.Max(1, length + 1));
            _ = GetWindowText(hwnd, builder, builder.Capacity);
            var title = builder.ToString();

            string processName = string.Empty;
            try
            {
                _ = GetWindowThreadProcessId(hwnd, out uint processId);
                if (processId != 0)
                {
                    var process = System.Diagnostics.Process.GetProcessById((int)processId);
                    processName = process.ProcessName;
                }
            }
            catch
            {
                // Ignore process lookup failures.
            }

            return new WindowInfo
            {
                Id = $"0x{hwnd.ToInt64():X}",
                Title = title,
                ProcessName = processName,
                IsVisible = IsWindowVisible(hwnd)
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Checks whether the focused foreground window matches the required process/title.
    /// Process uses exact case-insensitive match.
    /// Title uses case-insensitive contains match.
    /// </summary>
    public bool IsRequiredWindowFocused(string? requiredProcessName, string? requiredTitle)
    {
        if (string.IsNullOrWhiteSpace(requiredProcessName) && string.IsNullOrWhiteSpace(requiredTitle))
            return true;

        var fg = GetForegroundWindowInfo();
        if (fg == null) return false;

        if (!string.IsNullOrWhiteSpace(requiredProcessName))
        {
            if (!string.Equals(fg.ProcessName, requiredProcessName, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrWhiteSpace(requiredTitle))
        {
            if (string.IsNullOrWhiteSpace(fg.Title)) return false;
            if (!fg.Title.Contains(requiredTitle, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
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
        IntPtr lpvBits, ref BITMAPINFO lpbi, uint uUsage);

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

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    // DPI awareness (Windows 10 1607+)
    private const int DPI_AWARENESS_UNAWARE = 0;
    private const int DPI_AWARENESS_SYSTEM_AWARE = 1;
    private const int DPI_AWARENESS_PER_MONITOR_AWARE = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr dpiContext);

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
