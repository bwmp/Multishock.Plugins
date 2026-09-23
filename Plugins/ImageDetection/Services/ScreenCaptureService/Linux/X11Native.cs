#if !WINDOWS && !MACCATALYST
using System.Runtime.InteropServices;

namespace ImageDetection.Services.Linux;

// Xlib, MIT-SHM, RandR and SysV shared-memory entry points. Struct offsets match the
// x86_64/aarch64 LP64 ABI (XID and Atom are C longs).
internal static unsafe class X11Native
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXext = "libXext.so.6";
    private const string LibXrandr = "libXrandr.so.2";
    private const string LibC = "libc";

    public const int ZPixmap = 2;
    public static readonly nuint AllPlanes = nuint.MaxValue;
    public const int IsViewable = 2;
    public const nuint AnyPropertyType = 0;
    public const nuint XaCardinal = 6;
    public const nuint XaString = 31;
    public const nuint XaWindow = 33;
    public const nuint XaWmName = 39;

    public const int IpcPrivate = 0;
    public const int IpcCreat = 0x200;
    public const int IpcRmid = 0;

    [StructLayout(LayoutKind.Explicit, Size = 136)]
    public struct XImage
    {
        [FieldOffset(0)] public int Width;
        [FieldOffset(4)] public int Height;
        [FieldOffset(16)] public byte* Data;
        [FieldOffset(24)] public int ByteOrder;
        [FieldOffset(40)] public int Depth;
        [FieldOffset(44)] public int BytesPerLine;
        [FieldOffset(48)] public int BitsPerPixel;
        [FieldOffset(56)] public nuint RedMask;
        [FieldOffset(72)] public nuint BlueMask;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct XShmSegmentInfo
    {
        [FieldOffset(0)] public nuint ShmSeg;
        [FieldOffset(8)] public int ShmId;
        [FieldOffset(16)] public byte* ShmAddr;
        [FieldOffset(24)] public int ReadOnly;
    }

    [StructLayout(LayoutKind.Explicit, Size = 56)]
    public struct XRRMonitorInfo
    {
        [FieldOffset(0)] public nuint Name;
        [FieldOffset(8)] public int Primary;
        [FieldOffset(20)] public int X;
        [FieldOffset(24)] public int Y;
        [FieldOffset(28)] public int Width;
        [FieldOffset(32)] public int Height;
    }

    [StructLayout(LayoutKind.Explicit, Size = 136)]
    public struct XWindowAttributes
    {
        [FieldOffset(0)] public int X;
        [FieldOffset(4)] public int Y;
        [FieldOffset(8)] public int Width;
        [FieldOffset(12)] public int Height;
        [FieldOffset(20)] public int Depth;
        [FieldOffset(92)] public int MapState;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct XErrorEvent
    {
        [FieldOffset(8)] public IntPtr Display;
        [FieldOffset(32)] public byte ErrorCode;
    }

    [DllImport(LibX11)] public static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(LibX11)] public static extern int XCloseDisplay(IntPtr display);
    [DllImport(LibX11)] public static extern nuint XDefaultRootWindow(IntPtr display);
    [DllImport(LibX11)] public static extern int XDefaultScreen(IntPtr display);
    [DllImport(LibX11)] public static extern IntPtr XDefaultVisual(IntPtr display, int screen);
    [DllImport(LibX11)] public static extern int XDefaultDepth(IntPtr display, int screen);
    [DllImport(LibX11)] public static extern int XDisplayWidth(IntPtr display, int screen);
    [DllImport(LibX11)] public static extern int XDisplayHeight(IntPtr display, int screen);
    [DllImport(LibX11)] public static extern XImage* XGetImage(IntPtr display, nuint drawable, int x, int y, uint width, uint height, nuint planeMask, int format);
    [DllImport(LibX11)] public static extern int XDestroyImage(XImage* image);
    [DllImport(LibX11)] public static extern int XGetWindowAttributes(IntPtr display, nuint window, out XWindowAttributes attributes);
    [DllImport(LibX11)] public static extern int XTranslateCoordinates(IntPtr display, nuint source, nuint destination, int sourceX, int sourceY, out int destinationX, out int destinationY, out nuint child);
    [DllImport(LibX11)] public static extern nuint XInternAtom(IntPtr display, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int onlyIfExists);
    [DllImport(LibX11)] public static extern IntPtr XGetAtomName(IntPtr display, nuint atom);
    [DllImport(LibX11)] public static extern int XGetWindowProperty(IntPtr display, nuint window, nuint property, nint offset, nint length, int delete, nuint requestedType, out nuint actualType, out int actualFormat, out nuint itemCount, out nuint bytesAfter, out byte* data);
    [DllImport(LibX11)] public static extern int XFree(void* data);
    [DllImport(LibX11)] public static extern int XQueryTree(IntPtr display, nuint window, out nuint root, out nuint parent, out nuint* children, out uint childCount);
    [DllImport(LibX11)] public static extern int XSync(IntPtr display, int discard);
    [DllImport(LibX11)] public static extern IntPtr XSetErrorHandler(IntPtr handler);

    [DllImport(LibXext)] public static extern int XShmQueryExtension(IntPtr display);
    [DllImport(LibXext)] public static extern XImage* XShmCreateImage(IntPtr display, IntPtr visual, uint depth, int format, byte* data, XShmSegmentInfo* segment, uint width, uint height);
    [DllImport(LibXext)] public static extern int XShmAttach(IntPtr display, XShmSegmentInfo* segment);
    [DllImport(LibXext)] public static extern int XShmDetach(IntPtr display, XShmSegmentInfo* segment);
    [DllImport(LibXext)] public static extern int XShmGetImage(IntPtr display, nuint drawable, XImage* image, int x, int y, nuint planeMask);

    [DllImport(LibXrandr)] public static extern XRRMonitorInfo* XRRGetMonitors(IntPtr display, nuint window, int getActive, out int count);
    [DllImport(LibXrandr)] public static extern void XRRFreeMonitors(XRRMonitorInfo* monitors);

    [DllImport(LibC, SetLastError = true)] public static extern int shmget(int key, nuint size, int flags);
    [DllImport(LibC, SetLastError = true)] public static extern byte* shmat(int id, IntPtr address, int flags);
    [DllImport(LibC)] public static extern int shmdt(byte* address);
    [DllImport(LibC)] public static extern int shmctl(int id, int command, IntPtr buffer);
}
#endif
