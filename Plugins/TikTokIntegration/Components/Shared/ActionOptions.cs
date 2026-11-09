using System.Globalization;
using MultiShock.PluginSdk.Components;

namespace TikTokIntegration.Components.Shared;

internal static class ActionOptions
{
    public const int MinIntensity = 1;
    public const int MaxIntensity = 100;
    public const double MinDuration = 0.1;
    public const double MaxDuration = 15;

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

    public static SelectionMode ParseMode(string? value) =>
        Enum.TryParse<SelectionMode>(value, out var mode) ? mode : SelectionMode.All;

    public static int ClampIntensity(double? value) =>
        (int)Math.Clamp(Math.Round(value ?? MinIntensity), MinIntensity, MaxIntensity);

    public static double ClampDuration(double? value) =>
        Math.Round(Math.Clamp(value ?? 1, MinDuration, MaxDuration), 1);

    public static int ClampAmount(double? value) =>
        (int)Math.Clamp(Math.Round(value ?? 1), 1, 1_000_000);

    public static string Count(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";

    public static string Seconds(double seconds) =>
        seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";

    public static string Describe(string command, int intensity, double duration, int shockerCount) =>
        shockerCount == 0
            ? $"{command} {intensity}% for {Seconds(duration)} · no shockers selected"
            : $"{command} {intensity}% for {Seconds(duration)} on {Count(shockerCount, "shocker", "shockers")}";

    public static string Needs(int count, string what) =>
        $"{count} {(count == 1 ? "needs" : "need")} {what}";

    public static ReactionSummary SummarizeBrackets(bool enabled, int bracketCount, int shockerCount)
    {
        if (!enabled) return ReactionSummary.Off;
        if (bracketCount == 0) return new(ReactionStatus.Attention, "No brackets");
        if (shockerCount == 0) return new(ReactionStatus.Attention, "No shockers");
        return new(ReactionStatus.Ready, Count(bracketCount, "bracket", "brackets"));
    }

    public static ReactionSummary SummarizeEvents(IReadOnlyCollection<SimpleActionConfig> configs)
    {
        var on = configs.Count(c => c.Enabled);
        if (on == 0) return ReactionSummary.Off;

        var needShockers = configs.Count(c => c.Enabled && c.SelectedShockerIds.Count == 0);
        return needShockers == 0
            ? new(ReactionStatus.Ready, $"{on} of {configs.Count} on")
            : new(ReactionStatus.Attention, $"{on} on · {Needs(needShockers, "shockers")}");
    }

    public static ReactionSummary SummarizeRules(bool enabled, int ruleCount, int rulesOn, params (int Count, string What)[] problems)
    {
        if (!enabled) return ReactionSummary.Off;
        if (ruleCount == 0) return new(ReactionStatus.Off, "No rules");
        if (rulesOn == 0) return new(ReactionStatus.Attention, "No rules on");

        var parts = problems.Where(p => p.Count > 0).Select(p => Needs(p.Count, p.What)).ToList();
        return parts.Count == 0
            ? new(ReactionStatus.Ready, Count(rulesOn, "rule on", "rules on"))
            : new(ReactionStatus.Attention, string.Join(" · ", [$"{rulesOn} on", .. parts]));
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
