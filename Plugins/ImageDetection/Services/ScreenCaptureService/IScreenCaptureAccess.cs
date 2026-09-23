namespace ImageDetection.Services;

/// <summary>
/// Implemented by capture backends where the desktop, not the app, decides which screen is
/// captured (the Wayland screen-sharing portal).
/// </summary>
public interface IScreenCaptureAccess
{
    /// <summary>
    /// Whether the captured screen is chosen in the desktop's own sharing dialog.
    /// </summary>
    bool UsesSystemScreenPicker { get; }

    /// <summary>
    /// Forgets the remembered choice (and any earlier refusal) so the next capture asks again.
    /// </summary>
    void ResetScreenChoice();
}
