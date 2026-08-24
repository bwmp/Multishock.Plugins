namespace TikTokIntegration.Models;

public sealed class TikTokGiftEvent
{
    public string UserName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public int GiftId { get; init; }
    public string GiftName { get; init; } = "";
    public int DiamondCount { get; init; }
    public int RepeatCount { get; init; }
    public string GiftImageUrl { get; init; } = "";
    public bool IsStreakable { get; init; }
    public bool IsStreakEnd { get; init; }

    /// <summary>Total coin value of the event (per-gift diamond value x repeat count).</summary>
    public int TotalCoins => DiamondCount * Math.Max(1, RepeatCount);
}

public sealed class TikTokChatEvent
{
    public string UserName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Message { get; init; } = "";
}

public sealed class TikTokLikeEvent
{
    public string UserName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public int LikeCount { get; init; }
    public int TotalLikeCount { get; init; }
}

public sealed class TikTokSocialEvent
{
    public string UserName { get; init; } = "";
    public string DisplayName { get; init; } = "";
}

public sealed class TikTokSubscribeEvent
{
    public string UserName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public int SubMonth { get; init; }
}

public sealed class TikTokEnvelopeEvent
{
    public int Coins { get; init; }
}
