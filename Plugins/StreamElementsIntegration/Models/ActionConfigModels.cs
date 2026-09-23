namespace StreamElementsIntegration.Models;

/// <summary>How multiple selected shockers are chosen when an action fires.</summary>
public enum SelectionMode
{
    All,
    Random,
    RoundRobin
}

/// <summary>An amount threshold mapped to an action strength (bigger amount → stronger action).</summary>
public class AmountBracket
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The minimum event amount (money/bits/months/…) this bracket applies to.</summary>
    public double Threshold { get; set; } = 1;

    public int Intensity { get; set; } = 50;
    public double Duration { get; set; } = 1.0;
}

/// <summary>
/// One keyword-matched configuration within an event. When an event carries a message, the first
/// enabled section whose keyword appears in the message wins; a section with an empty keyword is the
/// default fallback. Mirrors the Twitch cheer "section" model.
/// </summary>
public class EventSection
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public bool Enabled { get; set; } = true;

    public string Name { get; set; } = "Default";

    /// <summary>Optional keyword; when set, this section only matches events whose message contains it.</summary>
    public string Keyword { get; set; } = "";

    public string CommandType { get; set; } = "Vibrate";
    public bool WarningVibrate { get; set; } = false;
    public SelectionMode Mode { get; set; } = SelectionMode.All;
    public List<string> SelectedShockerIds { get; set; } = new();

    /// <summary>Single-action strength used for events with no amount (e.g. follows).</summary>
    public int Intensity { get; set; } = 30;
    public double Duration { get; set; } = 1.0;

    /// <summary>When true, a bracket must match the amount exactly; otherwise the amount rounds down to the nearest bracket.</summary>
    public bool FixedAmount { get; set; } = false;

    /// <summary>Amount thresholds for amount-bearing events (tips, bits, subs, …).</summary>
    public List<AmountBracket> Brackets { get; set; } = new();
}

/// <summary>Per-event action configuration: a master enable plus one or more keyword-matched sections.</summary>
public class EventActionConfig
{
    /// <summary>The event type this config applies to (see <see cref="StreamElementsEventType"/>).</summary>
    public string Type { get; set; } = "";

    /// <summary>Master switch for the whole event; when false, no section fires.</summary>
    public bool Enabled { get; set; } = false;

    public List<EventSection> Sections { get; set; } = new();
}

/// <summary>All per-event action configs, keyed by event type.</summary>
public class ActionsConfig
{
    public Dictionary<string, EventActionConfig> Events { get; set; } = new();
}

/// <summary>Static UI/behaviour metadata for each StreamElements event type.</summary>
public sealed record EventTypeInfo(string Type, string DisplayName, bool HasAmount, string AmountLabel, string Icon, bool HasKeyword = false);

public static class StreamElementsEventCatalog
{
    public static readonly IReadOnlyList<EventTypeInfo> All =
    [
        new(StreamElementsEventType.Tip, "Tip", true, "Amount", "dollar-sign", HasKeyword: true),
        new(StreamElementsEventType.Cheer, "Cheer / Bits", true, "Bits", "gem", HasKeyword: true),
        new(StreamElementsEventType.Follow, "Follow", false, "", "user-plus"),
        new(StreamElementsEventType.Subscriber, "Subscriber", true, "Months", "star", HasKeyword: true),
        new(StreamElementsEventType.Host, "Host", true, "Viewers", "tv"),
        new(StreamElementsEventType.Raid, "Raid", true, "Raiders", "swords"),
        new(StreamElementsEventType.Redemption, "Store Redemption", true, "Cost", "gift", HasKeyword: true),
    ];
}
