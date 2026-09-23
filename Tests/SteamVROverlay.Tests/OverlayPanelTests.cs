using MultiShock.PluginSdk;
using SkiaSharp;
using SteamVROverlay.Overlay;

namespace SteamVROverlay.Tests;

public sealed class OverlayPanelTests
{
    private static readonly OverlayPanelOptions DefaultOptions = new()
    {
        DefaultIntensity = 30,
        DefaultDurationSeconds = 1.0,
        MaxIntensity = 100,
        MaxDurationSeconds = 5.0,
        HoldToShock = false,
        ShockHoldMs = 600
    };

    private static ShockerInfo Shocker(int deviceId, int shockerId, string name) =>
        new() { DeviceId = deviceId, ShockerId = shockerId, Name = name };

    private static OverlayPanel CreatePanel(
        OverlayPanelOptions? options = null,
        params ShockerInfo[] shockers)
    {
        var panel = new OverlayPanel(options ?? DefaultOptions);
        panel.SetShockers(shockers, isConnected: shockers.Length > 0);
        return panel;
    }

    private static List<PanelActionRequest> Record(OverlayPanel panel)
    {
        var actions = new List<PanelActionRequest>();
        panel.ActionRequested += actions.Add;
        return actions;
    }

    private static SKRect RectOf(OverlayPanel panel, string regionId) =>
        panel.Regions.Single(r => r.Id == regionId).Rect;

    private static void Click(OverlayPanel panel, string regionId)
    {
        var rect = RectOf(panel, regionId);
        panel.PointerDown(rect.MidX, rect.MidY);
        panel.PointerUp(rect.MidX, rect.MidY);
    }

    [Fact]
    public void TargetsEveryShockerWhenNothingIsSelected()
    {
        var panel = CreatePanel(null, Shocker(1, 1, "Left"), Shocker(1, 2, "Right"));

        Assert.Equal(2, panel.ResolveTargets().Count);
    }

    [Fact]
    public void SelectingChipsNarrowsTheTargets()
    {
        var panel = CreatePanel(null, Shocker(1, 1, "Left"), Shocker(1, 2, "Right"));

        Click(panel, "shocker:1:2");

        var targets = panel.ResolveTargets();
        Assert.Equal(2, Assert.Single(targets).ShockerId);
    }

    [Fact]
    public void ClickingASelectedChipAgainDeselectsIt()
    {
        var panel = CreatePanel(null, Shocker(1, 1, "Left"), Shocker(1, 2, "Right"));

        Click(panel, "shocker:1:1");
        Click(panel, "shocker:1:1");

        Assert.Equal(2, panel.ResolveTargets().Count);
    }

    [Fact]
    public void VibrateFiresWithTheCurrentIntensityAndDuration()
    {
        var panel = CreatePanel(null, Shocker(1, 1, "Left"));
        var actions = Record(panel);

        Click(panel, OverlayPanel.RegionVibrate);

        var action = Assert.Single(actions);
        Assert.Equal(PanelCommand.Vibrate, action.Command);
        Assert.Equal(30, action.Intensity);
        Assert.Equal(1.0, action.DurationSeconds);
        Assert.Single(action.Targets);
    }

    [Fact]
    public void NoActionFiresWithoutShockers()
    {
        var panel = CreatePanel();
        var actions = Record(panel);

        Click(panel, OverlayPanel.RegionVibrate);

        Assert.Empty(actions);
        Assert.Equal("No shockers loaded", panel.Toast);
    }

    [Fact]
    public void ReleasingOutsideThePressedRegionCancelsTheClick()
    {
        var panel = CreatePanel(null, Shocker(1, 1, "Left"));
        var actions = Record(panel);

        var rect = RectOf(panel, OverlayPanel.RegionVibrate);
        panel.PointerDown(rect.MidX, rect.MidY);
        panel.PointerUp(rect.MidX, rect.Bottom + 40f);

        Assert.Empty(actions);
    }

    [Fact]
    public void StopFiresImmediatelyEvenWhenHoldToShockIsOn()
    {
        var options = DefaultOptions with { HoldToShock = true };
        var panel = CreatePanel(options, Shocker(1, 1, "Left"));
        var actions = Record(panel);

        Click(panel, OverlayPanel.RegionStop);

        Assert.Equal(PanelCommand.Stop, Assert.Single(actions).Command);
    }

    [Fact]
    public void ShockFiresOnASinglePressWhenHoldIsDisabled()
    {
        var panel = CreatePanel(null, Shocker(1, 1, "Left"));
        var actions = Record(panel);

        Click(panel, OverlayPanel.RegionShock);

        Assert.Equal(PanelCommand.Shock, Assert.Single(actions).Command);
    }

    [Fact]
    public void HoldToShockDoesNotFireBeforeTheHoldElapses()
    {
        var options = DefaultOptions with { HoldToShock = true, ShockHoldMs = 600 };
        var panel = CreatePanel(options, Shocker(1, 1, "Left"));
        var actions = Record(panel);

        var rect = RectOf(panel, OverlayPanel.RegionShock);
        panel.PointerDown(rect.MidX, rect.MidY);
        panel.Tick(DateTime.UtcNow.AddMilliseconds(300));

        Assert.Empty(actions);
        Assert.InRange(panel.HoldProgress, 0.1f, 0.9f);
    }

    [Fact]
    public void HoldToShockFiresOnceTheHoldElapses()
    {
        var options = DefaultOptions with { HoldToShock = true, ShockHoldMs = 600 };
        var panel = CreatePanel(options, Shocker(1, 1, "Left"));
        var actions = Record(panel);

        var rect = RectOf(panel, OverlayPanel.RegionShock);
        panel.PointerDown(rect.MidX, rect.MidY);
        panel.Tick(DateTime.UtcNow.AddMilliseconds(700));

        Assert.Equal(PanelCommand.Shock, Assert.Single(actions).Command);
        Assert.Equal(0f, panel.HoldProgress);
    }

    [Fact]
    public void ReleasingBeforeTheHoldElapsesCancelsTheShock()
    {
        var options = DefaultOptions with { HoldToShock = true, ShockHoldMs = 600 };
        var panel = CreatePanel(options, Shocker(1, 1, "Left"));
        var actions = Record(panel);

        var rect = RectOf(panel, OverlayPanel.RegionShock);
        panel.PointerDown(rect.MidX, rect.MidY);
        panel.Tick(DateTime.UtcNow.AddMilliseconds(200));
        panel.PointerUp(rect.MidX, rect.MidY);
        panel.Tick(DateTime.UtcNow.AddMilliseconds(900));

        Assert.Empty(actions);
        Assert.Equal(0f, panel.HoldProgress);
    }

    [Fact]
    public void SlidingTheLaserOffTheShockButtonCancelsTheHold()
    {
        var options = DefaultOptions with { HoldToShock = true, ShockHoldMs = 600 };
        var panel = CreatePanel(options, Shocker(1, 1, "Left"));
        var actions = Record(panel);

        var rect = RectOf(panel, OverlayPanel.RegionShock);
        panel.PointerDown(rect.MidX, rect.MidY);
        panel.PointerMove(rect.Left - 40f, rect.MidY);
        panel.Tick(DateTime.UtcNow.AddMilliseconds(900));

        Assert.Empty(actions);
    }

    [Fact]
    public void DraggingTheIntensitySliderSpansTheConfiguredRange()
    {
        var panel = CreatePanel(null, Shocker(1, 1, "Left"));
        var track = OverlayPanel.TrackRectFor(RectOf(panel, OverlayPanel.RegionIntensity));

        panel.PointerDown(track.Left, track.MidY);
        Assert.Equal(1, panel.Intensity);

        panel.PointerMove(track.Right, track.MidY);
        Assert.Equal(100, panel.Intensity);

        panel.PointerMove(track.MidX, track.MidY);
        Assert.InRange(panel.Intensity, 48, 53);

        panel.PointerUp(track.MidX, track.MidY);
    }

    [Fact]
    public void IntensityIsCappedByTheConfiguredMaximum()
    {
        var options = DefaultOptions with { MaxIntensity = 40 };
        var panel = CreatePanel(options, Shocker(1, 1, "Left"));
        var track = OverlayPanel.TrackRectFor(RectOf(panel, OverlayPanel.RegionIntensity));

        panel.PointerDown(track.Right, track.MidY);

        Assert.Equal(40, panel.Intensity);
    }

    [Fact]
    public void LoweringTheMaximumClampsTheCurrentIntensity()
    {
        var panel = CreatePanel(null, Shocker(1, 1, "Left"));
        var track = OverlayPanel.TrackRectFor(RectOf(panel, OverlayPanel.RegionIntensity));
        panel.PointerDown(track.Right, track.MidY);
        panel.PointerUp(track.Right, track.MidY);
        Assert.Equal(100, panel.Intensity);

        panel.ApplyOptions(DefaultOptions with { MaxIntensity = 25 });

        Assert.Equal(25, panel.Intensity);
    }

    [Fact]
    public void ShockersArePagedAndPagingChangesTheVisibleChips()
    {
        var shockers = Enumerable.Range(1, 10)
            .Select(i => Shocker(1, i, $"Shocker {i}"))
            .ToArray();
        var panel = CreatePanel(null, shockers);

        Assert.Equal(2, panel.PageCount);
        Assert.Equal(8, panel.PageShockers().Count);

        Click(panel, OverlayPanel.RegionPageNext);

        Assert.Equal(1, panel.Page);
        Assert.Equal(2, panel.PageShockers().Count);
        Assert.Equal(9, panel.PageShockers()[0].ShockerId);
    }

    [Fact]
    public void SelectionSurvivesPagingAndStillTargetsOffPageShockers()
    {
        var shockers = Enumerable.Range(1, 10)
            .Select(i => Shocker(1, i, $"Shocker {i}"))
            .ToArray();
        var panel = CreatePanel(null, shockers);

        Click(panel, "shocker:1:1");
        Click(panel, OverlayPanel.RegionPageNext);
        Click(panel, "shocker:1:9");

        var targets = panel.ResolveTargets();
        Assert.Equal([1, 9], targets.Select(t => t.ShockerId).OrderBy(id => id));
    }

    [Fact]
    public void RemovingAShockerDropsItFromTheSelection()
    {
        var panel = CreatePanel(null, Shocker(1, 1, "Left"), Shocker(1, 2, "Right"));
        Click(panel, "shocker:1:2");

        panel.SetShockers([Shocker(1, 1, "Left")], isConnected: true);

        // The only selected shocker is gone, so the panel falls back to targeting everything.
        Assert.Equal(1, Assert.Single(panel.ResolveTargets()).ShockerId);
    }

    [Fact]
    public void TheCursorFollowsThePointerAndClearsOnLeave()
    {
        var panel = CreatePanel(null, Shocker(1, 1, "Left"));

        panel.PointerMove(100f, 200f);
        Assert.Equal(new SKPoint(100f, 200f), panel.Cursor);

        panel.PointerLeave();
        Assert.Null(panel.Cursor);
    }

    [Fact]
    public void SubPixelTremorDoesNotTriggerARedraw()
    {
        var panel = CreatePanel(null, Shocker(1, 1, "Left"));
        panel.PointerMove(100f, 200f);
        panel.ClearDirty();

        panel.PointerMove(100.3f, 200.2f);

        Assert.False(panel.IsDirty);
    }

    [Fact]
    public void PointerLeaveClearsAnInProgressPress()
    {
        var options = DefaultOptions with { HoldToShock = true };
        var panel = CreatePanel(options, Shocker(1, 1, "Left"));
        var actions = Record(panel);

        var rect = RectOf(panel, OverlayPanel.RegionShock);
        panel.PointerDown(rect.MidX, rect.MidY);
        panel.PointerLeave();
        panel.Tick(DateTime.UtcNow.AddMilliseconds(900));

        Assert.Empty(actions);
        Assert.Null(panel.PressedRegionId);
    }
}
