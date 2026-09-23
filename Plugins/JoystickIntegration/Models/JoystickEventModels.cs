namespace JoystickIntegration.Models;

/// <summary>How multiple selected shockers are chosen when an event fires.</summary>
public enum SelectionMode
{
    All,
    Random,
    RoundRobin
}

/// <summary>A tip (token) event. Tip-menu tips also carry the item name.</summary>
public sealed class JoystickTipEvent
{
    public string UserName { get; init; } = "";
    public int Tokens { get; init; }

    /// <summary>The tip-menu item name, if the tip came from a tip menu (else empty).</summary>
    public string TipMenuItem { get; init; } = "";

    public bool IsTipMenu => !string.IsNullOrEmpty(TipMenuItem);
}

public sealed class JoystickChatEvent
{
    public string UserName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Text { get; init; } = "";

    /// <summary>Parsed bot command (e.g. "timer" for "!timer 5m"), if any.</summary>
    public string BotCommand { get; init; } = "";
    public string BotCommandArg { get; init; } = "";

    public string ChannelId { get; init; } = "";
    public string MessageId { get; init; } = "";
    public bool IsStreamer { get; init; }
    public bool IsModerator { get; init; }
    public bool IsSubscriber { get; init; }
}

public sealed class JoystickFollowEvent
{
    public string UserName { get; init; } = "";
}

public sealed class JoystickSubscribeEvent
{
    public string UserName { get; init; } = "";

    /// <summary>Number of gifted subs when the event is a gifted-subscription batch (else 1).</summary>
    public int Quantity { get; init; } = 1;

    /// <summary>True when the subscription(s) were gifted by another user.</summary>
    public bool IsGifted { get; init; }
}

public sealed class JoystickWheelSpinEvent
{
    public string UserName { get; init; } = "";
    public string Prize { get; init; } = "";
    public int Tokens { get; init; }
}

/// <summary>A viewer entering or leaving the chat.</summary>
public sealed class JoystickPresenceEvent
{
    public string UserName { get; init; } = "";
    public bool Entered { get; init; }
}

public sealed class JoystickTipGoalEvent
{
    public string UserName { get; init; } = "";
    public int Tokens { get; init; }
}

/// <summary>Stream lifecycle state derived from StreamEvent types.</summary>
public enum JoystickStreamState
{
    Started,
    Ending,
    Resuming,
    Ended
}

public sealed class JoystickStreamStateEvent
{
    public JoystickStreamState State { get; init; }
}
