namespace StreamlabsIntegration;

/// <summary>Static endpoints and helpers for the Streamlabs Socket API.</summary>
public static class StreamlabsConstants
{
    /// <summary>Where users obtain their Socket API Token in the Streamlabs dashboard.</summary>
    public const string TokenSettingsUrl = "https://streamlabs.com/dashboard#/settings/api-settings";

    /// <summary>
    /// Builds the websocket URL for the Streamlabs socket server. The socket token is passed as a
    /// query parameter; the server authenticates the connection from it (no separate auth emit).
    /// EIO=3 selects the Engine.IO v3 protocol that the socket.io 2.x server speaks.
    /// </summary>
    public static Uri BuildSocketUri(string socketToken) =>
        new($"wss://sockets.streamlabs.com/socket.io/?token={Uri.EscapeDataString(socketToken)}&EIO=3&transport=websocket");
}
