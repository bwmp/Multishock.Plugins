using System.Text.Json.Serialization;

namespace TwitchIntegrationPlugin.Models;

#region Base Event Types

public abstract class TwitchEventBase
{
    [JsonPropertyName("broadcaster_user_id")]
    public string BroadcasterUserId { get; set; } = "";

    [JsonPropertyName("broadcaster_user_login")]
    public string BroadcasterUserLogin { get; set; } = "";

    [JsonPropertyName("broadcaster_user_name")]
    public string BroadcasterUserName { get; set; } = "";
}

public abstract class TwitchUserEventBase : TwitchEventBase
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("user_login")]
    public string UserLogin { get; set; } = "";

    [JsonPropertyName("user_name")]
    public string UserName { get; set; } = "";
}

#endregion

#region Cheer Events

public class CheerEvent : TwitchUserEventBase
{
    [JsonPropertyName("is_anonymous")]
    public bool IsAnonymous { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("bits")]
    public int Bits { get; set; }
}

#endregion

#region Chat Message Events

public class ChatMessageEvent : TwitchUserEventBase
{
    [JsonPropertyName("message_id")]
    public string MessageId { get; set; } = "";

    // Twitch channel.chat.message uses chatter_* fields for the author.
    // Map them onto the base UserId/UserLogin/UserName so existing code works.
    [JsonPropertyName("chatter_user_id")]
    public string ChatterUserId
    {
        get => UserId;
        set => UserId = value ?? string.Empty;
    }

    [JsonPropertyName("chatter_user_login")]
    public string ChatterUserLogin
    {
        get => UserLogin;
        set => UserLogin = value ?? string.Empty;
    }

    [JsonPropertyName("chatter_user_name")]
    public string ChatterUserName
    {
        get => UserName;
        set => UserName = value ?? string.Empty;
    }

    [JsonPropertyName("message")]
    public ChatMessage Message { get; set; } = new();

    [JsonPropertyName("color")]
    public string Color { get; set; } = "";

    [JsonPropertyName("badges")]
    public List<ChatBadge> Badges { get; set; } = new();

    [JsonPropertyName("cheer")]
    public ChatCheer? Cheer { get; set; }

    [JsonIgnore]
    public bool IsModerator => Badges.Any(b => b.SetId == "moderator");

    [JsonIgnore]
    public bool IsSubscriber => Badges.Any(b => b.SetId == "subscriber");

    [JsonIgnore]
    public bool IsVip => Badges.Any(b => b.SetId == "vip");
}

public class ChatMessage
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("fragments")]
    public List<ChatFragment> Fragments { get; set; } = new();
}

public class ChatBadge
{
    [JsonPropertyName("set_id")]
    public string SetId { get; set; } = "";

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("info")]
    public string Info { get; set; } = "";
}

public class ChatFragment
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("cheermote")]
    public ChatCheermote? Cheermote { get; set; }

    [JsonPropertyName("emote")]
    public ChatEmote? Emote { get; set; }
}

public class ChatCheermote
{
    [JsonPropertyName("prefix")]
    public string Prefix { get; set; } = "";

    [JsonPropertyName("bits")]
    public int Bits { get; set; }

    [JsonPropertyName("tier")]
    public int Tier { get; set; }
}

public class ChatEmote
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("emote_set_id")]
    public string EmoteSetId { get; set; } = "";
}

public class ChatCheer
{
    [JsonPropertyName("bits")]
    public int Bits { get; set; }
}

#endregion

#region Subscription Events

public class SubscribeEvent : TwitchUserEventBase
{
    [JsonPropertyName("tier")]
    public string Tier { get; set; } = "";

    [JsonPropertyName("is_gift")]
    public bool IsGift { get; set; }

    [JsonIgnore]
    public int TierNumber => Tier switch
    {
        "1000" => 1,
        "2000" => 2,
        "3000" => 3,
        _ => 1
    };
}

public class SubscriptionGiftEvent : TwitchUserEventBase
{
    [JsonPropertyName("tier")]
    public string Tier { get; set; } = "";

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("cumulative_total")]
    public int? CumulativeTotal { get; set; }

    [JsonPropertyName("is_anonymous")]
    public bool IsAnonymous { get; set; }

    [JsonIgnore]
    public int TierNumber => Tier switch
    {
        "1000" => 1,
        "2000" => 2,
        "3000" => 3,
        _ => 1
    };
}

public class SubscriptionMessageEvent : TwitchUserEventBase
{
    [JsonPropertyName("tier")]
    public string Tier { get; set; } = "";

    [JsonPropertyName("message")]
    public SubscriptionMessage Message { get; set; } = new();

    [JsonPropertyName("cumulative_months")]
    public int CumulativeMonths { get; set; }

    [JsonPropertyName("streak_months")]
    public int? StreakMonths { get; set; }

    [JsonPropertyName("duration_months")]
    public int DurationMonths { get; set; }

    [JsonIgnore]
    public int TierNumber => Tier switch
    {
        "1000" => 1,
        "2000" => 2,
        "3000" => 3,
        _ => 1
    };
}

public class SubscriptionMessage
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("emotes")]
    public List<MessageEmote>? Emotes { get; set; }
}

public class MessageEmote
{
    [JsonPropertyName("begin")]
    public int Begin { get; set; }

    [JsonPropertyName("end")]
    public int End { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
}

#endregion

#region Follow Events

public class FollowEvent : TwitchUserEventBase
{
    [JsonPropertyName("followed_at")]
    public DateTime FollowedAt { get; set; }
}

#endregion

#region Hype Train Events

public abstract class HypeTrainEventBase : TwitchEventBase
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("started_at")]
    public DateTime StartedAt { get; set; }

    [JsonPropertyName("top_contributions")]
    public List<HypeTrainContribution>? TopContributions { get; set; }
}

public class HypeTrainBeginEvent : HypeTrainEventBase
{
    [JsonPropertyName("goal")]
    public int Goal { get; set; }

    [JsonPropertyName("progress")]
    public int Progress { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("last_contribution")]
    public HypeTrainContribution? LastContribution { get; set; }
}

public class HypeTrainProgressEvent : HypeTrainEventBase
{
    [JsonPropertyName("goal")]
    public int Goal { get; set; }

    [JsonPropertyName("progress")]
    public int Progress { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("last_contribution")]
    public HypeTrainContribution? LastContribution { get; set; }
}

public class HypeTrainEndEvent : HypeTrainEventBase
{
    [JsonPropertyName("ended_at")]
    public DateTime EndedAt { get; set; }

    [JsonPropertyName("cooldown_ends_at")]
    public DateTime CooldownEndsAt { get; set; }
}

public class HypeTrainContribution
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("user_login")]
    public string UserLogin { get; set; } = "";

    [JsonPropertyName("user_name")]
    public string UserName { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

#endregion

#region Raid Events

public class RaidEvent : TwitchEventBase
{
    [JsonPropertyName("from_broadcaster_user_id")]
    public string FromBroadcasterUserId { get; set; } = "";

    [JsonPropertyName("from_broadcaster_user_login")]
    public string FromBroadcasterUserLogin { get; set; } = "";

    [JsonPropertyName("from_broadcaster_user_name")]
    public string FromBroadcasterUserName { get; set; } = "";

    [JsonPropertyName("to_broadcaster_user_id")]
    public string ToBroadcasterUserId { get; set; } = "";

    [JsonPropertyName("to_broadcaster_user_login")]
    public string ToBroadcasterUserLogin { get; set; } = "";

    [JsonPropertyName("to_broadcaster_user_name")]
    public string ToBroadcasterUserName { get; set; } = "";

    [JsonPropertyName("viewers")]
    public int Viewers { get; set; }
}

#endregion

#region Channel Point Redemption Events

public class ChannelPointRedemptionEvent : TwitchUserEventBase
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("user_input")]
    public string UserInput { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("reward")]
    public ChannelPointReward Reward { get; set; } = new();

    [JsonPropertyName("redeemed_at")]
    public DateTime RedeemedAt { get; set; }
}

public class ChannelPointReward
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("cost")]
    public int Cost { get; set; }

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = "";
}

#endregion
