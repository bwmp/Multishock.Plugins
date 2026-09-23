namespace FanslyIntegration.Models;

/// <summary>How multiple selected shockers are chosen when an event fires.</summary>
public enum SelectionMode
{
    All,
    Random,
    RoundRobin
}

/// <summary>A tip event. Amount is in whole tokens (Fansly reports tips in cents; we normalise).</summary>
public sealed class FanslyTipEvent
{
    public string UserName { get; init; } = "";
    public string DisplayName { get; init; } = "";

    /// <summary>Tip amount in Fansly tokens.</summary>
    public int Amount { get; init; }

    /// <summary>Optional message attached to the tip.</summary>
    public string Message { get; init; } = "";
}

public sealed class FanslyChatEvent
{
    public string UserName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Text { get; init; } = "";

    /// <summary>Parsed bot command (e.g. "shock" for "!shock 5"), if the message starts with '!'.</summary>
    public string BotCommand { get; init; } = "";
    public string BotCommandArg { get; init; } = "";

    public string SenderId { get; init; } = "";
    public string MessageId { get; init; } = "";
}

public sealed class FanslyFollowEvent
{
    public string UserName { get; init; } = "";
    public string DisplayName { get; init; } = "";
}

public sealed class FanslySubscribeEvent
{
    public string UserName { get; init; } = "";
    public string DisplayName { get; init; } = "";

    /// <summary>Subscription tier name, if provided.</summary>
    public string Tier { get; init; } = "";

    /// <summary>True when the subscription was gifted by another user.</summary>
    public bool IsGifted { get; init; }
}

/// <summary>Stream lifecycle state.</summary>
public enum FanslyStreamState
{
    Started,
    Ended
}

public sealed class FanslyStreamStateEvent
{
    public FanslyStreamState State { get; init; }
}
