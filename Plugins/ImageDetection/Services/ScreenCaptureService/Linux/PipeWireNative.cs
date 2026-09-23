#if !WINDOWS && !MACCATALYST
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ImageDetection.Services.Linux;

// libpipewire-0.3 entry points plus the SPA constants and struct offsets used to receive video
// frames. SPA's pod builder is header-only C, so the few pods needed are encoded by hand below.
internal static unsafe class PipeWireNative
{
    private const string Lib = "libpipewire-0.3.so.0";

    public const uint SpaTypeId = 3;
    public const uint SpaTypeInt = 4;
    public const uint SpaTypeRectangle = 10;
    public const uint SpaTypeFraction = 11;
    public const uint SpaTypeObject = 15;
    public const uint SpaTypeChoice = 19;
    public const uint SpaTypeObjectFormat = 0x40003;
    public const uint SpaTypeObjectParamBuffers = 0x40004;

    public const uint SpaParamEnumFormat = 3;
    public const uint SpaParamFormat = 4;
    public const uint SpaParamBuffers = 5;

    public const uint SpaFormatMediaType = 1;
    public const uint SpaFormatMediaSubtype = 2;
    public const uint SpaFormatVideoFormat = 0x20001;
    public const uint SpaFormatVideoSize = 0x20003;
    public const uint SpaFormatVideoFramerate = 0x20004;
    public const uint SpaFormatVideoMaxFramerate = 0x20005;
    public const uint SpaMediaTypeVideo = 2;
    public const uint SpaMediaSubtypeRaw = 1;
    public const uint SpaParamBuffersDataType = 6;

    public const uint SpaChoiceNone = 0;
    public const uint SpaChoiceRange = 1;
    public const uint SpaChoiceEnum = 3;
    public const uint SpaChoiceFlags = 4;

    public const uint VideoFormatRgbx = 7;
    public const uint VideoFormatBgrx = 8;
    public const uint VideoFormatRgba = 11;
    public const uint VideoFormatBgra = 12;

    public const uint SpaDataMemPtr = 1;
    public const uint SpaDataMemFd = 2;
    public const int SpaChunkFlagCorrupted = 1;

    public const int DirectionInput = 0;
    public const int StreamFlagAutoconnect = 1;
    public const int StreamFlagMapBuffers = 4;
    public const int StreamStateError = -1;
    public const int StreamStateStreaming = 3;

    // struct pw_stream_events (version 0 uses the members up to `drained`).
    public const int EventsSize = 96;
    public const int EventsStateChangedOffset = 16;
    public const int EventsParamChangedOffset = 40;
    public const int EventsProcessOffset = 64;
    public const int SpaHookSize = 48;

    // struct spa_buffer / spa_data / spa_chunk
    public const int BufferDatasOffset = 16;
    public const int BufferNDatasOffset = 4;
    public const int DataTypeOffset = 0;
    public const int DataDataOffset = 24;
    public const int DataChunkOffset = 32;
    public const int ChunkOffsetOffset = 0;
    public const int ChunkSizeOffset = 4;
    public const int ChunkStrideOffset = 8;
    public const int ChunkFlagsOffset = 12;

    [DllImport(Lib)] public static extern void pw_init(int* argc, byte*** argv);
    [DllImport(Lib)] public static extern IntPtr pw_get_library_version();
    [DllImport(Lib)] public static extern IntPtr pw_thread_loop_new([MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr properties);
    [DllImport(Lib)] public static extern IntPtr pw_thread_loop_get_loop(IntPtr loop);
    [DllImport(Lib)] public static extern int pw_thread_loop_start(IntPtr loop);
    [DllImport(Lib)] public static extern void pw_thread_loop_stop(IntPtr loop);
    [DllImport(Lib)] public static extern void pw_thread_loop_lock(IntPtr loop);
    [DllImport(Lib)] public static extern void pw_thread_loop_unlock(IntPtr loop);
    [DllImport(Lib)] public static extern void pw_thread_loop_destroy(IntPtr loop);
    [DllImport(Lib)] public static extern IntPtr pw_context_new(IntPtr mainLoop, IntPtr properties, nuint userDataSize);
    [DllImport(Lib)] public static extern void pw_context_destroy(IntPtr context);
    [DllImport(Lib)] public static extern IntPtr pw_context_connect_fd(IntPtr context, int fd, IntPtr properties, nuint userDataSize);
    [DllImport(Lib)] public static extern int pw_core_disconnect(IntPtr core);
    [DllImport(Lib)] public static extern IntPtr pw_properties_new_string([MarshalAs(UnmanagedType.LPUTF8Str)] string args);
    [DllImport(Lib)] public static extern IntPtr pw_stream_new(IntPtr core, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr properties);
    [DllImport(Lib)] public static extern void pw_stream_add_listener(IntPtr stream, void* hook, void* events, IntPtr data);
    [DllImport(Lib)] public static extern int pw_stream_connect(IntPtr stream, int direction, uint targetId, int flags, void** parameters, uint parameterCount);
    [DllImport(Lib)] public static extern int pw_stream_disconnect(IntPtr stream);
    [DllImport(Lib)] public static extern void pw_stream_destroy(IntPtr stream);
    [DllImport(Lib)] public static extern IntPtr pw_stream_dequeue_buffer(IntPtr stream);
    [DllImport(Lib)] public static extern int pw_stream_queue_buffer(IntPtr stream, IntPtr buffer);
    [DllImport(Lib)] public static extern int pw_stream_update_params(IntPtr stream, void** parameters, uint parameterCount);

    /// <summary>
    /// EnumFormat for raw 32-bit RGB video. No modifier is offered, so producers send
    /// CPU-mappable shared-memory buffers instead of DMA-BUFs.
    /// </summary>
    public static byte[] BuildVideoEnumFormat()
    {
        var pod = new PodWriter();
        var obj = pod.BeginObject(SpaTypeObjectFormat, SpaParamEnumFormat);
        pod.Property(SpaFormatMediaType);
        pod.Id(SpaMediaTypeVideo);
        pod.Property(SpaFormatMediaSubtype);
        pod.Id(SpaMediaSubtypeRaw);
        pod.Property(SpaFormatVideoFormat);
        pod.Choice(SpaChoiceEnum, SpaTypeId, 4,
            [VideoFormatBgrx, VideoFormatBgrx, VideoFormatBgra, VideoFormatRgbx, VideoFormatRgba]);
        pod.Property(SpaFormatVideoSize);
        pod.Choice(SpaChoiceRange, SpaTypeRectangle, 8, [1920, 1080, 1, 1, 16384, 16384]);
        pod.Property(SpaFormatVideoFramerate);
        pod.Choice(SpaChoiceRange, SpaTypeFraction, 8, [30, 1, 0, 1, 360, 1]);

        // Producers pace capture from this; xdg-desktop-portal-wlr sends no frames at all without it.
        pod.Property(SpaFormatVideoMaxFramerate);
        pod.Choice(SpaChoiceRange, SpaTypeFraction, 8, [30, 1, 1, 1, 360, 1]);
        pod.EndObject(obj);
        return pod.ToArray();
    }

    public static byte[] BuildShmBuffersParam()
    {
        var pod = new PodWriter();
        var obj = pod.BeginObject(SpaTypeObjectParamBuffers, SpaParamBuffers);
        pod.Property(SpaParamBuffersDataType);
        pod.Choice(SpaChoiceFlags, SpaTypeInt, 4, [(1u << (int)SpaDataMemPtr) | (1u << (int)SpaDataMemFd)]);
        pod.EndObject(obj);
        return pod.ToArray();
    }

    /// <summary>
    /// Reads the negotiated size and pixel format from a SPA_PARAM_Format object pod.
    /// </summary>
    public static bool TryParseVideoFormat(byte* pod, out int width, out int height, out uint format)
    {
        width = height = 0;
        format = 0;
        var size = *(uint*)pod;
        var type = *(uint*)(pod + 4);
        if (type != SpaTypeObject || size < 8)
        {
            return false;
        }

        var body = pod + 8;
        var offset = 8u; // skip the object type and id
        while (offset + 16 <= size)
        {
            var key = *(uint*)(body + offset);
            var valueSize = *(uint*)(body + offset + 8);
            var valueType = *(uint*)(body + offset + 12);
            var value = body + offset + 16;

            // A negotiated value can still be wrapped in a "None" choice; its first entry is the value.
            if (valueType == SpaTypeChoice && valueSize >= 16)
            {
                valueType = *(uint*)(value + 12);
                value += 16;
            }

            if (key == SpaFormatVideoFormat && valueType == SpaTypeId)
            {
                format = *(uint*)value;
            }
            else if (key == SpaFormatVideoSize && valueType == SpaTypeRectangle)
            {
                width = (int)*(uint*)value;
                height = (int)*(uint*)(value + 4);
            }

            offset += 16 + Align8(valueSize);
        }

        return width > 0 && height > 0 && format != 0;
    }

    private static uint Align8(uint value) => (value + 7) & ~7u;

    private sealed class PodWriter
    {
        private readonly List<byte> _bytes = [];

        public int BeginObject(uint objectType, uint id)
        {
            var start = _bytes.Count;
            WriteUInt32(0);
            WriteUInt32(SpaTypeObject);
            WriteUInt32(objectType);
            WriteUInt32(id);
            return start;
        }

        public void EndObject(int start) => PatchSize(start);

        public void Property(uint key)
        {
            WriteUInt32(key);
            WriteUInt32(0);
        }

        public void Id(uint value)
        {
            WriteUInt32(4);
            WriteUInt32(SpaTypeId);
            WriteUInt32(value);
            Pad();
        }

        public void Choice(uint choiceType, uint childType, uint childSize, uint[] values)
        {
            var start = _bytes.Count;
            WriteUInt32(0);
            WriteUInt32(SpaTypeChoice);
            WriteUInt32(choiceType);
            WriteUInt32(0);
            WriteUInt32(childSize);
            WriteUInt32(childType);
            foreach (var value in values)
            {
                WriteUInt32(value);
            }

            PatchSize(start);
            Pad();
        }

        public byte[] ToArray() => [.. _bytes];

        private void PatchSize(int start)
        {
            var size = (uint)(_bytes.Count - start - 8);
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, size);
            for (var i = 0; i < 4; i++)
            {
                _bytes[start + i] = buffer[i];
            }
        }

        private void Pad()
        {
            while (_bytes.Count % 8 != 0)
            {
                _bytes.Add(0);
            }
        }

        private void WriteUInt32(uint value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
            _bytes.AddRange(buffer.ToArray());
        }
    }
}
#endif
