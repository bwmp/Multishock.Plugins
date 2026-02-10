using System.Runtime.InteropServices;
using ImageDetection.Models;
using Microsoft.Extensions.Logging;

namespace ImageDetection.Services;

/// <summary>
/// Manages a single global hotkey using Win32 RegisterHotKey/UnregisterHotKey.
/// Runs a message-only window on a dedicated thread to receive WM_HOTKEY messages.
/// </summary>
public class GlobalHotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0x4D53; // 'MS' for MultiShock

    private readonly ILogger? _logger;
    private readonly object _lock = new();

    private Thread? _messageThread;
    private IntPtr _hwnd;
    private bool _isRegistered;
    private bool _disposed;
    private DateTime _lastTrigger = DateTime.MinValue;
    private HotkeyBinding? _currentBinding;

    /// <summary>
    /// Minimum interval between hotkey triggers to prevent double-fire.
    /// </summary>
    public TimeSpan ThrottleInterval { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Whether a hotkey is currently registered and listening.
    /// </summary>
    public bool IsRegistered
    {
        get { lock (_lock) { return _isRegistered; } }
    }

    /// <summary>
    /// The currently registered binding, or null if none.
    /// </summary>
    public HotkeyBinding? CurrentBinding
    {
        get { lock (_lock) { return _currentBinding; } }
    }

    /// <summary>
    /// Fired when the global hotkey is pressed. Invoked on the message thread —
    /// callers should marshal to their own context if needed.
    /// </summary>
    public event Action? HotkeyPressed;

    /// <summary>
    /// Fired when registration fails (e.g. hotkey already in use by another app).
    /// </summary>
    public event Action<string>? RegistrationFailed;

    public GlobalHotkeyService(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers a global hotkey. Unregisters any previous binding first.
    /// Returns true if registration succeeded.
    /// </summary>
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

        var vk = binding.GetVirtualKeyCode();
        if (vk == 0)
        {
            error = $"Unsupported key: {binding.Key}";
            return false;
        }

        lock (_lock)
        {
            UnregisterInternal();
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

    /// <summary>
    /// Unregisters the current hotkey and stops the message thread.
    /// </summary>
    public void Unregister()
    {
        lock (_lock)
        {
            UnregisterInternal();
            StopMessageThread();
            _isRegistered = false;
            _currentBinding = null;
            _logger?.LogInformation("Unregistered global hotkey");
        }
    }

    /// <summary>
    /// Updates the registration with new settings. If binding is disabled or null, unregisters.
    /// </summary>
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
            // Create a message-only window (HWND_MESSAGE parent)
            _hwnd = CreateMessageWindow();

            if (_hwnd == IntPtr.Zero)
            {
                tcs.TrySetResult((false, "Failed to create message window"));
                return;
            }

            var modifiers = binding.GetModifierFlags();
            var vk = binding.GetVirtualKeyCode();

            if (!RegisterHotKey(_hwnd, HOTKEY_ID, modifiers, (uint)vk))
            {
                var errorCode = Marshal.GetLastWin32Error();
                var errorMsg = errorCode == 1409
                    ? $"Hotkey {binding.GetDisplayString()} is already in use by another application"
                    : $"RegisterHotKey failed with error code {errorCode}";
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
                tcs.TrySetResult((false, errorMsg));
                return;
            }

            tcs.TrySetResult((true, null));

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

                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in hotkey message pump");
            tcs.TrySetResult((false, ex.Message));
        }
        finally
        {
            if (_hwnd != IntPtr.Zero)
            {
                UnregisterHotKey(_hwnd, HOTKEY_ID);
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }
    }

    private void UnregisterInternal()
    {
        if (_hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HOTKEY_ID);
        }
    }

    private void StopMessageThread()
    {
        if (_messageThread != null && _hwnd != IntPtr.Zero)
        {
            PostMessage(_hwnd, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
        }

        if (_messageThread != null)
        {
            if (!_messageThread.Join(TimeSpan.FromSeconds(3)))
            {
                _logger?.LogWarning("Hotkey message thread did not exit in time");
            }
            _messageThread = null;
        }

        _hwnd = IntPtr.Zero;
    }

    private static IntPtr CreateMessageWindow()
    {
        var wndClass = new WNDCLASS
        {
            lpfnWndProc = DefWindowProc,
            lpszClassName = $"MultiShock_HotkeyWnd_{Environment.CurrentManagedThreadId}"
        };

        var atom = RegisterClass(ref wndClass);
        if (atom == 0) return IntPtr.Zero;

        // HWND_MESSAGE = (IntPtr)(-3): message-only window, invisible
        return CreateWindowEx(
            0,
            wndClass.lpszClassName,
            "",
            0,
            0, 0, 0, 0,
            -3, // HWND_MESSAGE
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
    }

    #region Native Methods

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

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

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct WNDCLASS
    {
        public uint style;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
    }

    #endregion
}
