using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwitchIntegration.Models;

public class TwitchWebSocketMessage
{
    [JsonPropertyName("metadata")]
    public MessageMetadata Metadata { get; set; } = new();

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; set; }
}

public class MessageMetadata
{
    [JsonPropertyName("message_id")]
    public string MessageId { get; set; } = "";

    [JsonPropertyName("message_type")]
    public string MessageType { get; set; } = "";

    [JsonPropertyName("message_timestamp")]
    public DateTime MessageTimestamp { get; set; }

    [JsonPropertyName("subscription_type")]
    public string? SubscriptionType { get; set; }

    [JsonPropertyName("subscription_version")]
    public string? SubscriptionVersion { get; set; }
}

public class WelcomePayload
{
    [JsonPropertyName("session")]
    public SessionInfo Session { get; set; } = new();
}

public class SessionInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("connected_at")]
    public DateTime ConnectedAt { get; set; }

    [JsonPropertyName("keepalive_timeout_seconds")]
    public int KeepaliveTimeoutSeconds { get; set; }

    [JsonPropertyName("reconnect_url")]
    public string? ReconnectUrl { get; set; }
}

public class ReconnectPayload
{
    [JsonPropertyName("session")]
    public SessionInfo Session { get; set; } = new();
}

public class NotificationPayload
{
    [JsonPropertyName("subscription")]
    public SubscriptionInfo Subscription { get; set; } = new();

    [JsonPropertyName("event")]
    public JsonElement Event { get; set; }
}

public class SubscriptionInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("condition")]
    public JsonElement Condition { get; set; }

    [JsonPropertyName("transport")]
    public TransportInfo Transport { get; set; } = new();

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("cost")]
    public int Cost { get; set; }
}

public class TransportInfo
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }
}

public class CreateSubscriptionRequest
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("condition")]
    public Dictionary<string, string> Condition { get; set; } = new();

    [JsonPropertyName("transport")]
    public CreateSubscriptionTransport Transport { get; set; } = new();
}

public class CreateSubscriptionTransport
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = "websocket";

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = "";
}

public class TwitchUserResponse
{
    [JsonPropertyName("data")]
    public List<TwitchUser> Data { get; set; } = new();
}

public class TwitchUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("login")]
    public string Login { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("broadcaster_type")]
    public string BroadcasterType { get; set; } = "";

    [JsonPropertyName("profile_image_url")]
    public string ProfileImageUrl { get; set; } = "";
}

public class TokenValidationResponse
{
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = "";

    [JsonPropertyName("login")]
    public string Login { get; set; } = "";

    [JsonPropertyName("scopes")]
    public List<string> Scopes { get; set; } = new();

    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}
