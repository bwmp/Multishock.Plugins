using ImageDetection.Models;

namespace ImageDetection.Services;

/// <summary>
/// Platform-agnostic interface for a global hotkey service.
/// Implementations handle OS-specific hotkey registration.
/// </summary>
public interface IGlobalHotkeyService : IDisposable
{
    /// <summary>
    /// Whether global hotkeys are supported on the current platform/runtime.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Optional reason when <see cref="IsSupported"/> is false.
    /// </summary>
    string? UnsupportedReason { get; }

    /// <summary>
    /// Minimum interval between hotkey triggers to prevent double-fire.
    /// </summary>
    TimeSpan ThrottleInterval { get; set; }

    /// <summary>
    /// Whether a hotkey is currently registered and listening.
    /// </summary>
    bool IsRegistered { get; }

    /// <summary>
    /// The currently registered binding, or null if none.
    /// </summary>
    HotkeyBinding? CurrentBinding { get; }

    /// <summary>
    /// Fired when the global hotkey is pressed. May be invoked on a background thread —
    /// callers should marshal to their own context if needed.
    /// </summary>
    event Action? HotkeyPressed;

    /// <summary>
    /// Fired when registration fails (e.g. hotkey already in use by another app).
    /// </summary>
    event Action<string>? RegistrationFailed;

    /// <summary>
    /// Registers a global hotkey. Unregisters any previous binding first.
    /// Returns true if registration succeeded.
    /// </summary>
    bool TryRegister(HotkeyBinding binding, out string? error);

    /// <summary>
    /// Unregisters the current hotkey.
    /// </summary>
    void Unregister();

    /// <summary>
    /// Updates the registration with new settings. If binding is disabled or null, unregisters.
    /// </summary>
    bool UpdateBinding(HotkeyBinding? binding, out string? error);
}
