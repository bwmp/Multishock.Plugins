using System.Globalization;

namespace TwitchIntegration.Models;

public enum AutoMessageTrigger
{
    Cheer,
    Redeem,
    Subscription,
    GiftSubscription,
    Follow
}

public class AutoMessageTemplate
{
    public bool Enabled { get; set; } = true;

    public string Template { get; set; } = "";
}

public class RepeatingAutoMessage
{
    public bool Enabled { get; set; } = false;

    public int IntervalSeconds { get; set; } = 600;

    public string Template { get; set; } =
        "Cheer brackets: {brackets} Send the keyword with some bits for a reaction, e.g. 'Cheer100 keyword'.";
}

public class AutoMessageConfig
{
    public bool Enabled { get; set; } = false;

    public AutoMessageTemplate Cheer { get; set; } = new()
    {
        Template = "{username} cheered {amount} bits! Activating at {intensity} for {duration}"
    };

    public AutoMessageTemplate Redeem { get; set; } = new()
    {
        Template = "{username} redeemed {redeem}! Activating at {intensity} for {duration}"
    };

    public AutoMessageTemplate Subscription { get; set; } = new()
    {
        Template = "{username} subscribed! Activating at {intensity} for {duration}"
    };

    public AutoMessageTemplate GiftSubscription { get; set; } = new()
    {
        Template = "{username} gifted {amount} subs! Activating at {intensity} for {duration}"
    };

    public AutoMessageTemplate Follow { get; set; } = new()
    {
        Template = "{username} followed! Activating at {intensity} for {duration}"
    };

    public RepeatingAutoMessage Repeating { get; set; } = new();

    public AutoMessageTemplate Get(AutoMessageTrigger trigger) => trigger switch
    {
        AutoMessageTrigger.Cheer => Cheer,
        AutoMessageTrigger.Redeem => Redeem,
        AutoMessageTrigger.Subscription => Subscription,
        AutoMessageTrigger.GiftSubscription => GiftSubscription,
        _ => Follow,
    };
}

/// <summary>
/// Raised by a config service once it has fired a device action, carrying the
/// values an auto message template can reference.
/// </summary>
public sealed record AutoMessageActivation(AutoMessageTrigger Trigger, IReadOnlyDictionary<string, string> Values)
{
    public static AutoMessageActivation Create(
        AutoMessageTrigger trigger, string userName, int intensity, double duration,
        params (string Key, string Value)[] extra)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["username"] = userName,
            ["intensity"] = $"{intensity}%",
            ["duration"] = $"{duration.ToString("0.#", CultureInfo.InvariantCulture)}s",
        };

        foreach (var (key, value) in extra)
        {
            values[key] = value;
        }

        return new AutoMessageActivation(trigger, values);
    }
}
