namespace TikTokIntegration.Protocol;

/// <summary>
/// Decodes TikTok webcast protobuf messages. Field numbers follow the community-documented
/// webcast schema (TikTok.WebcastResponse and friends); unknown fields are skipped so that
/// additions on TikTok's side do not break decoding.
/// </summary>
public static class WebcastDecoder
{
    // WebcastWebsocketMessage { uint64 id = 2; string type = 7; bytes binary = 8; }
    public static WebsocketFrame DecodeWebsocketFrame(ReadOnlySpan<byte> data)
    {
        var frame = new WebsocketFrame();
        var reader = new ProtoReader(data);

        while (reader.TryReadTag(out var field, out var wire))
        {
            switch (field)
            {
                case 2 when wire == WireType.Varint:
                    frame.Id = reader.ReadVarint();
                    break;
                case 7 when wire == WireType.LengthDelimited:
                    frame.Type = reader.ReadString();
                    break;
                case 8 when wire == WireType.LengthDelimited:
                    frame.Payload = reader.ReadLengthDelimited().ToArray();
                    break;
                default:
                    reader.Skip(wire);
                    break;
            }
        }

        return frame;
    }

    // WebcastWebsocketAck { uint64 id = 2; string type = 7; }
    public static byte[] EncodeAck(ulong id)
    {
        var buffer = new List<byte>(16);
        ProtoWriter.WriteVarintField(buffer, 2, id);
        ProtoWriter.WriteString(buffer, 7, "ack");
        return [.. buffer];
    }

    // Static "hb" keep-alive frame (field 7 = "hb")
    public static readonly byte[] PingFrame = [0x3A, 0x02, 0x68, 0x62];

    // WebcastResponse { messages=1 { type=1, binary=2 }, cursor=2, fetchInterval=3,
    //                   internalExt=5, wsParams=7 { name=1, value=2 }, needAck=9, wsUrl=10 }
    public static WebcastResponse DecodeResponse(ReadOnlySpan<byte> data)
    {
        var response = new WebcastResponse();
        var reader = new ProtoReader(data);

        while (reader.TryReadTag(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == WireType.LengthDelimited:
                    response.Messages.Add(DecodeResponseMessage(reader.ReadLengthDelimited()));
                    break;
                case 2 when wire == WireType.LengthDelimited:
                    response.Cursor = reader.ReadString();
                    break;
                case 3 when wire == WireType.Varint:
                    response.FetchInterval = (int)reader.ReadVarint();
                    break;
                case 5 when wire == WireType.LengthDelimited:
                    response.InternalExt = reader.ReadString();
                    break;
                case 7 when wire == WireType.LengthDelimited:
                    var (name, value) = DecodeWsParam(reader.ReadLengthDelimited());
                    if (!string.IsNullOrEmpty(name))
                    {
                        response.WsParams[name] = value;
                    }
                    break;
                case 9 when wire == WireType.Varint:
                    response.NeedAck = reader.ReadVarint() != 0;
                    break;
                case 10 when wire == WireType.LengthDelimited:
                    response.WsUrl = reader.ReadString();
                    break;
                default:
                    reader.Skip(wire);
                    break;
            }
        }

        return response;
    }

    private static WebcastResponseMessage DecodeResponseMessage(ReadOnlySpan<byte> data)
    {
        var message = new WebcastResponseMessage();
        var reader = new ProtoReader(data);

        while (reader.TryReadTag(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == WireType.LengthDelimited:
                    message.Type = reader.ReadString();
                    break;
                case 2 when wire == WireType.LengthDelimited:
                    message.Binary = reader.ReadLengthDelimited().ToArray();
                    break;
                default:
                    reader.Skip(wire);
                    break;
            }
        }

        return message;
    }

    private static (string Name, string Value) DecodeWsParam(ReadOnlySpan<byte> data)
    {
        string name = "", value = "";
        var reader = new ProtoReader(data);

        while (reader.TryReadTag(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == WireType.LengthDelimited:
                    name = reader.ReadString();
                    break;
                case 2 when wire == WireType.LengthDelimited:
                    value = reader.ReadString();
                    break;
                default:
                    reader.Skip(wire);
                    break;
            }
        }

        return (name, value);
    }

    // User { userId=1, nickname=3, profilePicture=9 { urls=1 }, uniqueId=38 }
    public static WebcastUser DecodeUser(ReadOnlySpan<byte> data)
    {
        var user = new WebcastUser();
        var reader = new ProtoReader(data);

        while (reader.TryReadTag(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == WireType.Varint:
                    user.UserId = reader.ReadVarint();
                    break;
                case 3 when wire == WireType.LengthDelimited:
                    user.Nickname = reader.ReadString();
                    break;
                case 9 when wire == WireType.LengthDelimited:
                    user.ProfilePictureUrl = DecodeFirstUrl(reader.ReadLengthDelimited());
                    break;
                case 38 when wire == WireType.LengthDelimited:
                    user.UniqueId = reader.ReadString();
                    break;
                default:
                    reader.Skip(wire);
                    break;
            }
        }

        return user;
    }

    private static string DecodeFirstUrl(ReadOnlySpan<byte> data)
    {
        var reader = new ProtoReader(data);
        while (reader.TryReadTag(out var field, out var wire))
        {
            if (field == 1 && wire == WireType.LengthDelimited)
            {
                return reader.ReadString();
            }
            reader.Skip(wire);
        }
        return "";
    }

    // WebcastMessageEvent { msgId=2, createTime=4, eventDetails=8 { displayType=1, label=2 } }
    public static WebcastEventInfo DecodeEventInfo(ReadOnlySpan<byte> data)
    {
        var info = new WebcastEventInfo();
        var reader = new ProtoReader(data);

        while (reader.TryReadTag(out var field, out var wire))
        {
            switch (field)
            {
                case 2 when wire == WireType.Varint:
                    info.MsgId = reader.ReadVarint();
                    break;
                case 4 when wire == WireType.Varint:
                    info.CreateTime = reader.ReadVarint();
                    break;
                case 8 when wire == WireType.LengthDelimited:
                    DecodeEventDetails(reader.ReadLengthDelimited(), info);
                    break;
                default:
                    reader.Skip(wire);
                    break;
            }
        }

        return info;
    }

    private static void DecodeEventDetails(ReadOnlySpan<byte> data, WebcastEventInfo info)
    {
        var reader = new ProtoReader(data);
        while (reader.TryReadTag(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == WireType.LengthDelimited:
                    info.DisplayType = reader.ReadString();
                    break;
                case 2 when wire == WireType.LengthDelimited:
                    info.Label = reader.ReadString();
                    break;
                default:
                    reader.Skip(wire);
                    break;
            }
        }
    }

    // WebcastChatMessage { event=1, user=2, comment=3 }
    public static WebcastChat DecodeChat(ReadOnlySpan<byte> data)
    {
        var chat = new WebcastChat();
        var reader = new ProtoReader(data);

        while (reader.TryReadTag(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == WireType.LengthDelimited:
                    chat.Event = DecodeEventInfo(reader.ReadLengthDelimited());
                    break;
                case 2 when wire == WireType.LengthDelimited:
                    chat.User = DecodeUser(reader.ReadLengthDelimited());
                    break;
                case 3 when wire == WireType.LengthDelimited:
                    chat.Comment = reader.ReadString();
                    break;
                default:
                    reader.Skip(wire);
                    break;
            }
        }

        return chat;
    }

    // WebcastGiftMessage { event=1, giftId=2, repeatCount=5, user=7, repeatEnd=9, groupId=11,
    //                      giftDetails=15 { giftImage=1 { url=1 }, describe=2, giftType=11,
    //                                       diamondCount=12, giftName=16 } }
    public static WebcastGift DecodeGift(ReadOnlySpan<byte> data)
    {
        var gift = new WebcastGift();
        var reader = new ProtoReader(data);

        while (reader.TryReadTag(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == WireType.LengthDelimited:
                    gift.Event = DecodeEventInfo(reader.ReadLengthDelimited());
                    break;
                case 2 when wire == WireType.Varint:
                    gift.GiftId = (int)reader.ReadVarint();
                    break;
                case 5 when wire == WireType.Varint:
                    gift.RepeatCount = (int)reader.ReadVarint();
                    break;
                case 7 when wire == WireType.LengthDelimited:
                    gift.User = DecodeUser(reader.ReadLengthDelimited());
                    break;
                case 9 when wire == WireType.Varint:
                    gift.RepeatEnd = reader.ReadVarint() != 0;
                    break;
                case 11 when wire == WireType.Varint:
                    gift.GroupId = reader.ReadVarint();
                    break;
                case 15 when wire == WireType.LengthDelimited:
                    DecodeGiftDetails(reader.ReadLengthDelimited(), gift);
                    break;
                default:
                    reader.Skip(wire);
                    break;
            }
        }

        return gift;
    }

    private static void DecodeGiftDetails(ReadOnlySpan<byte> data, WebcastGift gift)
    {
        var reader = new ProtoReader(data);
        while (reader.TryReadTag(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == WireType.LengthDelimited:
                    gift.GiftImageUrl = DecodeFirstUrl(reader.ReadLengthDelimited());
                    break;
                case 2 when wire == WireType.LengthDelimited:
                    gift.Describe = reader.ReadString();
                    break;
                case 11 when wire == WireType.Varint:
                    gift.GiftType = (int)reader.ReadVarint();
                    break;
                case 12 when wire == WireType.Varint:
                    gift.DiamondCount = (int)reader.ReadVarint();
                    break;
                case 16 when wire == WireType.LengthDelimited:
                    gift.GiftName = reader.ReadString();
                    break;
                default:
                    reader.Skip(wire);
                    break;
            }
        }
    }

    // WebcastLikeMessage { event=1, likeCount=2, totalLikeCount=3, user=5 }
    public static WebcastLike DecodeLike(ReadOnlySpan<byte> data)
    {
        var like = new WebcastLike();
        var reader = new ProtoReader(data);

        while (reader.TryReadTag(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == WireType.LengthDelimited:
                    like.Event = DecodeEventInfo(reader.ReadLengthDelimited());
                    break;
                case 2 when wire == WireType.Varint:
                    like.LikeCount = (int)reader.ReadVarint();
                    break;
                case 3 when wire == WireType.Varint:
                    like.TotalLikeCount = (int)reader.ReadVarint();
                    break;
                case 5 when wire == WireType.LengthDelimited:
                    like.User = DecodeUser(reader.ReadLengthDelimited());
                    break;
                default:
                    reader.Skip(wire);
                    break;
            }
        }

        return like;
    }

    // WebcastSocialMessage { event=1, user=2 } — follows and shares, distinguished by displayType
    public static WebcastSocial DecodeSocial(ReadOnlySpan<byte> data)
    {
        var social = new WebcastSocial();
        var reader = new ProtoReader(data);

        while (reader.TryReadTag(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == WireType.LengthDelimited:
                    social.Event = DecodeEventInfo(reader.ReadLengthDelimited());
                    break;
                case 2 when wire == WireType.LengthDelimited:
                    social.User = DecodeUser(reader.ReadLengthDelimited());
                    break;
                default:
                    reader.Skip(wire);
                    break;
            }
        }

        return social;
    }

    // WebcastMemberMessage { event=1, user=2, actionId=10 }
    public static WebcastMember DecodeMember(ReadOnlySpan<byte> data)
    {
        var member = new WebcastMember();
        var reader = new ProtoReader(data);

        while (reader.TryReadTag(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == WireType.LengthDelimited:
                    member.Event = DecodeEventInfo(reader.ReadLengthDelimited());
                    break;
                case 2 when wire == WireType.LengthDelimited:
                    member.User = DecodeUser(reader.ReadLengthDelimited());
                    break;
                case 10 when wire == WireType.Varint:
                    member.ActionId = (int)reader.ReadVarint();
                    break;
                default:
                    reader.Skip(wire);
                    break;
            }
        }

        return member;
    }

    // WebcastRoomUserSeqMessage { viewerCount=3 }
    public static WebcastRoomUserSeq DecodeRoomUserSeq(ReadOnlySpan<byte> data)
    {
        var seq = new WebcastRoomUserSeq();
        var reader = new ProtoReader(data);

        while (reader.TryReadTag(out var field, out var wire))
        {
            if (field == 3 && wire == WireType.Varint)
            {
                seq.ViewerCount = (int)reader.ReadVarint();
            }
            else
            {
                reader.Skip(wire);
            }
        }

        return seq;
    }

    // WebcastControlMessage { action=2 } — action 3 = stream ended
    public static WebcastControl DecodeControl(ReadOnlySpan<byte> data)
    {
        var control = new WebcastControl();
        var reader = new ProtoReader(data);

        while (reader.TryReadTag(out var field, out var wire))
        {
            if (field == 2 && wire == WireType.Varint)
            {
                control.Action = (int)reader.ReadVarint();
            }
            else
            {
                reader.Skip(wire);
            }
        }

        return control;
    }

    // WebcastSubNotifyMessage { event=1, user=2, subMonth=4, subscribeType=5 }
    public static WebcastSubNotify DecodeSubNotify(ReadOnlySpan<byte> data)
    {
        var sub = new WebcastSubNotify();
        var reader = new ProtoReader(data);

        while (reader.TryReadTag(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == WireType.LengthDelimited:
                    sub.Event = DecodeEventInfo(reader.ReadLengthDelimited());
                    break;
                case 2 when wire == WireType.LengthDelimited:
                    sub.User = DecodeUser(reader.ReadLengthDelimited());
                    break;
                case 4 when wire == WireType.Varint:
                    sub.SubMonth = (int)reader.ReadVarint();
                    break;
                case 5 when wire == WireType.Varint:
                    sub.SubscribeType = (int)reader.ReadVarint();
                    break;
                default:
                    reader.Skip(wire);
                    break;
            }
        }

        return sub;
    }

    // WebcastEnvelopeMessage { treasureBoxData=2 { coins=5, canOpen=6 } }
    public static WebcastEnvelope DecodeEnvelope(ReadOnlySpan<byte> data)
    {
        var envelope = new WebcastEnvelope();
        var reader = new ProtoReader(data);

        while (reader.TryReadTag(out var field, out var wire))
        {
            if (field == 2 && wire == WireType.LengthDelimited)
            {
                var inner = new ProtoReader(reader.ReadLengthDelimited());
                while (inner.TryReadTag(out var innerField, out var innerWire))
                {
                    switch (innerField)
                    {
                        case 5 when innerWire == WireType.Varint:
                            envelope.Coins = (int)inner.ReadVarint();
                            break;
                        case 6 when innerWire == WireType.Varint:
                            envelope.CanOpen = (int)inner.ReadVarint();
                            break;
                        default:
                            inner.Skip(innerWire);
                            break;
                    }
                }
            }
            else
            {
                reader.Skip(wire);
            }
        }

        return envelope;
    }
}
