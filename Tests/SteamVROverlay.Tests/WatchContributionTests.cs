using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MultiShock.PluginSdk;
using MultiShock.PluginSdk.VrWatch;
using SkiaSharp;
using SteamVROverlay.Overlay;
using SteamVROverlay.Services;

namespace SteamVROverlay.Tests;

public sealed class WatchContributionTests
{
    private const string PluginId = "com.example.obs";

    private static readonly OverlayPanelOptions DefaultOptions = new();

    private static readonly VrWatchSlider Volume = new("volume", "Volume", 50, 0, 100, Step: 10, Unit: "%");

    private static VrWatchContent Content(bool recording = false, double volume = 50, bool buttonEnabled = true) => new()
    {
        Cards =
        [
            new VrWatchCard("status",
            [
                new VrWatchStats([new VrWatchStat("Scene", "Game"), new VrWatchStat("Dropped", "2%", VrWatchTone.Warning)]),
                new VrWatchToggle("recording", "Recording", recording),
            ], Title: "OBS"),
        ],
        Pages =
        [
            new VrWatchPage("scenes", "Scenes",
            [
                new VrWatchButtons([new VrWatchButton("scene:game", "Game", Enabled: buttonEnabled), new VrWatchButton("scene:chat", "Chat")]),
                Volume with { Value = volume },
            ], Status: "Game"),
        ],
    };

    private static IReadOnlyList<WatchContribution> Contributions(VrWatchContent content) =>
        [new WatchContribution(PluginId, "OBS", WatchContent.Normalize(content))];

    private static OverlayPanel CreatePanel(VrWatchContent? content = null, OverlayPanelOptions? options = null)
    {
        var panel = new OverlayPanel(options ?? DefaultOptions);
        panel.SetContributions(Contributions(content ?? Content()));
        return panel;
    }

    private static List<WatchInputRequest> RecordInputs(OverlayPanel panel)
    {
        var inputs = new List<WatchInputRequest>();
        panel.WatchInputRequested += inputs.Add;
        return inputs;
    }

    private static SKRect RectOf(OverlayPanel panel, string regionId) =>
        panel.Regions.Single(r => r.Id == regionId).Rect;

    private static void Click(OverlayPanel panel, string regionId)
    {
        var rect = RectOf(panel, regionId);
        panel.PointerDown(rect.MidX, rect.MidY);
        panel.PointerUp(rect.MidX, rect.MidY);
    }

    private static string Control(string controlId) => OverlayPanel.ControlRegionId(PluginId, controlId);

    private static readonly string ScenesPage = WatchContent.PageKey(PluginId, "scenes");

    [Fact]
    public void PluginCardsFollowTheStatusCardsAndPageButtonsFollowShockers()
    {
        var panel = CreatePanel();

        Assert.Equal(
            [WatchModule.Clock, WatchModule.Media, WatchModule.Audio, WatchModule.Performance, WatchModule.Extension, WatchModule.Panels, WatchModule.Page, WatchModule.Toolbar, WatchModule.Batteries],
            panel.Cards.Select(c => c.Module));
        Assert.Equal(ScenesPage, panel.Cards.Single(c => c.Module == WatchModule.Page).Key);

        for (var i = 1; i < panel.Cards.Count; i++)
        {
            Assert.Equal(panel.Cards[i - 1].Rect.Bottom + OverlayPanel.CardGap, panel.Cards[i].Rect.Top);
        }
    }

    [Fact]
    public void APluginCardGrowsToFitItsWidgets()
    {
        var panel = CreatePanel();
        var card = panel.Cards.Single(c => c.Module == WatchModule.Extension).Rect;

        Assert.Equal(2, panel.Widgets.Count);
        Assert.All(panel.Widgets, w => Assert.True(card.Contains(w.Rect)));
        Assert.True(panel.Widgets[0].Rect.Top >= OverlayPanel.CardTitleRect(card).Bottom);
    }

    [Fact]
    public void HiddenCardsAndPagesAreLeftOffTheWatch()
    {
        var options = DefaultOptions with
        {
            HiddenWatchItems = new HashSet<string> { WatchContent.CardKey(PluginId, "status"), ScenesPage }
        };
        var panel = CreatePanel(options: options);

        Assert.DoesNotContain(panel.Cards, c => c.Module is WatchModule.Extension or WatchModule.Page);
        Assert.DoesNotContain(panel.Regions, r => r.Id == Control("recording"));
    }

    [Fact]
    public void APageButtonOpensThePageAndWatchGoesBack()
    {
        var panel = CreatePanel();

        Click(panel, OverlayPanel.OpenPageRegionId(ScenesPage));

        Assert.Equal(PanelView.Page, panel.ActiveView);
        Assert.Equal("Scenes", panel.ActivePage?.Page.Title);
        Assert.Contains(panel.Regions, r => r.Id == Control("scene:chat"));
        Assert.DoesNotContain(panel.Regions, r => r.Id == Control("recording"));
        Assert.True(panel.Height >= OverlayPanel.MinPageHeight);

        Click(panel, OverlayPanel.RegionBackToWatch);

        Assert.Equal(PanelView.Watch, panel.ActiveView);
        Assert.Null(panel.ActivePage);
    }

    [Fact]
    public void ButtonsInARowShareTheWidth()
    {
        var panel = CreatePanel();
        panel.OpenPage(ScenesPage);

        var game = RectOf(panel, Control("scene:game"));
        var chat = RectOf(panel, Control("scene:chat"));

        Assert.Equal(game.Top, chat.Top);
        Assert.Equal(game.Width, chat.Width);
        Assert.Equal(OverlayPanel.ButtonGap, chat.Left - game.Right, 3);
    }

    [Fact]
    public void PressingAButtonSendsItToThePlugin()
    {
        var panel = CreatePanel();
        panel.OpenPage(ScenesPage);
        var inputs = RecordInputs(panel);

        Click(panel, Control("scene:chat"));

        var input = Assert.Single(inputs);
        Assert.Equal(PluginId, input.PluginId);
        Assert.Equal(new VrWatchButtonPressed("scene:chat"), input.Input);
    }

    [Fact]
    public void DisabledButtonsSendNothing()
    {
        var panel = CreatePanel(Content(buttonEnabled: false));
        panel.OpenPage(ScenesPage);
        var inputs = RecordInputs(panel);

        Click(panel, Control("scene:game"));

        Assert.Empty(inputs);
    }

    [Fact]
    public void AToggleShowsItsNewStateUntilThePluginHasHandledIt()
    {
        var panel = CreatePanel();
        var inputs = RecordInputs(panel);
        var toggle = (VrWatchToggle)panel.Widgets.Single(w => w.Widget is VrWatchToggle).Widget;

        Click(panel, Control("recording"));

        Assert.Equal(new VrWatchToggleChanged("recording", true), Assert.Single(inputs).Input);
        Assert.True(panel.ToggleIsOn(PluginId, toggle));

        // The plugin refused, so once it has settled the toggle shows its real state again.
        panel.InputSettled(PluginId);

        Assert.False(panel.ToggleIsOn(PluginId, toggle));
    }

    [Fact]
    public void ASliderFollowsTheDragAndSendsOneSnappedValueOnRelease()
    {
        var panel = CreatePanel();
        panel.OpenPage(ScenesPage);
        var inputs = RecordInputs(panel);
        var track = OverlayPanel.TrackRectFor(RectOf(panel, Control("volume")));

        panel.PointerDown(track.Left, track.MidY);
        panel.PointerMove(track.Left + track.Width * 0.73f, track.MidY);

        Assert.Equal(70, panel.SliderValue(PluginId, Volume));
        Assert.Empty(inputs);

        panel.PointerUp(track.Left + track.Width * 0.73f, track.MidY);

        Assert.Equal(new VrWatchSliderChanged("volume", 70), Assert.Single(inputs).Input);
        Assert.Equal(70, panel.SliderValue(PluginId, Volume));
    }

    [Fact]
    public void ReleasingASliderWhereItStartedSendsNothing()
    {
        var panel = CreatePanel();
        panel.OpenPage(ScenesPage);
        var inputs = RecordInputs(panel);
        var track = OverlayPanel.TrackRectFor(RectOf(panel, Control("volume")));

        panel.PointerDown(track.MidX, track.MidY);
        panel.PointerUp(track.MidX, track.MidY);

        Assert.Empty(inputs);
    }

    [Fact]
    public void ASliderLeftMidDragKeepsItsValue()
    {
        var panel = CreatePanel();
        panel.OpenPage(ScenesPage);
        var inputs = RecordInputs(panel);
        var track = OverlayPanel.TrackRectFor(RectOf(panel, Control("volume")));

        panel.PointerDown(track.Right, track.MidY);
        panel.PointerLeave();

        Assert.Equal(new VrWatchSliderChanged("volume", 100), Assert.Single(inputs).Input);
    }

    [Fact]
    public void RebuiltButUnchangedContentDoesNotRedraw()
    {
        var panel = CreatePanel();
        _ = panel.Height;
        panel.ClearDirty();

        panel.SetContributions(Contributions(Content()));
        Assert.False(panel.IsDirty);

        panel.SetContributions(Contributions(Content(recording: true)));
        Assert.True(panel.IsDirty);
    }

    [Fact]
    public void ThePanelReturnsToTheWatchWhenItsPageGoesAway()
    {
        var panel = CreatePanel();
        panel.OpenPage(ScenesPage);

        panel.SetContributions([]);

        Assert.Equal(PanelView.Watch, panel.ActiveView);
        Assert.DoesNotContain(panel.Cards, c => c.Module is WatchModule.Extension or WatchModule.Page);
    }

    [Fact]
    public void ACardStopsAtItsMaximumHeight()
    {
        var lines = Enumerable.Range(0, 40).Select(i => (VrWatchWidget)new VrWatchText($"Line {i}")).ToList();
        var panel = CreatePanel(new VrWatchContent { Cards = [new VrWatchCard("log", lines)] });

        var card = panel.Cards.Single(c => c.Module == WatchModule.Extension).Rect;

        Assert.True(card.Height <= OverlayPanel.MaxCardHeight);
        Assert.InRange(panel.Widgets.Count, 1, lines.Count - 1);
    }

    [Fact]
    public void MalformedContentFromAPluginIsDroppedRatherThanDrawn()
    {
        var content = new VrWatchContent
        {
            Cards =
            [
                null!,
                new VrWatchCard("card",
                [
                    null!,
                    new VrWatchText(null!),
                    new VrWatchButtons(null!),
                    new VrWatchToggle(null!, "No id", true),
                    new VrWatchStats([null!, new VrWatchStat("Label", null!)]),
                ]),
            ],
            Pages = null!,
        };

        var normalized = WatchContent.Normalize(content);

        var card = Assert.Single(normalized.Cards);
        Assert.Empty(normalized.Pages);
        Assert.Collection(
            card.Widgets,
            w => Assert.Equal("", Assert.IsType<VrWatchText>(w).Text),
            w => Assert.Empty(Assert.IsType<VrWatchButtons>(w).Buttons),
            w => Assert.Equal(new VrWatchStat("Label", ""), Assert.Single(Assert.IsType<VrWatchStats>(w).Stats)));

        var panel = CreatePanel(content);
        using var renderer = new OverlayRenderer();
        renderer.Render(panel);
    }

    [Fact]
    public void EveryWidgetKindRendersOnTheWatchAndOnAPage()
    {
        var widgets = new VrWatchWidget[]
        {
            new VrWatchText("A status line long enough to wrap onto the second of its two lines on the watch", VrWatchTone.Muted, MaxLines: 2),
            new VrWatchStats([new VrWatchStat("Viewers", "1,204"), new VrWatchStat("Bitrate", "6000 kbps", VrWatchTone.Danger)]),
            new VrWatchMeter("Goal", 0.4, "40%", VrWatchTone.Success),
            new VrWatchButtons([new VrWatchButton("a", "Start", VrWatchButtonStyle.Primary), new VrWatchButton("b", "Stop", VrWatchButtonStyle.Danger), new VrWatchButton("c", "Off", Enabled: false), new VrWatchButton("d", "Next row")]),
            new VrWatchToggle("t", "Enabled", true),
            new VrWatchToggle("u", "Disabled", false, Enabled: false),
            new VrWatchSlider("s", "Level", 3, 0, 10, Step: 0.5, Unit: "s"),
            new VrWatchSlider("broken", "Empty range", 1, 1, 1),
        };
        var panel = CreatePanel(new VrWatchContent
        {
            Cards = [new VrWatchCard("all", widgets, "Everything")],
            Pages = [new VrWatchPage("all", "Everything", widgets, "Status", IconPath: "M12 2L22 22L2 22Z"), new VrWatchPage("bad-icon", "Bad icon", [], IconPath: "not a path")],
        });
        using var renderer = new OverlayRenderer();

        renderer.Render(panel);
        Assert.Equal((int)MathF.Ceiling(panel.Height), renderer.Height);

        Assert.True(panel.OpenPage(WatchContent.PageKey(PluginId, "all")));
        panel.ShowToast("Not connected");
        renderer.Render(panel);
        Assert.Equal((int)MathF.Ceiling(panel.Height), renderer.Height);
        Assert.NotEmpty(renderer.EncodePng());

        Assert.True(panel.OpenPage(WatchContent.PageKey(PluginId, "bad-icon")));
        renderer.Render(panel);
    }

    [Fact]
    public void TheProviderServiceSkipsAPluginThatThrowsAndKeepsTheOthers()
    {
        var working = new FakePlugin("com.example.working", () => Content());
        var broken = new FakePlugin("com.example.broken", () => throw new InvalidOperationException("boom"));
        using var service = CreateService(working, broken, new PlainPlugin());

        var contribution = Assert.Single(service.Poll());

        Assert.Equal("com.example.working", contribution.PluginId);
        Assert.Single(contribution.Content.Cards);
    }

    [Fact]
    public async Task InputsReachThePluginInOrderAndReportWhenSettled()
    {
        var plugin = new FakePlugin(PluginId, () => Content()) { SlowInput = "first" };
        using var service = CreateService(plugin);
        service.Poll();

        service.Dispatch(new WatchInputRequest(PluginId, new VrWatchButtonPressed("first")));
        service.Dispatch(new WatchInputRequest(PluginId, new VrWatchButtonPressed("second")));
        var results = await TakeResultsAsync(service, 2);

        Assert.Equal(["first", "second"], plugin.Received.Select(i => i.ControlId));
        Assert.Equal([false, true], results.Select(r => r.Settled));
        Assert.Equal("Handled first", results[0].Message);
    }

    [Fact]
    public async Task APluginThatFailsAnInputGetsAMessageOnTheWatch()
    {
        var plugin = new FakePlugin(PluginId, () => Content()) { FailInputs = true };
        using var service = CreateService(plugin);
        service.Poll();

        service.Dispatch(new WatchInputRequest(PluginId, new VrWatchButtonPressed("scene:game")));
        var result = Assert.Single(await TakeResultsAsync(service, 1));

        Assert.Equal("Example could not do that", result.Message);
        Assert.True(result.Settled);
    }

    private static WatchProviderService CreateService(params IPlugin[] plugins)
    {
        var host = DispatchProxy.Create<IPluginHost, HostProxy>();
        ((HostProxy)host).Plugins = plugins;
        var services = new ServiceCollection().AddSingleton(host).BuildServiceProvider();
        return new WatchProviderService(services);
    }

    private static async Task<List<WatchInputResult>> TakeResultsAsync(WatchProviderService service, int count)
    {
        var results = new List<WatchInputResult>();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (results.Count < count && DateTime.UtcNow < deadline)
        {
            if (service.TryTakeResult(out var result))
            {
                results.Add(result);
                continue;
            }

            await Task.Delay(5);
        }

        return results;
    }

    public class HostProxy : DispatchProxy
    {
        public IReadOnlyList<IPlugin> Plugins { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            "CreateLogger" => NullLogger.Instance,
            "GetLoadedPlugins" => Plugins,
            _ => throw new NotSupportedException(targetMethod?.Name)
        };
    }

    private class PlainPlugin : IPlugin
    {
        public virtual string Id => "com.example.plain";
        public string Name => "Example";
        public string Version => "1.0.0";
        public string Description => "";
        public void ConfigureServices(IServiceCollection services) { }
        public void Initialize(IServiceProvider serviceProvider) { }
    }

    private sealed class FakePlugin(string id, Func<VrWatchContent> content) : PlainPlugin, IVrWatchProvider
    {
        public override string Id => id;

        /// <summary>An input that takes a while to handle, so handling inputs concurrently would reorder them.</summary>
        public string? SlowInput { get; init; }

        public bool FailInputs { get; init; }

        public ConcurrentQueue<VrWatchInput> Received { get; } = new();

        public VrWatchContent GetWatchContent() => content();

        public async Task<string?> OnWatchInputAsync(VrWatchInput input, CancellationToken cancellationToken)
        {
            if (FailInputs)
            {
                throw new InvalidOperationException("OBS is not connected");
            }

            if (input.ControlId == SlowInput)
            {
                await Task.Delay(50, cancellationToken);
            }

            Received.Enqueue(input);
            return $"Handled {input.ControlId}";
        }
    }
}
