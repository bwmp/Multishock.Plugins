namespace TikTokIntegration.Protocol;

// Decoded representations of the TikTok webcast protobuf messages.
// Field numbers are documented in WebcastDecoder; these are plain CLR models.

public sealed class WebsocketFrame
{
    public ulong Id { get; set; }
    public string Type { get; set; } = "";
    public byte[] Payload { get; set; } = [];
}

public sealed class WebcastResponse
{
    public List<WebcastResponseMessage> Messages { get; } = [];
    public string Cursor { get; set; } = "";
    public int FetchInterval { get; set; }
    public string InternalExt { get; set; } = "";
    public Dictionary<string, string> WsParams { get; } = [];
    public bool NeedAck { get; set; }
    public string WsUrl { get; set; } = "";
}

public sealed class WebcastResponseMessage
{
    public string Type { get; set; } = "";
    public byte[] Binary { get; set; } = [];
}

public sealed class WebcastUser
{
    public ulong UserId { get; set; }
    public string Nickname { get; set; } = "";
    public string UniqueId { get; set; } = "";
    public string ProfilePictureUrl { get; set; } = "";
}

public sealed class WebcastEventInfo
{
    public ulong MsgId { get; set; }
    public ulong CreateTime { get; set; }
    public string DisplayType { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class WebcastChat
{
    public WebcastEventInfo Event { get; set; } = new();
    public WebcastUser User { get; set; } = new();
    public string Comment { get; set; } = "";
}

public sealed class WebcastGift
{
    public WebcastEventInfo Event { get; set; } = new();
    public int GiftId { get; set; }
    public int RepeatCount { get; set; }
    public WebcastUser User { get; set; } = new();
    public bool RepeatEnd { get; set; }
    public ulong GroupId { get; set; }
    public string GiftName { get; set; } = "";
    public string Describe { get; set; } = "";
    public int GiftType { get; set; }
    public int DiamondCount { get; set; }
    public string GiftImageUrl { get; set; } = "";

    /// <summary>Type-1 gifts can be sent in streaks (combos) and repeat until RepeatEnd is set.</summary>
    public bool IsStreakable => GiftType == 1;
}

public sealed class WebcastLike
{
    public WebcastEventInfo Event { get; set; } = new();
    public WebcastUser User { get; set; } = new();
    public int LikeCount { get; set; }
    public int TotalLikeCount { get; set; }
}

public sealed class WebcastSocial
{
    public WebcastEventInfo Event { get; set; } = new();
    public WebcastUser User { get; set; } = new();
}

public sealed class WebcastMember
{
    public WebcastEventInfo Event { get; set; } = new();
    public WebcastUser User { get; set; } = new();
    public int ActionId { get; set; }
}

public sealed class WebcastRoomUserSeq
{
    public int ViewerCount { get; set; }
}

public sealed class WebcastControl
{
    public int Action { get; set; }
}

public sealed class WebcastSubNotify
{
    public WebcastEventInfo Event { get; set; } = new();
    public WebcastUser User { get; set; } = new();
    public int SubMonth { get; set; }
    public int SubscribeType { get; set; }
}

public sealed class WebcastEnvelope
{
    public int Coins { get; set; }
    public int CanOpen { get; set; }
}
