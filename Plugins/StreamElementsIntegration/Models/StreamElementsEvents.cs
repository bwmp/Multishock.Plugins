namespace StreamElementsIntegration.Models;

/// <summary>The normalized StreamElements event type. Matches the flow node <c>TypeId</c> suffixes.</summary>
public static class StreamElementsEventType
{
    public const string Tip = "tip";
    public const string Cheer = "cheer";
    public const string Follow = "follow";
    public const string Subscriber = "subscriber";
    public const string Host = "host";
    public const string Raid = "raid";
    public const string Redemption = "redemption";
}

/// <summary>A single normalized StreamElements event.</summary>
public sealed class StreamElementsEvent
{
    /// <summary>Normalized event type (see <see cref="StreamElementsEventType"/>).</summary>
    public string Type { get; init; } = "";

    /// <summary>The platform the event came from ("twitch", "youtube", "facebook"), when provided.</summary>
    public string Provider { get; init; } = "";

    /// <summary>Display name of the user who triggered the event.</summary>
    public string Name { get; init; } = "";

    /// <summary>User-supplied message (tips/cheers/subs), if any.</summary>
    public string Message { get; init; } = "";

    /// <summary>
    /// Numeric amount, meaning depends on <see cref="Type"/>:
    /// money for tip, bits for cheer, months/streak for subscriber,
    /// viewers for host, raiders for raid, cost for redemption.
    /// </summary>
    public double Amount { get; init; }

    /// <summary>ISO currency code for tips.</summary>
    public string Currency { get; init; } = "";

    /// <summary>Subscription tier ("1000", "2000", "3000", "prime"), subscribers only.</summary>
    public string Tier { get; init; } = "";

    /// <summary>True when the subscription was gifted by another user.</summary>
    public bool Gifted { get; init; }

    /// <summary>The gifter's name when <see cref="Gifted"/> is true.</summary>
    public string Sender { get; init; } = "";

    /// <summary>The redeemed store item name, for redemption events.</summary>
    public string Item { get; init; } = "";

    /// <summary>True when the event came from the dashboard's "Test" button rather than a real event.</summary>
    public bool IsTest { get; init; }
}
