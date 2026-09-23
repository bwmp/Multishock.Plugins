namespace FanslyIntegration;

/// <summary>
/// Endpoints and protocol constants for Fansly.
///
/// NOTE: Fansly does not publish a public API. The values below reflect the endpoints and
/// websocket envelope shape used by the Fansly web client at the time of writing and are the
/// pieces most likely to need updating if Fansly changes their protocol. The message-type
/// opcodes in <see cref="Op"/> in particular should be confirmed against a live capture — the
/// socket client logs any unrecognised frame type so the correct codes can be filled in.
/// </summary>
public static class FanslyConstants
{
    /// <summary>REST API base (used to resolve account IDs and the live chat-room id).</summary>
    public const string ApiBase = "https://apiv3.fansly.com/api/v1";

    /// <summary>Realtime chat/stream websocket endpoint.</summary>
    public const string WebSocketUrl = "wss://wsv3.fansly.com/";

    /// <summary>The site the user visits to grab their account token.</summary>
    public const string SiteHost = "https://fansly.com";

    /// <summary>How often to send a heartbeat frame to keep the socket alive.</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Websocket message-type codes. Frames are JSON envelopes shaped
    /// <c>{ "t": &lt;int&gt;, "d": &lt;string|object&gt; }</c> where <c>d</c> is usually a
    /// stringified JSON payload. Confirm these against live traffic before relying on them.
    /// </summary>
    public static class Op
    {
        /// <summary>Heartbeat/keepalive ping we send periodically.</summary>
        public const int Heartbeat = 0;

        /// <summary>Auth handshake sent right after connect; payload carries the account token.</summary>
        public const int Connect = 1;

        /// <summary>Subscribe to a chat room / live feed; payload carries the chatRoomId.</summary>
        public const int Subscribe = 10001;

        /// <summary>A chat-room message (chat text, and tip-carrying messages).</summary>
        public const int ChatRoomMessage = 10002;

        /// <summary>Account/feed events such as follows and subscriptions.</summary>
        public const int AccountEvent = 20001;
    }
}
