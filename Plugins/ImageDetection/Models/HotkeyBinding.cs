using System.Runtime.InteropServices;

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
    /// Gets the Win32 modifier flags for RegisterHotKey.
    /// </summary>
    public uint GetModifierFlags()
    {
        uint flags = MOD_NOREPEAT; // Prevent repeated WM_HOTKEY while held
        if (Ctrl) flags |= MOD_CONTROL;
        if (Alt) flags |= MOD_ALT;
        if (Shift) flags |= MOD_SHIFT;
        return flags;
    }

    /// <summary>
    /// Gets the Win32 virtual key code for the configured key.
    /// </summary>
    public int GetVirtualKeyCode()
    {
        return Key.ToUpperInvariant() switch
        {
            "F1" => 0x70,
            "F2" => 0x71,
            "F3" => 0x72,
            "F4" => 0x73,
            "F5" => 0x74,
            "F6" => 0x75,
            "F7" => 0x76,
            "F8" => 0x77,
            "F9" => 0x78,
            "F10" => 0x79,
            "F11" => 0x7A,
            "F12" => 0x7B,
            "PAUSE" or "BREAK" => 0x13,
            "SCROLLLOCK" => 0x91,
            "PRINTSCREEN" => 0x2C,
            "INSERT" => 0x2D,
            "DELETE" => 0x2E,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "NUMPAD0" => 0x60,
            "NUMPAD1" => 0x61,
            "NUMPAD2" => 0x62,
            "NUMPAD3" => 0x63,
            "NUMPAD4" => 0x64,
            "NUMPAD5" => 0x65,
            "NUMPAD6" => 0x66,
            "NUMPAD7" => 0x67,
            "NUMPAD8" => 0x68,
            "NUMPAD9" => 0x69,
            _ when Key.Length == 1 && char.IsLetterOrDigit(Key[0]) =>
                char.ToUpper(Key[0]),
            _ => 0x77 // Default to F8
        };
    }

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

    // Win32 modifier constants
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;
}
