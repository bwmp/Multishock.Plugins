#if !WINDOWS && !MACCATALYST
using Emgu.CV;
using ImageDetection.Models;
using ImageDetection.Services.Linux;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;

namespace ImageDetection.Services;

/// <summary>
/// Linux capture. X11 sessions read the screen directly; Wayland sessions go through the
/// xdg-desktop-portal screen-cast API. Window listing, focus checks and window capture use X11,
/// which on Wayland covers XWayland clients (including games running through Wine/Proton).
/// </summary>
public sealed class LinuxScreenCaptureService : IScreenCaptureService, IScreenCaptureAccess, IDisposable
{
    // A live screen cast keeps the desktop's "screen is being shared" indicator on, so the
    // session is dropped once detection stops asking for frames.
    private static readonly TimeSpan PortalIdleTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogger<LinuxScreenCaptureService>? _logger;
    private readonly string _restoreTokenPath;
    private readonly bool _isWayland;
    private readonly X11Capture? _x11;
    private readonly DesktopScreenCaptureService _commandLineCapture;
    private readonly Lazy<bool> _portalAvailable;
    private readonly object _portalSync = new();
    private readonly Timer _idleTimer;
    private ScreenCastPortal? _portal;
    private PipeWireScreenStream? _stream;
    private long _lastPortalUse;
    private string? _portalRefusal;
    private CaptureConfig _config = new();

    public LinuxScreenCaptureService(IPluginHost host, ILogger<LinuxScreenCaptureService>? logger = null)
    {
        _logger = logger;
        _restoreTokenPath = Path.Combine(host.GetPluginDataPath(ImageDetectionPlugin.PluginId), "screencast-restore-token");
        _isWayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) ||
            string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase);
        _x11 = X11Capture.TryOpen(logger);
        _commandLineCapture = new DesktopScreenCaptureService(logger);
        _portalAvailable = new Lazy<bool>(IsPortalAvailable);
        _idleTimer = new Timer(_ => ReleaseIdlePortal(), null, PortalIdleTimeout, PortalIdleTimeout);
    }

    public bool IsSupported => _isWayland
        ? _portalAvailable.Value || _commandLineCapture.IsSupported ||
          (Config.SourceType == CaptureSourceType.Window && _x11 is not null)
        : _x11 is not null || _commandLineCapture.IsSupported;

    public string? UnsupportedReason => IsSupported
        ? null
        : _isWayland
            ? "Screen capture on Wayland needs PipeWire and xdg-desktop-portal with a screen-cast backend " +
              "(xdg-desktop-portal-gnome, -kde, -wlr or -hyprland)."
            : "No X display is available for screen capture.";

    public string? DpiWarning => null;

    public bool UsesSystemScreenPicker => _isWayland && _portalAvailable.Value;

    public CaptureConfig Config
    {
        get => _config;
        set => _config = value ?? new CaptureConfig();
    }

    public Mat CaptureScreen() => Config.SourceType switch
    {
        CaptureSourceType.Window => CaptureWindow(Config.WindowTitle, Config.WindowId),
        _ => CaptureMonitor(Config.MonitorIndex)
    };

    public Mat CaptureMonitor(int monitorIndex = 1)
    {
        if (_isWayland)
        {
            return _portalAvailable.Value ? CaptureFromPortal() : _commandLineCapture.CaptureMonitor(monitorIndex);
        }

        return _x11 is not null ? _x11.CaptureMonitor(monitorIndex) : _commandLineCapture.CaptureMonitor(monitorIndex);
    }

    public Mat CaptureWindow(string? windowTitle, string? windowId = null)
    {
        if (_x11 is null)
        {
            throw new NotSupportedException(_isWayland
                ? "Window capture on Wayland works with X11 (XWayland) windows, but XWayland is not available."
                : "No X display is available for window capture.");
        }

        if (X11Capture.TryParseWindowId(windowId, out var window))
        {
            try
            {
                return _x11.CaptureWindow(window);
            }
            catch (InvalidOperationException) when (!string.IsNullOrWhiteSpace(windowTitle))
            {
                // Window ids do not survive the app restarting; fall back to the saved title.
            }
        }

        var match = string.IsNullOrWhiteSpace(windowTitle) ? null : _x11.FindWindowByTitle(windowTitle);
        if (match is null || !X11Capture.TryParseWindowId(match.Id, out window))
        {
            throw new InvalidOperationException(_isWayland
                ? $"Window not found: {windowTitle}. On Wayland only X11 (XWayland) windows can be captured individually."
                : $"Window not found: {windowTitle}");
        }

        return _x11.CaptureWindow(window);
    }

    public Mat ApplyRegionFilter(Mat screenshot, RegionConfig regionConfig) =>
        CaptureRegionFilter.Apply(screenshot, regionConfig);

    public List<MonitorInfo> GetMonitors()
    {
        if (UsesSystemScreenPicker)
        {
            // The portal, not a monitor index, decides what is captured.
            var size = CurrentPortalFrameSize() ?? PrimaryX11Size();
            return
            [
                new MonitorInfo
                {
                    Index = 1,
                    Name = "Screen chosen in the sharing dialog",
                    IsPrimary = true,
                    Resolution = size ?? new Resolution(0, 0),
                    Position = new Models.Point(0, 0)
                }
            ];
        }

        return _x11?.GetMonitors() ?? _commandLineCapture.GetMonitors();
    }

    public Resolution GetCurrentMonitorResolution()
    {
        if (UsesSystemScreenPicker)
        {
            if (CurrentPortalFrameSize() is { } size)
            {
                return size;
            }

            using var frame = CaptureFromPortal();
            return new Resolution(frame.Width, frame.Height);
        }

        if (_x11 is null)
        {
            return _commandLineCapture.GetCurrentMonitorResolution();
        }

        var monitors = _x11.GetMonitors();
        var index = Config.MonitorIndex >= 1 && Config.MonitorIndex <= monitors.Count ? Config.MonitorIndex : 1;
        return monitors[index - 1].Resolution;
    }

    public List<WindowInfo> GetWindows() => _x11?.GetWindows() ?? _commandLineCapture.GetWindows();

    public WindowInfo? GetForegroundWindowInfo() =>
        _x11 is not null ? _x11.GetActiveWindow() : _commandLineCapture.GetForegroundWindowInfo();

    public bool IsRequiredWindowFocused(string? requiredProcessName, string? requiredTitle)
    {
        if (string.IsNullOrWhiteSpace(requiredProcessName) && string.IsNullOrWhiteSpace(requiredTitle))
        {
            return true;
        }

        // On Wayland a native Wayland window has no X11 representation, so it reads as unfocused.
        var foreground = GetForegroundWindowInfo();
        return foreground is not null &&
            (string.IsNullOrWhiteSpace(requiredProcessName) || foreground.ProcessName.Contains(requiredProcessName, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(requiredTitle) || foreground.Title.Contains(requiredTitle, StringComparison.OrdinalIgnoreCase));
    }

    public void ResetScreenChoice()
    {
        lock (_portalSync)
        {
            StopPortal();
            _portalRefusal = null;
            try
            {
                File.Delete(_restoreTokenPath);
            }
            catch (IOException ex)
            {
                _logger?.LogWarning(ex, "Could not delete the screen-cast restore token");
            }
        }
    }

    public void Dispose()
    {
        _idleTimer.Dispose();
        lock (_portalSync)
        {
            StopPortal();
        }

        _x11?.Dispose();
    }

    private bool IsPortalAvailable()
    {
        if (!_isWayland || !PipeWireScreenStream.IsAvailable())
        {
            return false;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return ScreenCastPortal.IsAvailableAsync(_logger, timeout.Token).GetAwaiter().GetResult();
    }

    private Mat CaptureFromPortal()
    {
        lock (_portalSync)
        {
            // Asking again after a refusal would pop the dialog up on every detection loop.
            if (_portalRefusal is not null)
            {
                throw new InvalidOperationException(_portalRefusal);
            }

            if (_stream is null || _stream.Error is not null)
            {
                StartPortal();
            }

            _lastPortalUse = Environment.TickCount64;
            return _stream!.CaptureFrame(FirstFrameTimeout);
        }
    }

    private void StartPortal()
    {
        StopPortal();
        ScreenCastPortal portal;
        try
        {
            portal = ScreenCastPortal.StartAsync(ReadRestoreToken(), _logger, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (ScreenCastDeniedException)
        {
            _portalRefusal = "Screen sharing was declined. Use \"Choose screen\" in Image Detection to be asked again.";
            throw new InvalidOperationException(_portalRefusal);
        }

        try
        {
            SaveRestoreToken(portal.RestoreToken);
            var fd = portal.OpenPipeWireRemoteAsync().GetAwaiter().GetResult();
            _stream = PipeWireScreenStream.Connect(fd, portal.NodeId, _logger);
            _portal = portal;
        }
        catch
        {
            portal.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    private void StopPortal()
    {
        _stream?.Dispose();
        _stream = null;
        if (_portal is not null)
        {
            try
            {
                _portal.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Closing the screen-cast session failed");
            }

            _portal = null;
        }
    }

    private void ReleaseIdlePortal()
    {
        lock (_portalSync)
        {
            if (_stream is not null && Environment.TickCount64 - _lastPortalUse > PortalIdleTimeout.TotalMilliseconds)
            {
                _logger?.LogInformation("Closing the idle screen-cast session");
                StopPortal();
            }
        }
    }

    private Resolution? CurrentPortalFrameSize()
    {
        lock (_portalSync)
        {
            return _stream?.FrameSize is { } size ? new Resolution(size.Width, size.Height) : null;
        }
    }

    private Resolution? PrimaryX11Size()
    {
        var monitors = _x11?.GetMonitors();
        var primary = monitors?.FirstOrDefault(m => m.IsPrimary) ?? monitors?.FirstOrDefault();
        return primary?.Resolution;
    }

    private string? ReadRestoreToken()
    {
        try
        {
            return File.Exists(_restoreTokenPath) ? File.ReadAllText(_restoreTokenPath).Trim() : null;
        }
        catch (IOException ex)
        {
            _logger?.LogWarning(ex, "Could not read the screen-cast restore token");
            return null;
        }
    }

    private void SaveRestoreToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        try
        {
            File.WriteAllText(_restoreTokenPath, token);
        }
        catch (IOException ex)
        {
            _logger?.LogWarning(ex, "Could not save the screen-cast restore token");
        }
    }
}
#endif
