using MultiShock.PluginSdk.VrWatch;

namespace SteamVROverlay.Overlay;

/// <summary>
/// What another plugin shows on the watch, tagged with the plugin it came from.
/// </summary>
public sealed record WatchContribution(string PluginId, string PluginName, VrWatchContent Content);

/// <summary>
/// A control on another plugin's content was used.
/// </summary>
public sealed record WatchInputRequest(string PluginId, VrWatchInput Input);

/// <summary>
/// The outcome of a <see cref="WatchInputRequest"/>. <see cref="Settled"/> is true once the
/// plugin has no inputs left in flight, so its content can be trusted again.
/// </summary>
public sealed record WatchInputResult(string PluginId, string? Message, bool Settled);

public static class WatchContent
{
    /// <summary>Key of a plugin's card, used to hide it and to find it again.</summary>
    public static string CardKey(string pluginId, string cardId) => $"card:{pluginId}/{cardId}";

    /// <summary>Key of a plugin's page, used to hide it and to open it.</summary>
    public static string PageKey(string pluginId, string pageId) => $"page:{pluginId}/{pageId}";

    /// <summary>
    /// Content comes from other plugins, so nulls the annotations rule out can still arrive.
    /// Drops null and unknown widgets and controls without an id, and replaces missing text,
    /// so layout and drawing never have to guard against them.
    /// </summary>
    public static VrWatchContent Normalize(VrWatchContent content) => new()
    {
        Cards =
        [
            .. (content.Cards ?? []).OfType<VrWatchCard>()
                .Where(card => card.Id is not null)
                .Select(card => card with { Widgets = Normalize(card.Widgets) })
        ],
        Pages =
        [
            .. (content.Pages ?? []).OfType<VrWatchPage>()
                .Where(page => page.Id is not null)
                .Select(page => page with { Title = page.Title ?? "", Widgets = Normalize(page.Widgets) })
        ],
    };

    private static IReadOnlyList<VrWatchWidget> Normalize(IReadOnlyList<VrWatchWidget>? widgets) =>
        [.. (widgets ?? []).Select(Normalize).OfType<VrWatchWidget>()];

    private static VrWatchWidget? Normalize(VrWatchWidget? widget) => widget switch
    {
        VrWatchText text => text with { Text = text.Text ?? "" },
        VrWatchStats stats => stats with
        {
            Stats = [.. (stats.Stats ?? []).OfType<VrWatchStat>().Select(stat => stat with { Label = stat.Label ?? "", Value = stat.Value ?? "" })]
        },
        VrWatchMeter meter => meter with { Label = meter.Label ?? "" },
        VrWatchButtons buttons => buttons with
        {
            Buttons = [.. (buttons.Buttons ?? []).OfType<VrWatchButton>().Where(b => b.Id is not null).Select(b => b with { Label = b.Label ?? "" })]
        },
        VrWatchToggle { Id: not null } toggle => toggle with { Label = toggle.Label ?? "" },
        VrWatchSlider { Id: not null } slider => slider with { Label = slider.Label ?? "" },
        _ => null
    };

    /// <summary>
    /// Structural equality. Records compare their lists by reference, and providers build new
    /// lists on every call, so the default equality would redraw the watch on every poll.
    /// </summary>
    public static bool Same(IReadOnlyList<WatchContribution> a, IReadOnlyList<WatchContribution> b) =>
        a.Count == b.Count && a.Zip(b).All(pair =>
            pair.First.PluginId == pair.Second.PluginId &&
            pair.First.PluginName == pair.Second.PluginName &&
            SameList(pair.First.Content.Cards, pair.Second.Content.Cards, Same) &&
            SameList(pair.First.Content.Pages, pair.Second.Content.Pages, Same));

    private static bool Same(VrWatchCard a, VrWatchCard b) =>
        a.Id == b.Id && a.Title == b.Title && SameList(a.Widgets, b.Widgets, Same);

    private static bool Same(VrWatchPage a, VrWatchPage b) =>
        a.Id == b.Id && a.Title == b.Title && a.Status == b.Status && a.IconPath == b.IconPath &&
        SameList(a.Widgets, b.Widgets, Same);

    private static bool Same(VrWatchWidget a, VrWatchWidget b) => (a, b) switch
    {
        (VrWatchStats x, VrWatchStats y) => x.Stats.SequenceEqual(y.Stats),
        (VrWatchButtons x, VrWatchButtons y) => x.Buttons.SequenceEqual(y.Buttons),
        _ => a.Equals(b)
    };

    private static bool SameList<T>(IReadOnlyList<T> a, IReadOnlyList<T> b, Func<T, T, bool> same) =>
        a.Count == b.Count && a.Zip(b).All(pair => same(pair.First, pair.Second));
}
