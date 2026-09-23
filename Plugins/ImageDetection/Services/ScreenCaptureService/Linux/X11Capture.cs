#if !WINDOWS && !MACCATALYST
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Emgu.CV;
using Emgu.CV.CvEnum;
using ImageDetection.Models;
using Microsoft.Extensions.Logging;
using static ImageDetection.Services.Linux.X11Native;

namespace ImageDetection.Services.Linux;

/// <summary>
/// Screen, window and window-list access through an X server. On Wayland sessions this talks to
/// XWayland, which still covers X11 clients such as games running through Wine/Proton.
/// </summary>
internal sealed unsafe class X11Capture : IDisposable
{
    private static readonly object ErrorHandlerSync = new();
    private static IntPtr s_previousErrorHandler;
    private static bool s_errorHandlerInstalled;
    private static IntPtr s_ownDisplay;

    private readonly object _sync = new();
    private readonly ILogger? _logger;
    private readonly nuint _root;
    private readonly IntPtr _visual;
    private readonly int _depth;
    private readonly Dictionary<string, nuint> _atoms = [];
    private IntPtr _display;
    private bool _shmAvailable;
    private ShmImage? _shmImage;

    private X11Capture(IntPtr display, ILogger? logger)
    {
        _display = display;
        _logger = logger;
        var screen = XDefaultScreen(display);
        _root = XDefaultRootWindow(display);
        _visual = XDefaultVisual(display, screen);
        _depth = XDefaultDepth(display, screen);
        _shmAvailable = XShmQueryExtension(display) != 0;
    }

    public static X11Capture? TryOpen(ILogger? logger)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            return null;
        }

        try
        {
            var display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
            {
                logger?.LogWarning("Could not open X display {Display}", Environment.GetEnvironmentVariable("DISPLAY"));
                return null;
            }

            InstallErrorHandler(display);
            return new X11Capture(display, logger);
        }
        catch (DllNotFoundException ex)
        {
            logger?.LogWarning(ex, "libX11 is not available");
            return null;
        }
    }

    public List<MonitorInfo> GetMonitors()
    {
        lock (_sync)
        {
            EnsureOpen();
            var monitors = new List<MonitorInfo>();
            try
            {
                var infos = XRRGetMonitors(_display, _root, 1, out var count);
                if (infos != null)
                {
                    for (var i = 0; i < count; i++)
                    {
                        var info = infos[i];
                        monitors.Add(new MonitorInfo
                        {
                            Index = i + 1,
                            Name = GetAtomName(info.Name) ?? $"Monitor {i + 1}",
                            IsPrimary = info.Primary != 0,
                            Resolution = new Resolution(info.Width, info.Height),
                            Position = new Models.Point(info.X, info.Y)
                        });
                    }

                    XRRFreeMonitors(infos);
                }
            }
            catch (DllNotFoundException ex)
            {
                _logger?.LogDebug(ex, "libXrandr is not available; treating the X screen as one monitor");
            }

            if (monitors.Count == 0)
            {
                var screen = XDefaultScreen(_display);
                monitors.Add(new MonitorInfo
                {
                    Index = 1,
                    Name = "Screen",
                    IsPrimary = true,
                    Resolution = new Resolution(XDisplayWidth(_display, screen), XDisplayHeight(_display, screen)),
                    Position = new Models.Point(0, 0)
                });
            }

            return monitors;
        }
    }

    public Mat CaptureMonitor(int monitorIndex)
    {
        var monitors = GetMonitors();
        if (monitorIndex < 1 || monitorIndex > monitors.Count)
        {
            monitorIndex = 1;
        }

        var monitor = monitors[monitorIndex - 1];
        return CaptureRootRegion(monitor.Position.X, monitor.Position.Y, monitor.Resolution.Width, monitor.Resolution.Height);
    }

    public Mat CaptureRootRegion(int x, int y, int width, int height)
    {
        lock (_sync)
        {
            EnsureOpen();
            if (_shmAvailable)
            {
                try
                {
                    return CaptureWithShm(x, y, width, height);
                }
                catch (InvalidOperationException ex)
                {
                    // MIT-SHM is unusable across IPC namespaces (containers, some sandboxes).
                    _logger?.LogInformation("MIT-SHM capture failed, using XGetImage instead: {Message}", ex.Message);
                    _shmAvailable = false;
                    _shmImage?.Dispose();
                    _shmImage = null;
                }
            }

            return CaptureWithGetImage(_root, x, y, width, height);
        }
    }

    public Mat CaptureWindow(nuint window)
    {
        lock (_sync)
        {
            EnsureOpen();
            BeginErrorTrap();
            var status = XGetWindowAttributes(_display, window, out var attributes);
            if (status == 0 || EndErrorTrap() != 0)
            {
                throw new InvalidOperationException($"X11 window 0x{window:x} no longer exists.");
            }

            if (attributes.MapState != IsViewable)
            {
                throw new InvalidOperationException("The selected window is minimized or hidden, so it cannot be captured.");
            }

            // A window's own contents stay readable while it is covered, as long as a
            // compositor redirects it (every modern desktop and XWayland does).
            return CaptureWithGetImage(window, 0, 0, attributes.Width, attributes.Height);
        }
    }

    public List<WindowInfo> GetWindows()
    {
        lock (_sync)
        {
            EnsureOpen();
            var windows = new List<WindowInfo>();
            var clients = ReadWindowList(_root, "_NET_CLIENT_LIST");
            if (clients.Count == 0)
            {
                // Not every window manager publishes EWMH (WSLg's Weston, bare X11 WMs).
                clients = FindClientWindows();
            }

            foreach (var window in clients)
            {
                var info = DescribeWindow(window);
                if (info is not null && !string.IsNullOrWhiteSpace(info.Title))
                {
                    windows.Add(info);
                }
            }

            return windows;
        }
    }

    public WindowInfo? GetActiveWindow()
    {
        lock (_sync)
        {
            EnsureOpen();
            var active = ReadWindowList(_root, "_NET_ACTIVE_WINDOW").FirstOrDefault();
            return active == 0 ? null : DescribeWindow(active);
        }
    }

    public WindowInfo? FindWindowByTitle(string title) =>
        GetWindows().FirstOrDefault(w => w.Title.Equals(title, StringComparison.Ordinal))
        ?? GetWindows().FirstOrDefault(w => w.Title.Contains(title, StringComparison.OrdinalIgnoreCase));

    public static bool TryParseWindowId(string? id, out nuint window)
    {
        window = 0;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var text = id.Trim();
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? nuint.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out window) && window != 0
            : nuint.TryParse(text, out window) && window != 0;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_display == IntPtr.Zero)
            {
                return;
            }

            _shmImage?.Dispose();
            _shmImage = null;
            lock (ErrorHandlerSync)
            {
                if (s_ownDisplay == _display)
                {
                    s_ownDisplay = IntPtr.Zero;
                }
            }

            XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }
    }

    private Mat CaptureWithShm(int x, int y, int width, int height)
    {
        if (_shmImage is null || _shmImage.Width != width || _shmImage.Height != height)
        {
            _shmImage?.Dispose();
            _shmImage = ShmImage.Create(_display, _visual, _depth, width, height);
        }

        BeginErrorTrap();
        var ok = XShmGetImage(_display, _root, _shmImage.Image, x, y, AllPlanes) != 0;
        XSync(_display, 0);
        if (!ok || EndErrorTrap() != 0)
        {
            throw new InvalidOperationException("XShmGetImage failed.");
        }

        return ToMat(_shmImage.Image);
    }

    private Mat CaptureWithGetImage(nuint drawable, int x, int y, int width, int height)
    {
        BeginErrorTrap();
        var image = XGetImage(_display, drawable, x, y, (uint)width, (uint)height, AllPlanes, ZPixmap);
        var error = EndErrorTrap();
        if (image == null || error != 0)
        {
            throw new InvalidOperationException($"XGetImage failed (X error {error}).");
        }

        try
        {
            return ToMat(image);
        }
        finally
        {
            XDestroyImage(image);
        }
    }

    private static Mat ToMat(XImage* image)
    {
        if (image->BitsPerPixel != 32)
        {
            throw new NotSupportedException($"Unsupported X11 pixel format ({image->BitsPerPixel} bits per pixel).");
        }

        var mat = new Mat(image->Height, image->Width, DepthType.Cv8U, 4);
        var rowBytes = image->Width * 4;
        var destination = (byte*)mat.DataPointer;
        for (var row = 0; row < image->Height; row++)
        {
            Buffer.MemoryCopy(image->Data + (long)row * image->BytesPerLine, destination + (long)row * mat.Step, mat.Step, rowBytes);
        }

        // 32bpp X visuals are almost always BGRx in memory; swap if the server says otherwise.
        if (image->RedMask == 0xff && image->BlueMask == 0xff0000)
        {
            CvInvoke.CvtColor(mat, mat, ColorConversion.Rgba2Bgra);
        }

        return mat;
    }

    private WindowInfo? DescribeWindow(nuint window)
    {
        var title = ReadStringProperty(window, "_NET_WM_NAME", Atom("UTF8_STRING"))
            ?? ReadStringProperty(window, XaWmName, XaString);
        if (title is null)
        {
            return null;
        }

        var pid = ReadCardinal(window, "_NET_WM_PID");
        return new WindowInfo
        {
            Id = $"0x{window:x}",
            Title = title,
            ProcessName = pid is > 0 ? ReadProcessName((int)pid.Value) : string.Empty,
            IsVisible = true
        };
    }

    private List<nuint> ReadWindowList(nuint window, string property)
    {
        var result = new List<nuint>();
        if (!TryReadProperty(window, Atom(property), XaWindow, out var data, out var format, out var count))
        {
            return result;
        }

        try
        {
            if (format == 32)
            {
                // Format-32 properties are returned as arrays of C longs.
                var items = (nuint*)data;
                for (nuint i = 0; i < count; i++)
                {
                    result.Add(items[i]);
                }
            }
        }
        finally
        {
            XFree(data);
        }

        return result;
    }

    /// <summary>
    /// Top-level application windows are the ones carrying WM_STATE, either directly under the
    /// root or one level down inside a window manager's frame.
    /// </summary>
    private List<nuint> FindClientWindows()
    {
        var clients = new List<nuint>();
        var wmState = Atom("WM_STATE");
        foreach (var topLevel in QueryChildren(_root))
        {
            if (HasProperty(topLevel, wmState))
            {
                clients.Add(topLevel);
                continue;
            }

            clients.AddRange(QueryChildren(topLevel).Where(child => HasProperty(child, wmState)));
        }

        return clients.Where(IsMapped).ToList();
    }

    private List<nuint> QueryChildren(nuint window)
    {
        var result = new List<nuint>();
        BeginErrorTrap();
        var status = XQueryTree(_display, window, out _, out _, out var children, out var count);
        if (status == 0 || EndErrorTrap() != 0 || children == null)
        {
            return result;
        }

        for (var i = 0; i < count; i++)
        {
            result.Add(children[i]);
        }

        XFree(children);
        return result;
    }

    private bool HasProperty(nuint window, nuint property)
    {
        if (!TryReadProperty(window, property, AnyPropertyType, out var data, out _, out _))
        {
            return false;
        }

        XFree(data);
        return true;
    }

    private bool IsMapped(nuint window)
    {
        BeginErrorTrap();
        var status = XGetWindowAttributes(_display, window, out var attributes);
        return status != 0 && EndErrorTrap() == 0 && attributes.MapState == IsViewable;
    }

    private uint? ReadCardinal(nuint window, string property)
    {
        if (!TryReadProperty(window, Atom(property), XaCardinal, out var data, out var format, out var count))
        {
            return null;
        }

        try
        {
            return format == 32 && count > 0 ? (uint)*(nuint*)data : null;
        }
        finally
        {
            XFree(data);
        }
    }

    private string? ReadStringProperty(nuint window, string property, nuint type) =>
        ReadStringProperty(window, Atom(property), type);

    private string? ReadStringProperty(nuint window, nuint property, nuint type)
    {
        if (!TryReadProperty(window, property, type, out var data, out var format, out var count))
        {
            return null;
        }

        try
        {
            return format == 8 && count > 0 ? Encoding.UTF8.GetString(data, (int)count).TrimEnd('\0') : null;
        }
        finally
        {
            XFree(data);
        }
    }

    private bool TryReadProperty(nuint window, nuint property, nuint type, out byte* data, out int format, out nuint count)
    {
        BeginErrorTrap();
        var status = XGetWindowProperty(_display, window, property, 0, 1 << 16, 0, type,
            out _, out format, out count, out _, out data);
        var error = EndErrorTrap();
        if (status != 0 || error != 0 || data == null)
        {
            if (data != null)
            {
                XFree(data);
                data = null;
            }

            return false;
        }

        return true;
    }

    private nuint Atom(string name)
    {
        if (!_atoms.TryGetValue(name, out var atom))
        {
            atom = XInternAtom(_display, name, 0);
            _atoms[name] = atom;
        }

        return atom;
    }

    private string? GetAtomName(nuint atom)
    {
        if (atom == 0)
        {
            return null;
        }

        var name = XGetAtomName(_display, atom);
        if (name == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(name);
        }
        finally
        {
            XFree((void*)name);
        }
    }

    private static string ReadProcessName(int pid)
    {
        try
        {
            return File.ReadAllText($"/proc/{pid}/comm").Trim();
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private void EnsureOpen()
    {
        ObjectDisposedException.ThrowIf(_display == IntPtr.Zero, this);
    }

    private void BeginErrorTrap()
    {
        XSync(_display, 0);
        Interlocked.Exchange(ref s_lastErrorCode, 0);
    }

    private int EndErrorTrap()
    {
        XSync(_display, 0);
        return Interlocked.Exchange(ref s_lastErrorCode, 0);
    }

    private static int s_lastErrorCode;

    // Xlib's default error handler terminates the process, so errors on this connection must be
    // handled here. Errors on other connections (the host's) go to whichever handler was there before.
    private static void InstallErrorHandler(IntPtr display)
    {
        lock (ErrorHandlerSync)
        {
            s_ownDisplay = display;
            if (s_errorHandlerInstalled)
            {
                return;
            }

            delegate* unmanaged[Cdecl]<IntPtr, XErrorEvent*, int> handler = &OnXError;
            s_previousErrorHandler = XSetErrorHandler((IntPtr)handler);
            s_errorHandlerInstalled = true;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnXError(IntPtr display, XErrorEvent* error)
    {
        if (display == Volatile.Read(ref s_ownDisplay))
        {
            Interlocked.Exchange(ref s_lastErrorCode, error->ErrorCode);
            return 0;
        }

        var previous = (delegate* unmanaged[Cdecl]<IntPtr, XErrorEvent*, int>)s_previousErrorHandler;
        return previous != null ? previous(display, error) : 0;
    }

    private sealed class ShmImage : IDisposable
    {
        private readonly IntPtr _display;
        private XShmSegmentInfo* _segment;

        private ShmImage(IntPtr display, XImage* image, XShmSegmentInfo* segment, int width, int height)
        {
            _display = display;
            Image = image;
            _segment = segment;
            Width = width;
            Height = height;
        }

        public XImage* Image { get; private set; }

        public int Width { get; }

        public int Height { get; }

        public static ShmImage Create(IntPtr display, IntPtr visual, int depth, int width, int height)
        {
            var segment = (XShmSegmentInfo*)NativeMemory.AllocZeroed((nuint)sizeof(XShmSegmentInfo));
            var image = XShmCreateImage(display, visual, (uint)depth, ZPixmap, null, segment, (uint)width, (uint)height);
            if (image == null)
            {
                NativeMemory.Free(segment);
                throw new InvalidOperationException("XShmCreateImage failed.");
            }

            segment->ShmId = shmget(IpcPrivate, (nuint)((long)image->BytesPerLine * image->Height), IpcCreat | 0x180);
            if (segment->ShmId < 0)
            {
                XDestroyImage(image);
                NativeMemory.Free(segment);
                throw new InvalidOperationException($"shmget failed (errno {Marshal.GetLastPInvokeError()}).");
            }

            segment->ShmAddr = shmat(segment->ShmId, IntPtr.Zero, 0);
            if (segment->ShmAddr == (byte*)-1)
            {
                shmctl(segment->ShmId, IpcRmid, IntPtr.Zero);
                XDestroyImage(image);
                NativeMemory.Free(segment);
                throw new InvalidOperationException($"shmat failed (errno {Marshal.GetLastPInvokeError()}).");
            }

            image->Data = segment->ShmAddr;
            segment->ReadOnly = 0;

            Interlocked.Exchange(ref s_lastErrorCode, 0);
            var attached = XShmAttach(display, segment) != 0;
            XSync(display, 0);

            // Marked for removal now; the kernel frees it once both sides have detached.
            shmctl(segment->ShmId, IpcRmid, IntPtr.Zero);

            var shm = new ShmImage(display, image, segment, width, height);
            if (!attached || Interlocked.Exchange(ref s_lastErrorCode, 0) != 0)
            {
                shm.Dispose();
                throw new InvalidOperationException("XShmAttach failed.");
            }

            return shm;
        }

        public void Dispose()
        {
            if (Image == null)
            {
                return;
            }

            XShmDetach(_display, _segment);
            XSync(_display, 0);
            shmdt(_segment->ShmAddr);

            // XDestroyImage frees Data with free(); it belongs to the shared-memory segment instead.
            Image->Data = null;
            XDestroyImage(Image);
            Image = null;
            NativeMemory.Free(_segment);
            _segment = null;
        }
    }
}
#endif
