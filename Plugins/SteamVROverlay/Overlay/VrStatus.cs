using System.Runtime.InteropServices;
using System.Text;
using Valve.VR;

namespace SteamVROverlay.Overlay;

/// <summary>
/// Reads device batteries and compositor frame timing from OpenVR. Must be called on the
/// thread that owns the OpenVR connection.
/// </summary>
public sealed class VrStatus
{
    /// <summary>Frame timing is summarised over this much recent history.</summary>
    private const double FrameWindowSeconds = 1.0;

    private readonly Compositor_FrameTiming[] _timings = new Compositor_FrameTiming[256];
    private readonly StringBuilder _text = new(256);
    private uint _sceneProcessId;
    private string? _sceneAppName;
    private uint _lastFrameIndex;

    public static IReadOnlyList<DeviceBattery> ReadBatteries(CVRSystem system)
    {
        var devices = new List<(BatteryDeviceKind Kind, float Level, bool Charging)>();

        for (uint index = 0; index < OpenVR.k_unMaxTrackedDeviceCount; index++)
        {
            var deviceClass = system.GetTrackedDeviceClass(index);
            if (deviceClass is ETrackedDeviceClass.Invalid or ETrackedDeviceClass.TrackingReference ||
                !system.IsTrackedDeviceConnected(index) ||
                !ReadBool(system, index, ETrackedDeviceProperty.Prop_DeviceProvidesBatteryStatus_Bool))
            {
                continue;
            }

            var error = ETrackedPropertyError.TrackedProp_Success;
            var level = system.GetFloatTrackedDeviceProperty(index, ETrackedDeviceProperty.Prop_DeviceBatteryPercentage_Float, ref error);
            if (error != ETrackedPropertyError.TrackedProp_Success)
            {
                continue;
            }

            var kind = deviceClass switch
            {
                ETrackedDeviceClass.HMD => BatteryDeviceKind.Headset,
                ETrackedDeviceClass.Controller => HandOf(system, index),
                ETrackedDeviceClass.GenericTracker => BatteryDeviceKind.Tracker,
                _ => BatteryDeviceKind.Other
            };

            devices.Add((kind, Math.Clamp(level, 0f, 1f), ReadBool(system, index, ETrackedDeviceProperty.Prop_DeviceIsCharging_Bool)));
        }

        return DescribeBatteries(devices);
    }

    /// <summary>
    /// Names and orders battery readings: headset, hands, other controllers, then trackers
    /// numbered in SteamVR's device order.
    /// </summary>
    public static IReadOnlyList<DeviceBattery> DescribeBatteries(IEnumerable<(BatteryDeviceKind Kind, float Level, bool Charging)> devices)
    {
        var trackerNumber = 0;
        var batteries = new List<DeviceBattery>();

        foreach (var (kind, level, charging) in devices)
        {
            var name = kind switch
            {
                BatteryDeviceKind.Headset => "Headset",
                BatteryDeviceKind.LeftHand => "Left hand",
                BatteryDeviceKind.RightHand => "Right hand",
                BatteryDeviceKind.Controller => "Controller",
                BatteryDeviceKind.Tracker => $"Tracker {++trackerNumber}",
                _ => "Device"
            };
            batteries.Add(new DeviceBattery(name, kind, level, charging));
        }

        // OrderBy is stable, so devices of the same kind keep SteamVR's order.
        return [.. batteries.OrderBy(b => b.Kind)];
    }

    public FrameStats ReadFrameStats(CVRCompositor compositor, CVRSystem system, CVRApplications? applications)
    {
        var appName = ReadSceneAppName(compositor, applications);

        var hzError = ETrackedPropertyError.TrackedProp_Success;
        var hz = system.GetFloatTrackedDeviceProperty(
            OpenVR.k_unTrackedDeviceIndex_Hmd,
            ETrackedDeviceProperty.Prop_DisplayFrequency_Float,
            ref hzError);
        float? displayHz = hzError == ETrackedPropertyError.TrackedProp_Success && hz > 0f ? hz : null;

        // Only the first entry's size needs setting; the compositor infers the rest from it.
        _timings[0].m_nSize = (uint)Marshal.SizeOf<Compositor_FrameTiming>();
        var count = (int)compositor.GetFrameTimings(_timings);

        // With no app running the compositor keeps reporting the last one's final frames.
        var newest = count > 0 ? _timings[count - 1].m_nFrameIndex : 0;
        var stale = appName is null || newest == _lastFrameIndex;
        _lastFrameIndex = newest;

        var summary = stale ? default : Summarize(_timings.AsSpan(0, count), FrameWindowSeconds);
        return new FrameStats(appName, summary.Fps, displayHz, summary.GpuMs, summary.ReprojectedFraction);
    }

    public readonly record struct FrameSummary(float? Fps, float? GpuMs, float? ReprojectedFraction);

    /// <summary>
    /// Summarises the frames that fall within <paramref name="windowSeconds"/> of the newest.
    /// Each entry is one frame from the app; it counts once per vsync it was shown for, so any
    /// vsync beyond the first was filled by reprojection.
    /// </summary>
    public static FrameSummary Summarize(ReadOnlySpan<Compositor_FrameTiming> timings, double windowSeconds)
    {
        if (timings.Length < 2)
        {
            return default;
        }

        var newest = timings[^1].m_flSystemTimeInSeconds;
        var first = timings.Length - 1;
        while (first > 0 && newest - timings[first - 1].m_flSystemTimeInSeconds <= windowSeconds)
        {
            first--;
        }

        var window = timings[first..];
        var span = newest - window[0].m_flSystemTimeInSeconds;
        if (window.Length < 2 || span <= 0)
        {
            return default;
        }

        var gpuMs = 0f;
        long vsyncs = 0;
        foreach (var frame in window)
        {
            gpuMs += frame.m_flTotalRenderGpuMs;
            vsyncs += frame.m_nNumFramePresents + frame.m_nNumDroppedFrames;
        }

        var fps = (float)((window.Length - 1) / span);
        var reprojected = vsyncs > 0 ? Math.Clamp(1f - (float)window.Length / vsyncs, 0f, 1f) : 0f;
        return new FrameSummary(fps, gpuMs / window.Length, reprojected);
    }

    private string? ReadSceneAppName(CVRCompositor compositor, CVRApplications? applications)
    {
        var processId = compositor.GetCurrentSceneFocusProcess();
        if (processId == 0 || applications is null)
        {
            _sceneProcessId = 0;
            _sceneAppName = null;
            return null;
        }

        if (processId == _sceneProcessId)
        {
            return _sceneAppName;
        }

        _sceneProcessId = processId;
        _sceneAppName = null;

        _text.Clear();
        if (applications.GetApplicationKeyByProcessId(processId, _text, (uint)_text.Capacity) != EVRApplicationError.None)
        {
            // Apps without a SteamVR manifest still render; they just have no display name.
            _sceneAppName = "VR app";
            return _sceneAppName;
        }

        var key = _text.ToString();
        var error = EVRApplicationError.None;
        _text.Clear();
        applications.GetApplicationPropertyString(key, EVRApplicationProperty.Name_String, _text, (uint)_text.Capacity, ref error);
        _sceneAppName = error == EVRApplicationError.None && _text.Length > 0 ? _text.ToString() : "VR app";
        return _sceneAppName;
    }

    private static BatteryDeviceKind HandOf(CVRSystem system, uint index)
    {
        var role = system.GetControllerRoleForTrackedDeviceIndex(index);
        if (role is ETrackedControllerRole.Invalid)
        {
            // Controllers that are on but not currently driving a hand still report which hand they belong to.
            var error = ETrackedPropertyError.TrackedProp_Success;
            var hint = system.GetInt32TrackedDeviceProperty(index, ETrackedDeviceProperty.Prop_ControllerRoleHint_Int32, ref error);
            role = error == ETrackedPropertyError.TrackedProp_Success ? (ETrackedControllerRole)hint : ETrackedControllerRole.Invalid;
        }

        return role switch
        {
            ETrackedControllerRole.LeftHand => BatteryDeviceKind.LeftHand,
            ETrackedControllerRole.RightHand => BatteryDeviceKind.RightHand,
            _ => BatteryDeviceKind.Controller
        };
    }

    private static bool ReadBool(CVRSystem system, uint index, ETrackedDeviceProperty property)
    {
        var error = ETrackedPropertyError.TrackedProp_Success;
        var value = system.GetBoolTrackedDeviceProperty(index, property, ref error);
        return error == ETrackedPropertyError.TrackedProp_Success && value;
    }
}
