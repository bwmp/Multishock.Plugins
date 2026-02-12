namespace ImageDetection.Models;

/// <summary>
/// Configuration for a global hotkey binding.
/// </summary>
public class HotkeyBinding
{
    /// <summary>
    /// Whether the hotkey is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The key name (e.g. "F8", "F9", "F12").
    /// </summary>
    public string Key { get; set; } = "F8";

    /// <summary>
    /// Whether Ctrl modifier is required.
    /// </summary>
    public bool Ctrl { get; set; } = false;

    /// <summary>
    /// Whether Alt modifier is required.
    /// </summary>
    public bool Alt { get; set; } = false;

    /// <summary>
    /// Whether Shift modifier is required.
    /// </summary>
    public bool Shift { get; set; } = false;

    /// <summary>
    /// Gets a display-friendly string for this binding (e.g. "Ctrl+Shift+F8").
    /// </summary>
    public string GetDisplayString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        parts.Add(Key.ToUpperInvariant());
        return string.Join("+", parts);
    }

    /// <summary>
    /// List of supported key names for UI dropdowns.
    /// </summary>
    public static readonly string[] SupportedKeys =
    [
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "Pause", "ScrollLock", "PrintScreen",
        "Insert", "Delete", "Home", "End", "PageUp", "PageDown",
        "Numpad0", "Numpad1", "Numpad2", "Numpad3", "Numpad4",
        "Numpad5", "Numpad6", "Numpad7", "Numpad8", "Numpad9"
    ];

}
