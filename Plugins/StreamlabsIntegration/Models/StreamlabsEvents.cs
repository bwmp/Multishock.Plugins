namespace StreamlabsIntegration.Models;

/// <summary>The normalized Streamlabs event type. Matches the flow node <c>TypeId</c> suffixes.</summary>
public static class StreamlabsEventType
{
    public const string Donation = "donation";
    public const string Subscription = "subscription";
    public const string Follow = "follow";
    public const string Bits = "bits";
    public const string Host = "host";
    public const string Raid = "raid";
    public const string SuperChat = "superchat";
}

/// <summary>
/// A single normalized Streamlabs event. The raw Streamlabs socket payload carries a
/// <c>message</c> array of one or more of these under a shared <c>type</c>.
/// </summary>
public sealed class StreamlabsEvent
{
    /// <summary>Normalized event type (see <see cref="StreamlabsEventType"/>).</summary>
    public string Type { get; init; } = "";

    /// <summary>The raw Streamlabs <c>type</c> string (e.g. "resub", "subscription").</summary>
    public string RawType { get; init; } = "";

    /// <summary>The connected platform the event came from (the socket "for" field), e.g. "twitch_account".</summary>
    public string Platform { get; init; } = "";

    /// <summary>Display name of the user who triggered the event.</summary>
    public string Name { get; init; } = "";

    /// <summary>User-supplied message (donation/sub/bits/superchat), if any.</summary>
    public string Message { get; init; } = "";

    /// <summary>
    /// Numeric amount, meaning depends on <see cref="Type"/>:
    /// money for donation/superchat, bits for bits, months for subscription,
    /// viewers for host, raiders for raid.
    /// </summary>
    public double Amount { get; init; }

    /// <summary>Pre-formatted amount string from Streamlabs (donations/superchats), e.g. "$13.37".</summary>
    public string FormattedAmount { get; init; } = "";

    /// <summary>ISO currency code for monetary events (donation/superchat).</summary>
    public string Currency { get; init; } = "";

    /// <summary>Subscription plan identifier ("1000", "2000", "3000", "Prime"), subscriptions only.</summary>
    public string SubPlan { get; init; } = "";

    /// <summary>Stable event identifier when provided by Streamlabs (used for de-duplication).</summary>
    public string Id { get; init; } = "";
}
