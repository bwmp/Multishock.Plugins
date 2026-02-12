using ImageDetection.Models;

namespace ImageDetection.Services;

/// <summary>
/// Fallback hotkey service for unsupported platforms.
/// </summary>
public class NoopGlobalHotkeyService : IGlobalHotkeyService
{
    private HotkeyBinding? _currentBinding;
    private Action<string>? _registrationFailed;

    public bool IsSupported => false;

    public string? UnsupportedReason => "Global hotkeys are only implemented for Windows in this plugin build.";

    public TimeSpan ThrottleInterval { get; set; } = TimeSpan.FromMilliseconds(300);

    public bool IsRegistered => false;

    public HotkeyBinding? CurrentBinding => _currentBinding;

    public event Action? HotkeyPressed
    {
        add { }
        remove { }
    }

    public event Action<string>? RegistrationFailed
    {
        add => _registrationFailed += value;
        remove => _registrationFailed -= value;
    }

    public bool TryRegister(HotkeyBinding binding, out string? error)
    {
        _currentBinding = binding;

        if (binding is { Enabled: false })
        {
            error = null;
            return true;
        }

        error = UnsupportedReason ?? "Global hotkeys are not supported on this platform.";
        _registrationFailed?.Invoke(error);
        return false;
    }

    public void Unregister()
    {
        _currentBinding = null;
    }

    public bool UpdateBinding(HotkeyBinding? binding, out string? error)
    {
        if (binding == null || !binding.Enabled)
        {
            Unregister();
            error = null;
            return true;
        }

        return TryRegister(binding, out error);
    }

    public void Dispose()
    {
    }
}
