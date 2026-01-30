using System.Text.Json.Serialization;

namespace OBSIntegration.Models;

/// <summary>
/// OBS WebSocket 5.x operation codes
/// </summary>
public static class ObsOpCode
{
    public const int Hello = 0;
    public const int Identify = 1;
    public const int Identified = 2;
    public const int Reidentify = 3;
    public const int Event = 5;
    public const int Request = 6;
    public const int RequestResponse = 7;
    public const int RequestBatch = 8;
    public const int RequestBatchResponse = 9;
}

/// <summary>
/// Base OBS WebSocket message structure
/// </summary>
public class ObsMessage
{
    [JsonPropertyName("op")]
    public int Op { get; set; }

    [JsonPropertyName("d")]
    public object? D { get; set; }
}

/// <summary>
/// Generic message with typed data
/// </summary>
public class ObsMessage<T>
{
    [JsonPropertyName("op")]
    public int Op { get; set; }

    [JsonPropertyName("d")]
    public T? D { get; set; }
}

/// <summary>
/// Hello message from server (op=0)
/// </summary>
public class ObsHelloData
{
    [JsonPropertyName("obsWebSocketVersion")]
    public string ObsWebSocketVersion { get; set; } = "";

    [JsonPropertyName("rpcVersion")]
    public int RpcVersion { get; set; }

    [JsonPropertyName("authentication")]
    public ObsAuthChallenge? Authentication { get; set; }
}

public class ObsAuthChallenge
{
    [JsonPropertyName("challenge")]
    public string Challenge { get; set; } = "";

    [JsonPropertyName("salt")]
    public string Salt { get; set; } = "";
}

/// <summary>
/// Identify message to server (op=1)
/// </summary>
public class ObsIdentifyData
{
    [JsonPropertyName("rpcVersion")]
    public int RpcVersion { get; set; } = 1;

    [JsonPropertyName("authentication")]
    public string? Authentication { get; set; }

    [JsonPropertyName("eventSubscriptions")]
    public int EventSubscriptions { get; set; } = (int)ObsEventSubscription.All;
}

/// <summary>
/// Event subscription flags
/// </summary>
[Flags]
public enum ObsEventSubscription
{
    None = 0,
    General = 1 << 0,
    Config = 1 << 1,
    Scenes = 1 << 2,
    Inputs = 1 << 3,
    Transitions = 1 << 4,
    Filters = 1 << 5,
    Outputs = 1 << 6,
    SceneItems = 1 << 7,
    MediaInputs = 1 << 8,
    Vendors = 1 << 9,
    Ui = 1 << 10,
    All = General | Config | Scenes | Inputs | Transitions | Filters | Outputs | SceneItems | MediaInputs | Vendors | Ui,
}

/// <summary>
/// Identified message from server (op=2)
/// </summary>
public class ObsIdentifiedData
{
    [JsonPropertyName("negotiatedRpcVersion")]
    public int NegotiatedRpcVersion { get; set; }
}

/// <summary>
/// Event message from server (op=5)
/// </summary>
public class ObsEventData
{
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = "";

    [JsonPropertyName("eventIntent")]
    public int EventIntent { get; set; }

    [JsonPropertyName("eventData")]
    public Dictionary<string, object?>? EventData { get; set; }
}

/// <summary>
/// Request message to server (op=6)
/// </summary>
public class ObsRequestData
{
    [JsonPropertyName("requestType")]
    public string RequestType { get; set; } = "";

    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = "";

    [JsonPropertyName("requestData")]
    public Dictionary<string, object?>? RequestData { get; set; }
}

/// <summary>
/// Request response from server (op=7)
/// </summary>
public class ObsRequestResponseData
{
    [JsonPropertyName("requestType")]
    public string RequestType { get; set; } = "";

    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = "";

    [JsonPropertyName("requestStatus")]
    public ObsRequestStatus RequestStatus { get; set; } = new();

    [JsonPropertyName("responseData")]
    public Dictionary<string, object?>? ResponseData { get; set; }
}

public class ObsRequestStatus
{
    [JsonPropertyName("result")]
    public bool Result { get; set; }

    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }
}
