using MultiShock.PluginSdk;
using SteamVROverlay.Overlay;

namespace SteamVROverlay.Tests;

/// <summary>
/// Drawing is otherwise only visible in a headset, so both views are rendered with and
/// without data to catch layout code that throws.
/// </summary>
public sealed class OverlayRendererTests
{
    [Theory]
    [InlineData(PanelView.Watch, true)]
    [InlineData(PanelView.Watch, false)]
    [InlineData(PanelView.Shockers, true)]
    [InlineData(PanelView.Shockers, false)]
    public void EveryViewRenders(PanelView view, bool withData)
    {
        var panel = new OverlayPanel(new OverlayPanelOptions());
        panel.ShowView(view);

        if (withData)
        {
            panel.SetShockers([new ShockerInfo { DeviceId = 1, ShockerId = 1, Name = "Left" }], isConnected: true);
            panel.SetBatteries(VrStatus.DescribeBatteries([(BatteryDeviceKind.Headset, 0.5f, true), (BatteryDeviceKind.Tracker, 0.05f, false)]));
            panel.SetMedia(new MediaState(
                "A song title long enough to be cut short on the watch face", "Artist", true, true, true, true,
                TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(3), DateTime.UtcNow, 1.0, Thumbnail: null));
            panel.SetAudio(new AudioState("Speakers", 0.4f, true, "Microphone", true));
            panel.SetFrameStats(new FrameStats("VRChat", 72f, 90f, 13f, 0.2f));
            panel.SetSystemUsage(new SystemUsage(0.3f, 8UL << 30, 32UL << 30));
        }
        else
        {
            panel.SetFrameStats(new FrameStats(null, null, null, null, null));
        }

        panel.Tick(DateTime.UtcNow);

        using var renderer = new OverlayRenderer();
        renderer.Render(panel);

        Assert.NotEmpty(renderer.EncodePng());
        Assert.Equal((int)MathF.Ceiling(panel.Height), renderer.Height);
    }

    [Fact]
    public void TheBitmapFollowsThePanelHeight()
    {
        var panel = new OverlayPanel(new OverlayPanelOptions());
        using var renderer = new OverlayRenderer();
        renderer.Render(panel);
        var watchHeight = renderer.Height;

        panel.ShowView(PanelView.Shockers);
        renderer.Render(panel);

        Assert.NotEqual(watchHeight, renderer.Height);
        Assert.Equal((int)OverlayPanel.ShockersHeight, renderer.Height);
    }

    [Fact]
    public void NotificationsRender()
    {
        using var renderer = new NotificationRenderer();
        renderer.Render(new VrNotification("Title", "A message long enough to wrap onto a second line in the popup", TimeSpan.FromSeconds(4), Buzz: true));

        Assert.NotEmpty(renderer.EncodePng());
    }
}
