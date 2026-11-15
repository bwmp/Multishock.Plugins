using System.Globalization;
using System.Text.Json;
using ThroneAPI.Client;

namespace ThroneIntegration.Models;

/// <summary>A Throne webhook event as delivered by the relay, flattened for flows and the UI.</summary>
public sealed record ThroneGift(
    string EventId,
    string EventType,
    string CreatorUsername,
    string GifterUsername,
    string Message,
    string ItemName,
    string ItemThumbnailUrl,
    long AmountMinorUnits,
    string Currency,
    bool IsSurpriseGift,
    DateTimeOffset ReceivedAt)
{
    public const string GiftPurchased = "gift_purchased";
    public const string ContributionPurchased = "contribution_purchased";
    public const string GiftCrowdfunded = "gift_crowdfunded";

    // Throne sends money as an integer count of 1/100 currency units (1099 = 10.99).
    public decimal Amount => AmountMinorUnits / 100m;

    public string FormattedAmount => $"{Amount.ToString("0.00", CultureInfo.InvariantCulture)} {Currency}";

    public string TypeLabel => EventType switch
    {
        GiftPurchased => "Gift",
        ContributionPurchased => "Contribution",
        GiftCrowdfunded => "Crowdfund completed",
        _ => EventType,
    };

    public static ThroneGift FromRelayEvent(RelayEvent relayEvent)
    {
        var evt = relayEvent.Event;
        var data = evt.Data;
        // Contributions carry the contributed amount; purchases and crowdfund completions carry the item price.
        var amountField = evt.EventType == ContributionPurchased ? "amount" : "price";

        return new ThroneGift(
            evt.EventId,
            evt.EventType,
            ReadString(data, "creator_username"),
            ReadString(data, "gifter_username") is { Length: > 0 } gifter ? gifter : "Anonymous",
            ReadString(data, "message"),
            ReadString(data, "item_name"),
            ReadString(data, "item_thumbnail_url"),
            data.TryGetProperty(amountField, out var amount) && amount.TryGetInt64(out var units) ? units : 0,
            ReadString(data, "currency"),
            data.TryGetProperty("is_surprise_gift", out var surprise) && surprise.ValueKind == JsonValueKind.True,
            DateTimeOffset.FromUnixTimeSeconds(relayEvent.ReceivedAt));
    }

    private static string ReadString(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
