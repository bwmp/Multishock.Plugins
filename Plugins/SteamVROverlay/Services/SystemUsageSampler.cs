using System.Runtime.InteropServices;
using SteamVROverlay.Overlay;

namespace SteamVROverlay.Services;

/// <summary>
/// Whole-system CPU load and memory use. CPU load is measured between consecutive samples,
/// so the first sample has none.
/// </summary>
public sealed partial class SystemUsageSampler
{
    private ulong _lastIdle;
    private ulong _lastBusyAndIdle;

    public SystemUsage? Sample()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GetSystemTimes(out var idle, out var kernel, out var user) || !GlobalMemoryStatusEx(ref memory))
        {
            return null;
        }

        // Kernel time already includes idle time.
        var total = kernel + user;
        float? cpuLoad = null;
        if (_lastBusyAndIdle != 0 && total > _lastBusyAndIdle)
        {
            cpuLoad = Math.Clamp(1f - (float)(idle - _lastIdle) / (total - _lastBusyAndIdle), 0f, 1f);
        }

        _lastIdle = idle;
        _lastBusyAndIdle = total;

        return new SystemUsage(cpuLoad, memory.TotalPhysical - memory.AvailablePhysical, memory.TotalPhysical);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    // FILETIMEs are passed as 64-bit counts of 100 ns intervals.
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatus buffer);
}
