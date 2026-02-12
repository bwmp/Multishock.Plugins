using System.Runtime.InteropServices;
using ImageDetection.Models;
using Microsoft.Extensions.Logging;

namespace ImageDetection.Services;

/// <summary>
/// Windows implementation of <see cref="IGlobalHotkeyService"/> using Win32 RegisterHotKey.
/// Registers with hWnd=NULL so WM_HOTKEY is posted directly to the thread's message queue,
/// eliminating the need to create a window.
/// </summary>
public class WindowsGlobalHotkeyService(ILogger? logger = null) : IGlobalHotkeyService
{
    private const int WM_HOTKEY = 0x0312;
    private const int WM_QUIT = 0x0012;
    private const int HOTKEY_ID = 0x4D53; // 'MS' for MultiShock

    private readonly ILogger? _logger = logger;
    private readonly object _lock = new();

    private Thread? _messageThread;
    private int _threadId;
    private bool _isRegistered;
    private bool _disposed;
    private DateTime _lastTrigger = DateTime.MinValue;
    private HotkeyBinding? _currentBinding;

    public bool IsSupported => true;

    public string? UnsupportedReason => null;

    /// <inheritdoc />
    public TimeSpan ThrottleInterval { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <inheritdoc />
    public bool IsRegistered
    {
        get { lock (_lock) { return _isRegistered; } }
    }

    /// <inheritdoc />
    public HotkeyBinding? CurrentBinding
    {
        get { lock (_lock) { return _currentBinding; } }
    }

    /// <inheritdoc />
    public event Action? HotkeyPressed;

    /// <inheritdoc />
    public event Action<string>? RegistrationFailed;


    /// <inheritdoc />
    public bool TryRegister(HotkeyBinding binding, out string? error)
    {
        error = null;

        if (binding == null)
        {
            error = "Binding is null";
            return false;
        }

        if (!binding.Enabled)
        {
            Unregister();
            return true;
        }

        var vk = GetVirtualKeyCode(binding.Key);
        if (vk == 0)
        {
            error = $"Unsupported key: {binding.Key}";
            return false;
        }

        lock (_lock)
        {
            StopMessageThread();

            _currentBinding = binding;

            var tcs = new TaskCompletionSource<(bool success, string? err)>();

            _messageThread = new Thread(() => MessagePumpThread(binding, tcs))
            {
                IsBackground = true,
                Name = "GlobalHotkey-MessagePump"
            };
            _messageThread.SetApartmentState(ApartmentState.STA);
            _messageThread.Start();

            bool regSuccess;
            string? regError;

            if (tcs.Task.Wait(TimeSpan.FromSeconds(5)))
            {
                (regSuccess, regError) = tcs.Task.Result;
            }
            else
            {
                regSuccess = false;
                regError = "Timeout waiting for hotkey registration";
            }

            if (!regSuccess)
            {
                error = regError ?? "Registration failed";
                _isRegistered = false;
                _logger?.LogWarning("Failed to register hotkey {Binding}: {Error}", binding.GetDisplayString(), error);
                RegistrationFailed?.Invoke(error);
                return false;
            }

            _isRegistered = true;
            _logger?.LogInformation("Registered global hotkey: {Binding}", binding.GetDisplayString());
            return true;
        }
    }

    /// <inheritdoc />
    public void Unregister()
    {
        lock (_lock)
        {
            StopMessageThread();
            _isRegistered = false;
            _currentBinding = null;
            _logger?.LogInformation("Unregistered global hotkey");
        }
    }

    /// <inheritdoc />
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

    private void MessagePumpThread(HotkeyBinding binding, TaskCompletionSource<(bool, string?)> tcs)
    {
        try
        {
            _threadId = GetCurrentThreadId();

            var modifiers = GetModifierFlags(binding);
            var vk = GetVirtualKeyCode(binding.Key);

            // Register with hWnd=NULL — WM_HOTKEY goes directly to the thread's message queue
            if (!RegisterHotKey(IntPtr.Zero, HOTKEY_ID, modifiers, (uint)vk))
            {
                var errorCode = Marshal.GetLastWin32Error();
                var errorMsg = errorCode == 1409
                    ? $"Hotkey {binding.GetDisplayString()} is already in use by another application"
                    : $"RegisterHotKey failed with error code {errorCode}";
                _ = tcs.TrySetResult((false, errorMsg));
                return;
            }

            _ = tcs.TrySetResult((true, null));

            while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0))
            {
                if (msg.message == WM_HOTKEY && msg.wParam == (IntPtr)HOTKEY_ID)
                {
                    var now = DateTime.UtcNow;
                    if (now - _lastTrigger >= ThrottleInterval)
                    {
                        _lastTrigger = now;
                        try
                        {
                            HotkeyPressed?.Invoke();
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Error in HotkeyPressed handler");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in hotkey message pump");
            _ = tcs.TrySetResult((false, ex.Message));
        }
        finally
        {
            _ = UnregisterHotKey(IntPtr.Zero, HOTKEY_ID);
            _threadId = 0;
        }
    }

    private void StopMessageThread()
    {
        if (_messageThread != null && _threadId != 0)
        {
            _ = PostThreadMessage((uint)_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        if (_messageThread != null)
        {
            if (!_messageThread.Join(TimeSpan.FromSeconds(3)))
            {
                _logger?.LogWarning("Hotkey message thread did not exit in time");
            }
            _messageThread = null;
        }

        _threadId = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
    }

    private static uint GetModifierFlags(HotkeyBinding binding)
    {
        uint flags = MOD_NOREPEAT;
        if (binding.Ctrl) flags |= MOD_CONTROL;
        if (binding.Alt) flags |= MOD_ALT;
        if (binding.Shift) flags |= MOD_SHIFT;
        return flags;
    }

    private static int GetVirtualKeyCode(string? key)
    {
        var normalized = (key ?? string.Empty).ToUpperInvariant();

        return normalized switch
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
            _ when normalized.Length == 1 && char.IsLetterOrDigit(normalized[0]) => normalized[0],
            _ => 0x77
        };
    }

    #region Native Methods

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    #endregion
}
