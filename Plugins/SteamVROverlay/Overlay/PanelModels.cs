using MultiShock.PluginSdk.VrWatch;
using SkiaSharp;

namespace SteamVROverlay.Overlay;

public enum PanelView
{
    /// <summary>The watch face: a stack of cards like OVR Toolkit's wrist watch.</summary>
    Watch,

    /// <summary>The shocker controls, opened from the watch.</summary>
    Shockers,

    /// <summary>A page another plugin added, opened from the watch.</summary>
    Page
}

/// <summary>
/// Cards on the watch face, top to bottom.
/// </summary>
public enum WatchModule
{
    Clock,
    Media,
    Audio,
    Performance,

    /// <summary>A card another plugin added.</summary>
    Extension,

    /// <summary>The button that opens the shocker controls.</summary>
    Panels,

    /// <summary>The button that opens a page another plugin added.</summary>
    Page,

    /// <summary>The buttons that make the watch movable and show or hide the batteries.</summary>
    Toolbar,

    Batteries
}

/// <param name="Key">
/// For <see cref="WatchModule.Extension"/> and <see cref="WatchModule.Page"/>, the contributed
/// card or page it shows.
/// </param>
public sealed record WatchCard(WatchModule Module, SKRect Rect, string? Key = null);

public sealed record ContributedCard(string Key, string PluginId, VrWatchCard Card);

public sealed record ContributedPage(string Key, string PluginId, VrWatchPage Page);

/// <summary>
/// A contributed widget where layout put it, in a card on the watch or on a page.
/// </summary>
public sealed record PlacedWidget(string PluginId, VrWatchWidget Widget, SKRect Rect);

public enum PanelControl
{
    PreviousTrack,
    PlayPause,
    NextTrack,
    SetVolume,
    ToggleOutputMute,
    ToggleMicrophoneMute,
    ToggleBatteries
}

/// <summary>
/// A media or audio control used on the panel. <see cref="Volume"/> is only meaningful for
/// <see cref="PanelControl.SetVolume"/>.
/// </summary>
public sealed record PanelControlRequest(PanelControl Control, float Volume = 0f);

public enum BatteryDeviceKind
{
    Headset,
    LeftHand,
    RightHand,
    Controller,
    Tracker,
    Other
}

public sealed record DeviceBattery(string Name, BatteryDeviceKind Kind, float Level, bool IsCharging);

public sealed record MediaState(
    string Title,
    string Artist,
    bool IsPlaying,
    bool CanPlayPause,
    bool CanGoNext,
    bool CanGoPrevious,
    TimeSpan Position,
    TimeSpan Duration,
    DateTime PositionSampledUtc,
    double PlaybackRate,
    byte[]? Thumbnail)
{
    public bool HasTimeline => Duration > TimeSpan.Zero;

    /// <summary>
    /// Players report their position only when it changes discontinuously, so the position
    /// while playing is extrapolated from the last report.
    /// </summary>
    public TimeSpan PositionAt(DateTime utcNow)
    {
        var position = Position;
        if (IsPlaying && utcNow > PositionSampledUtc)
        {
            position += TimeSpan.FromTicks((long)((utcNow - PositionSampledUtc).Ticks * PlaybackRate));
        }

        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return HasTimeline && position > Duration ? Duration : position;
    }
}

public sealed record AudioState(
    string? OutputName,
    float OutputVolume,
    bool OutputMuted,
    string? MicrophoneName,
    bool MicrophoneMuted)
{
    public bool HasOutput => OutputName is not null;

    public bool HasMicrophone => MicrophoneName is not null;
}

/// <summary>
/// Frame pacing of the running VR app, as reported by the SteamVR compositor.
/// </summary>
public sealed record FrameStats(
    string? AppName,
    float? AppFps,
    float? DisplayHz,
    float? GpuFrameMs,
    float? ReprojectedFraction);

public sealed record SystemUsage(float? CpuLoad, ulong MemoryUsedBytes, ulong MemoryTotalBytes);
