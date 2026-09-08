using System.Runtime.InteropServices;
using MoneyShot.Abstractions;
using MoneyShot.Services;

namespace MoneyShot.Platform.Windows;

/// <summary>
/// Win32 RegisterHotKey-based global hotkeys (see LINUX_PORT.md § Global hotkeys for the X11
/// XGrabKey equivalent, and the note that Wayland has no direct equivalent at all).
///
/// Hooks WM_HOTKEY on a dedicated, invisible, message-only window (HWND_MESSAGE) that this class
/// creates and owns via a WinForms NativeWindow, rather than subclassing the app's actual main
/// window the way the original HwndSource.AddHook-based implementation did. This means: (a) this
/// project still needs no WPF reference (a NativeWindow works regardless of UI framework), and
/// (b) hotkey registration no longer depends on the main window having a native handle at all —
/// which matters for a UI that starts hidden in the tray with no window ever shown (see
/// MoneyShot.UI's MainWindow, the Avalonia build from LINUX_PORT.md Phase 1).
/// </summary>
public sealed class Win32GlobalHotkeys : IGlobalHotkeys, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HWND_MESSAGE_PARENT = -3;
    private readonly Dictionary<int, Action> _hotKeyActions = new();
    private int _currentId;
    private HotKeyMessageWindow? _messageWindow;

    private IntPtr WindowHandle => _messageWindow?.Handle ?? IntPtr.Zero;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public void Initialize()
    {
        _messageWindow = new HotKeyMessageWindow(this);
        _messageWindow.CreateHandle(new System.Windows.Forms.CreateParams
        {
            Parent = new IntPtr(HWND_MESSAGE_PARENT)
        });
    }

    public bool RegisterHotKeyFromString(string hotkeyString, Action action)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hotkeyString))
            {
                Logger.Warn("Hotkey string is null or empty");
                return false;
            }

            var (modifiers, key) = HotKeyParser.ParseHotKey(hotkeyString);
            if (key == 0)
            {
                Logger.Warn($"Invalid hotkey: {hotkeyString}");
                return false;
            }

            return RegisterHotKeyCore(modifiers, key, action);
        }
        catch (Exception ex)
        {
            Logger.Error($"Error registering hotkey '{hotkeyString}'", ex);
            return false;
        }
    }

    private bool RegisterHotKeyCore(uint modifiers, uint key, Action action)
    {
        _currentId++;
        if (RegisterHotKey(WindowHandle, _currentId, modifiers, key))
        {
            _hotKeyActions[_currentId] = action;
            return true;
        }

        // Most often the combination is already claimed by another application (or by a
        // still-running instance). Without this log the hotkey just silently does nothing.
        Logger.Warn($"RegisterHotKey failed for modifiers=0x{modifiers:X} key=0x{key:X} — combination may be in use by another application.");
        return false;
    }

    public void UnregisterAll()
    {
        foreach (var id in _hotKeyActions.Keys.ToList())
        {
            UnregisterHotKey(WindowHandle, id);
        }
        _hotKeyActions.Clear();
        _currentId = 0; // Reset ID counter
    }

    private void HandleHotKeyMessage(int id)
    {
        if (_hotKeyActions.TryGetValue(id, out var action))
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                // An exception escaping the window procedure can take down the process; a failed
                // capture should be logged, not fatal.
                Logger.Error("Hotkey action threw", ex);
            }
        }
    }

    public void Dispose()
    {
        UnregisterAll();
        if (_messageWindow?.Handle != IntPtr.Zero)
        {
            _messageWindow?.ReleaseHandle();
        }
    }

    private sealed class HotKeyMessageWindow : System.Windows.Forms.NativeWindow
    {
        private readonly Win32GlobalHotkeys _owner;

        public HotKeyMessageWindow(Win32GlobalHotkeys owner) => _owner = owner;

        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                _owner.HandleHotKeyMessage(m.WParam.ToInt32());
            }
            base.WndProc(ref m);
        }
    }
}
