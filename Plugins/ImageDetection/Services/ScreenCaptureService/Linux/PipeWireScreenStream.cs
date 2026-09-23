#if !WINDOWS && !MACCATALYST
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Microsoft.Extensions.Logging;
using static ImageDetection.Services.Linux.PipeWireNative;

namespace ImageDetection.Services.Linux;

/// <summary>
/// Receives a screen-cast node from PipeWire and keeps the most recent frame.
/// </summary>
internal sealed unsafe class PipeWireScreenStream : IDisposable
{
    // Copying every frame of a 60 Hz 4K stream would cost ~2 GB/s; detection never needs more than this.
    private static readonly long MinCopyIntervalTicks = TimeSpan.FromMilliseconds(30).Ticks;

    private readonly ILogger? _logger;
    private readonly object _frameSync = new();
    private readonly ManualResetEventSlim _firstFrame = new();
    private GCHandle _self;
    private IntPtr _loop;
    private IntPtr _context;
    private IntPtr _core;
    private IntPtr _stream;
    private void* _events;
    private void* _hook;
    private int _width;
    private int _height;
    private uint _format;
    private byte[]? _frame;
    private int _frameWidth;
    private int _frameHeight;
    private uint _frameFormat;
    private long _lastCopyTicks;
    private volatile string? _error;
    private bool _warnedUnmappable;

    private PipeWireScreenStream(ILogger? logger)
    {
        _logger = logger;
    }

    public string? Error => _error;

    public (int Width, int Height)? FrameSize
    {
        get
        {
            lock (_frameSync)
            {
                return _frame is null ? null : (_frameWidth, _frameHeight);
            }
        }
    }

    public static bool IsAvailable()
    {
        try
        {
            pw_get_library_version();
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    /// <param name="remoteFd">PipeWire socket from the portal. Ownership passes to PipeWire.</param>
    public static PipeWireScreenStream Connect(int remoteFd, uint nodeId, ILogger? logger)
    {
        var stream = new PipeWireScreenStream(logger);
        try
        {
            stream.Start(remoteFd, nodeId);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Returns the latest frame as a BGRA image, waiting for the first one if necessary.
    /// </summary>
    public Mat CaptureFrame(TimeSpan firstFrameTimeout)
    {
        if (!_firstFrame.Wait(firstFrameTimeout))
        {
            throw new TimeoutException(_error ?? "The screen-cast stream did not deliver a frame in time.");
        }

        lock (_frameSync)
        {
            if (_frame is null)
            {
                throw new InvalidOperationException(_error ?? "The screen-cast stream has no frame.");
            }

            var mat = new Mat(_frameHeight, _frameWidth, DepthType.Cv8U, 4);
            Marshal.Copy(_frame, 0, mat.DataPointer, _frameWidth * _frameHeight * 4);
            if (_frameFormat is VideoFormatRgbx or VideoFormatRgba)
            {
                CvInvoke.CvtColor(mat, mat, ColorConversion.Rgba2Bgra);
            }

            return mat;
        }
    }

    public void Dispose()
    {
        if (_loop != IntPtr.Zero)
        {
            pw_thread_loop_lock(_loop);
            if (_stream != IntPtr.Zero)
            {
                pw_stream_disconnect(_stream);
                pw_stream_destroy(_stream);
                _stream = IntPtr.Zero;
            }

            if (_core != IntPtr.Zero)
            {
                pw_core_disconnect(_core);
                _core = IntPtr.Zero;
            }

            pw_thread_loop_unlock(_loop);
            pw_thread_loop_stop(_loop);
        }

        if (_context != IntPtr.Zero)
        {
            pw_context_destroy(_context);
            _context = IntPtr.Zero;
        }

        if (_loop != IntPtr.Zero)
        {
            pw_thread_loop_destroy(_loop);
            _loop = IntPtr.Zero;
        }

        NativeMemory.Free(_events);
        NativeMemory.Free(_hook);
        _events = null;
        _hook = null;
        if (_self.IsAllocated)
        {
            _self.Free();
        }

        _firstFrame.Dispose();
    }

    private void Start(int remoteFd, uint nodeId)
    {
        pw_init(null, null);
        _self = GCHandle.Alloc(this);

        _loop = pw_thread_loop_new("multishock-capture", IntPtr.Zero);
        _context = pw_context_new(pw_thread_loop_get_loop(_loop), IntPtr.Zero, 0);
        if (_loop == IntPtr.Zero || _context == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not create a PipeWire context.");
        }

        if (pw_thread_loop_start(_loop) < 0)
        {
            throw new InvalidOperationException("Could not start the PipeWire thread loop.");
        }

        _events = NativeMemory.AllocZeroed(EventsSize);
        _hook = NativeMemory.AllocZeroed(SpaHookSize);
        *(uint*)_events = 0;
        *(delegate* unmanaged[Cdecl]<IntPtr, int, int, byte*, void>*)((byte*)_events + EventsStateChangedOffset) = &OnStateChanged;
        *(delegate* unmanaged[Cdecl]<IntPtr, uint, byte*, void>*)((byte*)_events + EventsParamChangedOffset) = &OnParamChanged;
        *(delegate* unmanaged[Cdecl]<IntPtr, void>*)((byte*)_events + EventsProcessOffset) = &OnProcess;

        var format = BuildVideoEnumFormat();
        pw_thread_loop_lock(_loop);
        try
        {
            _core = pw_context_connect_fd(_context, remoteFd, IntPtr.Zero, 0);
            if (_core == IntPtr.Zero)
            {
                throw new InvalidOperationException("Could not connect to the PipeWire remote provided by the desktop portal.");
            }

            _stream = pw_stream_new(_core, "MultiShock Image Detection",
                pw_properties_new_string("media.type=Video media.category=Capture media.role=Screen"));
            if (_stream == IntPtr.Zero)
            {
                throw new InvalidOperationException("Could not create a PipeWire stream.");
            }

            pw_stream_add_listener(_stream, _hook, _events, GCHandle.ToIntPtr(_self));

            fixed (byte* formatPod = format)
            {
                var parameters = stackalloc void*[1] { formatPod };
                var result = pw_stream_connect(_stream, DirectionInput, nodeId,
                    StreamFlagAutoconnect | StreamFlagMapBuffers, parameters, 1);
                if (result < 0)
                {
                    throw new InvalidOperationException($"pw_stream_connect failed ({result}).");
                }
            }
        }
        finally
        {
            pw_thread_loop_unlock(_loop);
        }
    }

    private void HandleParamChanged(uint id, byte* param)
    {
        if (id != SpaParamFormat || param == null)
        {
            return;
        }

        if (!TryParseVideoFormat(param, out _width, out _height, out _format))
        {
            _error = "The screen-cast stream negotiated an unsupported video format.";
            return;
        }

        _logger?.LogInformation("Screen-cast stream negotiated {Width}x{Height}, format {Format}", _width, _height, _format);
        var buffers = BuildShmBuffersParam();
        fixed (byte* buffersPod = buffers)
        {
            var parameters = stackalloc void*[1] { buffersPod };
            pw_stream_update_params(_stream, parameters, 1);
        }
    }

    private void HandleProcess()
    {
        // Drain to the newest buffer; older ones are already stale.
        var newest = IntPtr.Zero;
        IntPtr buffer;
        while ((buffer = pw_stream_dequeue_buffer(_stream)) != IntPtr.Zero)
        {
            if (newest != IntPtr.Zero)
            {
                pw_stream_queue_buffer(_stream, newest);
            }

            newest = buffer;
        }

        if (newest == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var now = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;
            if (_frame is not null && now - _lastCopyTicks < MinCopyIntervalTicks)
            {
                return;
            }

            CopyFrame(*(byte**)newest);
            _lastCopyTicks = now;
        }
        finally
        {
            pw_stream_queue_buffer(_stream, newest);
        }
    }

    private void CopyFrame(byte* spaBuffer)
    {
        if (_width <= 0 || _height <= 0 || *(uint*)(spaBuffer + BufferNDatasOffset) == 0)
        {
            return;
        }

        var data = *(byte**)(spaBuffer + BufferDatasOffset);
        var pixels = *(byte**)(data + DataDataOffset);
        var chunk = *(byte**)(data + DataChunkOffset);
        if (pixels == null || chunk == null)
        {
            if (!_warnedUnmappable)
            {
                _warnedUnmappable = true;
                _logger?.LogWarning("Screen-cast buffer of type {Type} could not be mapped", *(uint*)(data + DataTypeOffset));
            }

            return;
        }

        if ((*(int*)(chunk + ChunkFlagsOffset) & SpaChunkFlagCorrupted) != 0 || *(uint*)(chunk + ChunkSizeOffset) == 0)
        {
            return;
        }

        var stride = *(int*)(chunk + ChunkStrideOffset);
        var rowBytes = _width * 4;
        if (stride <= 0)
        {
            stride = rowBytes;
        }

        var source = pixels + *(uint*)(chunk + ChunkOffsetOffset);
        lock (_frameSync)
        {
            var size = rowBytes * _height;
            if (_frame is null || _frame.Length != size)
            {
                _frame = new byte[size];
            }

            fixed (byte* destination = _frame)
            {
                for (var row = 0; row < _height; row++)
                {
                    Buffer.MemoryCopy(source + (long)row * stride, destination + (long)row * rowBytes, rowBytes, rowBytes);
                }
            }

            _frameWidth = _width;
            _frameHeight = _height;
            _frameFormat = _format;
        }

        if (!_firstFrame.IsSet)
        {
            _logger?.LogDebug("First screen-cast frame received (stride {Stride})", stride);
        }

        _firstFrame.Set();
    }

    private static PipeWireScreenStream? FromHandle(IntPtr data) =>
        GCHandle.FromIntPtr(data).Target as PipeWireScreenStream;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnStateChanged(IntPtr data, int oldState, int state, byte* error)
    {
        var self = FromHandle(data);
        if (self is null)
        {
            return;
        }

        self._logger?.LogDebug("Screen-cast stream state {OldState} -> {State}", oldState, state);
        if (state == StreamStateError)
        {
            self._error = error != null ? Marshal.PtrToStringUTF8((IntPtr)error) : "The screen-cast stream failed.";
            self._logger?.LogWarning("Screen-cast stream error: {Error}", self._error);
            self._firstFrame.Set();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnParamChanged(IntPtr data, uint id, byte* param)
    {
        try
        {
            FromHandle(data)?.HandleParamChanged(id, param);
        }
        catch (Exception ex)
        {
            FromHandle(data)?._logger?.LogError(ex, "Failed to handle a screen-cast format change");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnProcess(IntPtr data)
    {
        try
        {
            FromHandle(data)?.HandleProcess();
        }
        catch (Exception ex)
        {
            FromHandle(data)?._logger?.LogError(ex, "Failed to read a screen-cast frame");
        }
    }
}
#endif
