namespace TwitchIntegration;

public static class TwitchConstants
{
    public const string ClientId = "p8ocxh0kltx07kivciequxrsuqx2ng";

    public const string EventSubWebSocketUrl = "wss://eventsub.wss.twitch.tv/ws";
    public const string EventSubWebSocketUrlLocal = "ws://127.0.0.1:8080/ws";

    public const string HelixApiBaseUrl = "https://api.twitch.tv/helix";
    public const string HelixApiBaseUrlLocal = "http://127.0.0.1:8080";

    public const int OAuthCallbackPort = 8783;

    public static string OAuthRedirectUri => $"http://localhost:{OAuthCallbackPort}/callback";

    public static readonly string[] RequiredScopes =
    [
        "bits:read",                    // channel.cheer
        "channel:read:subscriptions",   // channel.subscribe, channel.subscription.gift, channel.subscription.message
        "moderator:read:followers",     // channel.follow
        "channel:read:hype_train",      // channel.hype_train.*
        "channel:read:redemptions",     // channel.channel_points_custom_reward_redemption.add
        "user:read:chat",               // channel.chat.message
        "user:write:chat",              // send chat messages
    ];

    public static class EventTypes
    {
        public const string Cheer = "channel.cheer";
        public const string Subscribe = "channel.subscribe";
        public const string SubscriptionGift = "channel.subscription.gift";
        public const string SubscriptionMessage = "channel.subscription.message";
        public const string Follow = "channel.follow";
        public const string HypeTrainBegin = "channel.hype_train.begin";
        public const string HypeTrainProgress = "channel.hype_train.progress";
        public const string HypeTrainEnd = "channel.hype_train.end";
        public const string Raid = "channel.raid";
        public const string ChannelPointRedemption = "channel.channel_points_custom_reward_redemption.add";
        public const string ChatMessage = "channel.chat.message";
    }

    public static readonly Dictionary<string, string> EventVersions = new()
    {
        [EventTypes.Cheer] = "1",
        [EventTypes.Subscribe] = "1",
        [EventTypes.SubscriptionGift] = "1",
        [EventTypes.SubscriptionMessage] = "1",
        [EventTypes.Follow] = "2",
        [EventTypes.HypeTrainBegin] = "1",
        [EventTypes.HypeTrainProgress] = "1",
        [EventTypes.HypeTrainEnd] = "1",
        [EventTypes.Raid] = "1",
        [EventTypes.ChatMessage] = "1",
        [EventTypes.ChannelPointRedemption] = "1",
    };
}
