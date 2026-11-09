using StreamlabsIntegration.Models;

namespace StreamlabsIntegration.Components.Shared;

public enum ReactionState
{
    Off,
    Ready,
    NeedsAttention,
}

internal static class ReactionStatus
{
    public static string? SectionIssue(EventSection section, EventTypeInfo info)
    {
        if (section.SelectedShockerIds.Count == 0) return "No shockers";
        return null;
    }

    public static (ReactionState State, string Text) Summarize(EventActionConfig cfg, EventTypeInfo info)
    {
        if (!cfg.Enabled) return (ReactionState.Off, "Off");

        var on = cfg.Sections.Where(s => s.Enabled).ToList();
        if (on.Count == 0)
            return (ReactionState.NeedsAttention, cfg.Sections.Count == 0 ? "No sections" : "All sections off");

        var needShockers = on.Count(s => s.SelectedShockerIds.Count == 0);
        if (needShockers == 0)
            return (ReactionState.Ready, $"{Count(on.Count, "section", "sections")} ready");

        var parts = new List<string> { $"{on.Count} on" };
        if (needShockers > 0) parts.Add($"{needShockers} {(needShockers == 1 ? "needs" : "need")} shockers");
        return (ReactionState.NeedsAttention, string.Join(" · ", parts));
    }

    public static string Count(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";
}
