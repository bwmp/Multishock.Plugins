namespace ImageDetection.Models;

/// <summary>
/// Configuration for the action to perform when an image is detected.
/// </summary>
public class ActionConfig
{
    /// <summary>
    /// Whether direct device actions are enabled (vs only firing flow events).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Type of action to perform.
    /// </summary>
    public ActionType Type { get; set; } = ActionType.Shock;

    /// <summary>
    /// Intensity of the action (0-100).
    /// </summary>
    public int Intensity { get; set; } = 20;

    /// <summary>
    /// Duration of the action in seconds.
    /// </summary>
    public double DurationSeconds { get; set; } = 1.0;

    /// <summary>
    /// Mode for selecting which shockers to target.
    /// </summary>
    public ShockerMode Mode { get; set; } = ShockerMode.Selected;

    /// <summary>
    /// Shocker IDs to target. All selected shockers are activated in Selected mode;
    /// a random subset is chosen in Random mode.
    /// </summary>
    public List<string> ShockerIds { get; set; } = [];

    /// <summary>
    /// Minimum number of random shockers to activate when Mode is Random.
    /// Clamped to the number of selected shockers at runtime.
    /// </summary>
    public int RandomCountMin { get; set; } = 1;

    /// <summary>
    /// Maximum number of random shockers to activate when Mode is Random.
    /// Clamped to the number of selected shockers at runtime.
    /// </summary>
    public int RandomCountMax { get; set; } = 1;
}

/// <summary>
/// Type of device action.
/// </summary>
public enum ActionType
{
    Shock = 0,
    Vibrate = 1,
    Beep = 2
}

/// <summary>
/// Mode for selecting shockers.
/// </summary>
public enum ShockerMode
{
    /// <summary>
    /// Activate all selected shockers.
    /// Kept as value 0 for backward compatibility (previously "All").
    /// Legacy configs with no ShockerIds will target all connected shockers.
    /// </summary>
    Selected = 0,

    /// <summary>
    /// Pick a random subset from the selected shockers.
    /// The number of shockers to activate is controlled by <see cref="ActionConfig.RandomCount"/>.
    /// Value kept at 2 for backward compatibility with legacy "Random" configs.
    /// </summary>
    Random = 2
}
