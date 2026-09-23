namespace JoystickIntegration;

/// <summary>
/// Endpoints and protocol constants for the joystick.tv Bot API.
/// Reference: https://joystick.tv developer docs (@joysticktv on GitHub).
/// </summary>
public static class JoystickConstants
{
    /// <summary>Site host that serves the OAuth authorize page the streamer visits.</summary>
    public const string SiteHost = "https://joystick.tv";

    /// <summary>API host for token exchange, REST endpoints, and the websocket.</summary>
    public const string ApiHost = "https://api.joystick.tv";

    /// <summary>OAuth2 authorize endpoint (browser redirect target).</summary>
    public const string AuthorizeUrl = SiteHost + "/api/oauth/authorize";

    /// <summary>OAuth2 token endpoint (authorization_code and refresh_token grants).</summary>
    public const string TokenUrl = ApiHost + "/api/oauth/token";

    /// <summary>ActionCable websocket endpoint. Token query param is Base64("clientId:clientSecret").</summary>
    public const string CableUrl = ApiHost + "/cable";

    /// <summary>Endpoint used to inject sample events into the bot for testing (basic-auth).</summary>
    public const string EchoUrl = ApiHost + "/echo";

    /// <summary>The single ActionCable channel all bot events flow through.</summary>
    public const string GatewayIdentifier = "{\"channel\":\"GatewayChannel\"}";

    /// <summary>Websocket subprotocol required by ActionCable.</summary>
    public const string CableSubProtocol = "actioncable-v1-json";

    /// <summary>
    /// Loopback port the plugin listens on to catch the OAuth authorization code.
    /// The streamer must set this exact redirect URL when creating their bot application.
    /// </summary>
    public const int OAuthCallbackPort = 8087;

    public static string OAuthRedirectUri => $"http://127.0.0.1:{OAuthCallbackPort}/auth";
}
