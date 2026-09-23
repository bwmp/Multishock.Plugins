using ImageDetection.Models;
using MultiShock.PluginSdk;

namespace ImageDetection.Services;

public sealed class HostGlobalHotkeyService(IHostGlobalHotkeyService hostService) : IGlobalHotkeyService
{
    private IDisposable? _registration;
    private HotkeyBinding? _currentBinding;
    private DateTime _lastTrigger = DateTime.MinValue;

    public bool IsSupported => hostService.IsSupported;

    public string? UnsupportedReason => hostService.UnsupportedReason;

    public TimeSpan ThrottleInterval { get; set; } = TimeSpan.FromMilliseconds(300);

    public bool IsRegistered => _registration is not null;

    public HotkeyBinding? CurrentBinding => _currentBinding;

    public event Action? HotkeyPressed;

    public event Action<string>? RegistrationFailed;

    public bool TryRegister(HotkeyBinding binding, out string? error)
    {
        if (!binding.Enabled)
        {
            Unregister();
            error = null;
            return true;
        }

        Unregister();
        var keys = new List<string>(4);
        if (binding.Ctrl) keys.Add("Ctrl");
        if (binding.Alt) keys.Add("Alt");
        if (binding.Shift) keys.Add("Shift");
        keys.Add(binding.Key);

        if (!hostService.TryRegister(keys, OnPressed, out _registration, out error) || _registration is null)
        {
            RegistrationFailed?.Invoke(error ?? "The host could not register the global hotkey.");
            return false;
        }

        _currentBinding = binding;
        return true;
    }

    public void Unregister()
    {
        _registration?.Dispose();
        _registration = null;
        _currentBinding = null;
    }

    public bool UpdateBinding(HotkeyBinding? binding, out string? error)
    {
        if (binding is null || !binding.Enabled)
        {
            Unregister();
            error = null;
            return true;
        }

        return TryRegister(binding, out error);
    }

    private void OnPressed()
    {
        var now = DateTime.UtcNow;
        if (now - _lastTrigger < ThrottleInterval)
        {
            return;
        }

        _lastTrigger = now;
        HotkeyPressed?.Invoke();
    }

    public void Dispose() => Unregister();
}
