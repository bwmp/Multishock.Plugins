namespace StreamElementsIntegration;

/// <summary>Static endpoints and helpers for the StreamElements realtime API.</summary>
public static class StreamElementsConstants
{
    /// <summary>Where users obtain their JWT token in the StreamElements dashboard.</summary>
    public const string TokenSettingsUrl = "https://streamelements.com/dashboard/account/channels";

    /// <summary>
    /// The StreamElements realtime websocket URL. Unlike Streamlabs, no token is passed in the query;
    /// the client emits an <c>authenticate</c> event with the JWT after the socket connects.
    /// EIO=3 selects the Engine.IO v3 protocol the socket.io 2.x server speaks.
    /// </summary>
    public static Uri SocketUri => new("wss://realtime.streamelements.com/socket.io/?EIO=3&transport=websocket");
}
