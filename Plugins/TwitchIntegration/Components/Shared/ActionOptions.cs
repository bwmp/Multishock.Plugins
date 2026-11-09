using MultiShock.PluginSdk.Components;

namespace TwitchIntegration.Components.Shared;

internal static class ActionOptions
{
    public static readonly IReadOnlyList<Select<string>.SelectOption<string>> Commands =
    [
        new("Shock", "Shock"),
        new("Vibrate", "Vibrate"),
        new("Beep", "Beep"),
    ];

    public static readonly IReadOnlyList<Select<string>.SelectOption<string>> Modes =
    [
        new(nameof(SelectionMode.All), "All shockers"),
        new(nameof(SelectionMode.Random), "One random shocker"),
        new(nameof(SelectionMode.RoundRobin), "Round robin"),
    ];

    public static readonly IReadOnlyList<Select<string>.SelectOption<string>> Matching =
    [
        new("round", "Round down"),
        new("exact", "Exact amount"),
    ];

    public static SelectionMode ParseMode(string? value) =>
        Enum.TryParse<SelectionMode>(value, out var mode) ? mode : SelectionMode.All;

    public static string Count(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";

    public static string Needs(int count, string what) =>
        $"{count} {(count == 1 ? "needs" : "need")} {what}";

    public static ReactionSummary Summarize(bool enabled, int on, string singular, string plural, params (int Count, string What)[] problems)
    {
        if (!enabled || on == 0) return ReactionSummary.Off;

        var parts = problems.Where(p => p.Count > 0).Select(p => Needs(p.Count, p.What)).ToList();
        return parts.Count == 0
            ? new(ReactionStatus.Ready, Count(on, $"{singular} on", $"{plural} on"))
            : new(ReactionStatus.Attention, string.Join(" · ", [$"{on} on", .. parts]));
    }
}

public enum ReactionStatus
{
    Off,
    Ready,
    Attention,
}

public readonly record struct ReactionSummary(ReactionStatus Status, string Text)
{
    public static readonly ReactionSummary Off = new(ReactionStatus.Off, "Off");
}
