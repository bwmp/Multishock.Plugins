using System.Text.Json;
using System.Text.Json.Serialization;

namespace OBSIntegration.Models;

/// <summary>
/// OBS event type constants
/// </summary>
public static class ObsEventTypes
{
    // Scene events
    public const string CurrentProgramSceneChanged = "CurrentProgramSceneChanged";
    public const string CurrentPreviewSceneChanged = "CurrentPreviewSceneChanged";
    public const string SceneListChanged = "SceneListChanged";
    public const string SceneCreated = "SceneCreated";
    public const string SceneRemoved = "SceneRemoved";
    public const string SceneNameChanged = "SceneNameChanged";

    // Scene item events
    public const string SceneItemCreated = "SceneItemCreated";
    public const string SceneItemRemoved = "SceneItemRemoved";
    public const string SceneItemListReindexed = "SceneItemListReindexed";
    public const string SceneItemEnableStateChanged = "SceneItemEnableStateChanged";
    public const string SceneItemLockStateChanged = "SceneItemLockStateChanged";
    public const string SceneItemSelected = "SceneItemSelected";

    // Input events
    public const string InputCreated = "InputCreated";
    public const string InputRemoved = "InputRemoved";
    public const string InputNameChanged = "InputNameChanged";
    public const string InputMuteStateChanged = "InputMuteStateChanged";
    public const string InputVolumeChanged = "InputVolumeChanged";

    // Filter events
    public const string SourceFilterCreated = "SourceFilterCreated";
    public const string SourceFilterRemoved = "SourceFilterRemoved";
    public const string SourceFilterNameChanged = "SourceFilterNameChanged";
    public const string SourceFilterEnableStateChanged = "SourceFilterEnableStateChanged";

    // Transition events
    public const string CurrentSceneTransitionChanged = "CurrentSceneTransitionChanged";
    public const string CurrentSceneTransitionDurationChanged = "CurrentSceneTransitionDurationChanged";
    public const string SceneTransitionStarted = "SceneTransitionStarted";
    public const string SceneTransitionEnded = "SceneTransitionEnded";

    // Output events
    public const string StreamStateChanged = "StreamStateChanged";
    public const string RecordStateChanged = "RecordStateChanged";
    public const string ReplayBufferStateChanged = "ReplayBufferStateChanged";
    public const string VirtualcamStateChanged = "VirtualcamStateChanged";

    // Media events
    public const string MediaInputPlaybackStarted = "MediaInputPlaybackStarted";
    public const string MediaInputPlaybackEnded = "MediaInputPlaybackEnded";
    public const string MediaInputActionTriggered = "MediaInputActionTriggered";
}

/// <summary>
/// OBS request type constants
/// </summary>
public static class ObsRequestTypes
{
    // General
    public const string GetVersion = "GetVersion";
    public const string GetStats = "GetStats";
    public const string BroadcastCustomEvent = "BroadcastCustomEvent";
    public const string CallVendorRequest = "CallVendorRequest";
    public const string GetHotkeyList = "GetHotkeyList";
    public const string TriggerHotkeyByName = "TriggerHotkeyByName";
    public const string TriggerHotkeyByKeySequence = "TriggerHotkeyByKeySequence";
    public const string Sleep = "Sleep";

    // Config
    public const string GetPersistentData = "GetPersistentData";
    public const string SetPersistentData = "SetPersistentData";
    public const string GetSceneCollectionList = "GetSceneCollectionList";
    public const string SetCurrentSceneCollection = "SetCurrentSceneCollection";
    public const string GetProfileList = "GetProfileList";
    public const string SetCurrentProfile = "SetCurrentProfile";

    // Scenes
    public const string GetSceneList = "GetSceneList";
    public const string GetCurrentProgramScene = "GetCurrentProgramScene";
    public const string SetCurrentProgramScene = "SetCurrentProgramScene";
    public const string GetCurrentPreviewScene = "GetCurrentPreviewScene";
    public const string SetCurrentPreviewScene = "SetCurrentPreviewScene";
    public const string CreateScene = "CreateScene";
    public const string RemoveScene = "RemoveScene";
    public const string SetSceneName = "SetSceneName";

    // Scene items (sources in scenes)
    public const string GetSceneItemList = "GetSceneItemList";
    public const string GetSceneItemId = "GetSceneItemId";
    public const string CreateSceneItem = "CreateSceneItem";
    public const string RemoveSceneItem = "RemoveSceneItem";
    public const string DuplicateSceneItem = "DuplicateSceneItem";
    public const string GetSceneItemEnabled = "GetSceneItemEnabled";
    public const string SetSceneItemEnabled = "SetSceneItemEnabled";
    public const string GetSceneItemLocked = "GetSceneItemLocked";
    public const string SetSceneItemLocked = "SetSceneItemLocked";
    public const string GetSceneItemIndex = "GetSceneItemIndex";
    public const string SetSceneItemIndex = "SetSceneItemIndex";
    public const string GetSceneItemBlendMode = "GetSceneItemBlendMode";
    public const string SetSceneItemBlendMode = "SetSceneItemBlendMode";
    public const string GetSceneItemTransform = "GetSceneItemTransform";
    public const string SetSceneItemTransform = "SetSceneItemTransform";

    // Inputs
    public const string GetInputList = "GetInputList";
    public const string GetInputKindList = "GetInputKindList";
    public const string GetSpecialInputs = "GetSpecialInputs";
    public const string CreateInput = "CreateInput";
    public const string RemoveInput = "RemoveInput";
    public const string SetInputName = "SetInputName";
    public const string GetInputDefaultSettings = "GetInputDefaultSettings";
    public const string GetInputSettings = "GetInputSettings";
    public const string SetInputSettings = "SetInputSettings";
    public const string GetInputMute = "GetInputMute";
    public const string SetInputMute = "SetInputMute";
    public const string ToggleInputMute = "ToggleInputMute";
    public const string GetInputVolume = "GetInputVolume";
    public const string SetInputVolume = "SetInputVolume";

    // Filters
    public const string GetSourceFilterList = "GetSourceFilterList";
    public const string GetSourceFilterDefaultSettings = "GetSourceFilterDefaultSettings";
    public const string CreateSourceFilter = "CreateSourceFilter";
    public const string RemoveSourceFilter = "RemoveSourceFilter";
    public const string SetSourceFilterName = "SetSourceFilterName";
    public const string GetSourceFilter = "GetSourceFilter";
    public const string SetSourceFilterIndex = "SetSourceFilterIndex";
    public const string SetSourceFilterSettings = "SetSourceFilterSettings";
    public const string SetSourceFilterEnabled = "SetSourceFilterEnabled";

    // Transitions
    public const string GetTransitionKindList = "GetTransitionKindList";
    public const string GetSceneTransitionList = "GetSceneTransitionList";
    public const string GetCurrentSceneTransition = "GetCurrentSceneTransition";
    public const string SetCurrentSceneTransition = "SetCurrentSceneTransition";
    public const string SetCurrentSceneTransitionDuration = "SetCurrentSceneTransitionDuration";
    public const string SetCurrentSceneTransitionSettings = "SetCurrentSceneTransitionSettings";
    public const string TriggerStudioModeTransition = "TriggerStudioModeTransition";

    // Stream
    public const string GetStreamStatus = "GetStreamStatus";
    public const string ToggleStream = "ToggleStream";
    public const string StartStream = "StartStream";
    public const string StopStream = "StopStream";
    public const string SendStreamCaption = "SendStreamCaption";

    // Record
    public const string GetRecordStatus = "GetRecordStatus";
    public const string ToggleRecord = "ToggleRecord";
    public const string StartRecord = "StartRecord";
    public const string StopRecord = "StopRecord";
    public const string ToggleRecordPause = "ToggleRecordPause";
    public const string PauseRecord = "PauseRecord";
    public const string ResumeRecord = "ResumeRecord";

    // Media
    public const string GetMediaInputStatus = "GetMediaInputStatus";
    public const string SetMediaInputCursor = "SetMediaInputCursor";
    public const string OffsetMediaInputCursor = "OffsetMediaInputCursor";
    public const string TriggerMediaInputAction = "TriggerMediaInputAction";

    // UI
    public const string GetStudioModeEnabled = "GetStudioModeEnabled";
    public const string SetStudioModeEnabled = "SetStudioModeEnabled";
    public const string OpenInputPropertiesDialog = "OpenInputPropertiesDialog";
    public const string OpenInputFiltersDialog = "OpenInputFiltersDialog";
    public const string OpenInputInteractDialog = "OpenInputInteractDialog";
}

// Event data models

public class SceneChangedEventData
{
    [JsonPropertyName("sceneName")]
    public string SceneName { get; set; } = "";

    [JsonPropertyName("sceneUuid")]
    public string? SceneUuid { get; set; }
}

public class SceneItemEnableStateChangedEventData
{
    [JsonPropertyName("sceneName")]
    public string SceneName { get; set; } = "";

    [JsonPropertyName("sceneUuid")]
    public string? SceneUuid { get; set; }

    [JsonPropertyName("sceneItemId")]
    public int SceneItemId { get; set; }

    [JsonPropertyName("sceneItemEnabled")]
    public bool SceneItemEnabled { get; set; }
}

public class InputMuteStateChangedEventData
{
    [JsonPropertyName("inputName")]
    public string InputName { get; set; } = "";

    [JsonPropertyName("inputUuid")]
    public string? InputUuid { get; set; }

    [JsonPropertyName("inputMuted")]
    public bool InputMuted { get; set; }
}

public class SourceFilterEnableStateChangedEventData
{
    [JsonPropertyName("sourceName")]
    public string SourceName { get; set; } = "";

    [JsonPropertyName("filterName")]
    public string FilterName { get; set; } = "";

    [JsonPropertyName("filterEnabled")]
    public bool FilterEnabled { get; set; }
}

public class StreamStateChangedEventData
{
    [JsonPropertyName("outputActive")]
    public bool OutputActive { get; set; }

    [JsonPropertyName("outputState")]
    public string OutputState { get; set; } = "";
}

public class RecordStateChangedEventData
{
    [JsonPropertyName("outputActive")]
    public bool OutputActive { get; set; }

    [JsonPropertyName("outputState")]
    public string OutputState { get; set; } = "";

    [JsonPropertyName("outputPath")]
    public string? OutputPath { get; set; }
}

// Response data models

public class SceneListResponse
{
    [JsonPropertyName("currentProgramSceneName")]
    public string CurrentProgramSceneName { get; set; } = "";

    [JsonPropertyName("currentProgramSceneUuid")]
    public string? CurrentProgramSceneUuid { get; set; }

    [JsonPropertyName("currentPreviewSceneName")]
    public string? CurrentPreviewSceneName { get; set; }

    [JsonPropertyName("currentPreviewSceneUuid")]
    public string? CurrentPreviewSceneUuid { get; set; }

    [JsonPropertyName("scenes")]
    public List<SceneInfo> Scenes { get; set; } = new();
}

public class SceneInfo
{
    [JsonPropertyName("sceneIndex")]
    public int SceneIndex { get; set; }

    [JsonPropertyName("sceneName")]
    public string SceneName { get; set; } = "";

    [JsonPropertyName("sceneUuid")]
    public string? SceneUuid { get; set; }
}

public class SceneItemInfo
{
    [JsonPropertyName("sceneItemId")]
    public int SceneItemId { get; set; }

    [JsonPropertyName("sceneItemIndex")]
    public int SceneItemIndex { get; set; }

    [JsonPropertyName("sourceName")]
    public string SourceName { get; set; } = "";

    [JsonPropertyName("sourceUuid")]
    public string? SourceUuid { get; set; }

    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = "";

    [JsonPropertyName("inputKind")]
    public string? InputKind { get; set; }

    [JsonPropertyName("isGroup")]
    public bool? IsGroup { get; set; }

    [JsonPropertyName("sceneItemEnabled")]
    public bool SceneItemEnabled { get; set; }

    [JsonPropertyName("sceneItemLocked")]
    public bool SceneItemLocked { get; set; }
}

public class InputInfo
{
    [JsonPropertyName("inputName")]
    public string InputName { get; set; } = "";

    [JsonPropertyName("inputUuid")]
    public string? InputUuid { get; set; }

    [JsonPropertyName("inputKind")]
    public string InputKind { get; set; } = "";

    [JsonPropertyName("unversionedInputKind")]
    public string? UnversionedInputKind { get; set; }
}

public class FilterInfo
{
    [JsonPropertyName("filterEnabled")]
    public bool FilterEnabled { get; set; }

    [JsonPropertyName("filterIndex")]
    public int FilterIndex { get; set; }

    [JsonPropertyName("filterKind")]
    public string FilterKind { get; set; } = "";

    [JsonPropertyName("filterName")]
    public string FilterName { get; set; } = "";

    [JsonPropertyName("filterSettings")]
    public Dictionary<string, object?>? FilterSettings { get; set; }
}
