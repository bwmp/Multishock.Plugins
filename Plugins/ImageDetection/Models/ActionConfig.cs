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
    public ShockerMode Mode { get; set; } = ShockerMode.All;

    /// <summary>
    /// Specific shocker IDs to target (when Mode is Specific).
    /// </summary>
    public List<string> ShockerIds { get; set; } = [];
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
    /// Target all connected shockers.
    /// </summary>
    All,

    /// <summary>
    /// Target specific shockers by ID.
    /// </summary>
    Specific,

    /// <summary>
    /// Target a random shocker.
    /// </summary>
    Random
}
