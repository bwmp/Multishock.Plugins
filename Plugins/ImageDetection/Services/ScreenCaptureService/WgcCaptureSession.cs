using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Microsoft.Extensions.Logging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Foundation.Metadata;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace ImageDetection.Services;

/// <summary>
/// A Windows Graphics Capture (WinRT) frame source for a single monitor or
/// window. Unlike GDI BitBlt, WGC captures hardware-accelerated and
/// fullscreen-exclusive content, is not affected by windows overlapping the
/// target, and copies frames on the GPU.
///
/// WGC is push-based: frames arrive in the pool only when the content changes.
/// <see cref="CaptureFrame"/> therefore returns the most recent frame, reusing
/// the last one when the screen is static.
/// </summary>
internal sealed class WgcCaptureSession : IDisposable
{
    // IID of Windows.Graphics.Capture.GraphicsCaptureItem
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    private readonly ILogger? _logger;
    private readonly object _sync = new();

    private ID3D11Device? _d3dDevice;
    private ID3D11DeviceContext? _d3dContext;
    private IDirect3DDevice? _winrtDevice;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private ID3D11Texture2D? _staging;
    private int _stagingWidth;
    private int _stagingHeight;
    private SizeInt32 _poolSize;
    private Mat? _lastFrame;
    private volatile bool _itemClosed;

    /// <summary>
    /// True when the capture target has gone away (e.g. the captured window
    /// was closed). The owner should dispose and recreate the session.
    /// </summary>
    public bool IsDead => _itemClosed;

    private WgcCaptureSession(ILogger? logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Whether Windows Graphics Capture is available on this system.
    /// </summary>
    public static bool IsSupported()
    {
        try
        {
            return ApiInformation.IsTypePresent("Windows.Graphics.Capture.GraphicsCaptureSession")
                && GraphicsCaptureSession.IsSupported();
        }
        catch
        {
            return false;
        }
    }

    public static WgcCaptureSession CreateForMonitor(IntPtr monitorHandle, bool includeCursor, ILogger? logger = null)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var itemAbi = interop.CreateForMonitor(monitorHandle, GraphicsCaptureItemGuid);
        return Create(itemAbi, includeCursor, logger);
    }

    public static WgcCaptureSession CreateForWindow(IntPtr windowHandle, bool includeCursor, ILogger? logger = null)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var itemAbi = interop.CreateForWindow(windowHandle, GraphicsCaptureItemGuid);
        return Create(itemAbi, includeCursor, logger);
    }

    private static WgcCaptureSession Create(IntPtr itemAbi, bool includeCursor, ILogger? logger)
    {
        GraphicsCaptureItem item;
        try
        {
            item = GraphicsCaptureItem.FromAbi(itemAbi);
        }
        finally
        {
            Marshal.Release(itemAbi);
        }

        var session = new WgcCaptureSession(logger);
        try
        {
            session.Initialize(item, includeCursor);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private void Initialize(GraphicsCaptureItem item, bool includeCursor)
    {
        _item = item;
        _item.Closed += OnItemClosed;

        var result = D3D11.D3D11CreateDevice(
            null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, null, out ID3D11Device? device);
        if (result.Failure || device == null)
        {
            D3D11.D3D11CreateDevice(
                null, DriverType.Warp, DeviceCreationFlags.BgraSupport, null, out device).CheckError();
        }

        _d3dDevice = device!;
        _d3dContext = _d3dDevice.ImmediateContext;

        using (var dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>())
        {
            var hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var winrtDevicePtr);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                _winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(winrtDevicePtr);
            }
            finally
            {
                Marshal.Release(winrtDevicePtr);
            }
        }

        _poolSize = _item.Size;
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
        _session = _framePool.CreateCaptureSession(_item);

        TrySetCursorCapture(_session, includeCursor);
        TryDisableBorder(_session);

        _session.StartCapture();
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args)
    {
        _itemClosed = true;
    }

    private static void TrySetCursorCapture(GraphicsCaptureSession session, bool includeCursor)
    {
        try
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
                && ApiInformation.IsPropertyPresent(
                    "Windows.Graphics.Capture.GraphicsCaptureSession", "IsCursorCaptureEnabled"))
            {
                session.IsCursorCaptureEnabled = includeCursor;
            }
        }
        catch
        {
            // Cursor toggle unavailable; frames still arrive.
        }
    }

    private void TryDisableBorder(GraphicsCaptureSession session)
    {
        try
        {
            if (ApiInformation.IsPropertyPresent(
                "Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
            {
                session.IsBorderRequired = false;
            }
        }
        catch (Exception ex)
        {
            // Windows 10 (and unprivileged apps on Windows 11) keep the OS
            // capture border; capture itself is unaffected.
            _logger?.LogDebug(ex, "Could not disable the capture border");
        }
    }

    /// <summary>
    /// Returns the newest available frame as a BGRA Mat (caller owns it).
    /// When the content has not changed since the previous call, the last
    /// frame is returned again.
    /// </summary>
    public Mat CaptureFrame(int timeoutMs = 250)
    {
        lock (_sync)
        {
            if (_itemClosed)
                throw new InvalidOperationException("The capture target has been closed.");

            var framePool = _framePool ?? throw new ObjectDisposedException(nameof(WgcCaptureSession));

            var frame = DrainLatestFrame(framePool);

            // No frame yet: for a brand-new session wait briefly for the first
            // one; otherwise the content is simply unchanged.
            if (frame == null && _lastFrame == null)
            {
                var deadline = Environment.TickCount64 + timeoutMs;
                while (frame == null && Environment.TickCount64 < deadline)
                {
                    Thread.Sleep(5);
                    frame = DrainLatestFrame(framePool);
                }
            }

            if (frame != null)
            {
                using (frame)
                {
                    var contentSize = frame.ContentSize;
                    if (contentSize.Width != _poolSize.Width || contentSize.Height != _poolSize.Height)
                    {
                        // Target was resized: recreate the pool for the next frame.
                        _poolSize = contentSize;
                        framePool.Recreate(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, contentSize);
                    }

                    var mat = CopyFrameToMat(frame);
                    _lastFrame?.Dispose();
                    _lastFrame = mat;
                }
            }

            if (_lastFrame == null)
                throw new InvalidOperationException("No frame received from Windows Graphics Capture.");

            return _lastFrame.Clone();
        }
    }

    private static Direct3D11CaptureFrame? DrainLatestFrame(Direct3D11CaptureFramePool framePool)
    {
        Direct3D11CaptureFrame? latest = null;

        while (true)
        {
            var next = framePool.TryGetNextFrame();
            if (next == null) break;
            latest?.Dispose();
            latest = next;
        }

        return latest;
    }

    private Mat CopyFrameToMat(Direct3D11CaptureFrame frame)
    {
        var access = frame.Surface.As<IDirect3DDxgiInterfaceAccess>();
        var textureGuid = typeof(ID3D11Texture2D).GUID;
        var texturePtr = access.GetInterface(textureGuid);

        using var texture = new ID3D11Texture2D(texturePtr);
        var description = texture.Description;

        int width = Math.Min(frame.ContentSize.Width, (int)description.Width);
        int height = Math.Min(frame.ContentSize.Height, (int)description.Height);
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("Captured frame has invalid dimensions.");

        EnsureStagingTexture((int)description.Width, (int)description.Height, description.Format);

        _d3dContext!.CopyResource(_staging!, texture);
        var mapped = _d3dContext.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var mat = new Mat(height, width, DepthType.Cv8U, 4);
            unsafe
            {
                var src = (byte*)mapped.DataPointer;
                var dst = (byte*)mat.DataPointer;
                long rowBytes = (long)width * 4;
                for (int y = 0; y < height; y++)
                {
                    Buffer.MemoryCopy(
                        src + (long)y * mapped.RowPitch,
                        dst + (long)y * mat.Step,
                        rowBytes, rowBytes);
                }
            }
            return mat;
        }
        finally
        {
            _d3dContext.Unmap(_staging!, 0);
        }
    }

    private void EnsureStagingTexture(int width, int height, Format format)
    {
        if (_staging != null && _stagingWidth == width && _stagingHeight == height) return;

        _staging?.Dispose();
        _staging = _d3dDevice!.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None
        });
        _stagingWidth = width;
        _stagingHeight = height;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            try { _session?.Dispose(); } catch { }
            try { _framePool?.Dispose(); } catch { }

            if (_item != null)
            {
                try { _item.Closed -= OnItemClosed; } catch { }
            }

            _staging?.Dispose();
            _d3dContext?.Dispose();
            _d3dDevice?.Dispose();
            try { _winrtDevice?.Dispose(); } catch { }
            _lastFrame?.Dispose();

            _session = null;
            _framePool = null;
            _item = null;
            _staging = null;
            _d3dContext = null;
            _d3dDevice = null;
            _winrtDevice = null;
            _lastFrame = null;
        }
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [ComImport]
    [System.Runtime.InteropServices.Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] in Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, [In] in Guid iid);
    }

    [ComImport]
    [System.Runtime.InteropServices.Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] in Guid iid);
    }
}
