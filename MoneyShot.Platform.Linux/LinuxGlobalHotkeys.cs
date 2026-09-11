using MoneyShot.Abstractions;
using MoneyShot.Services;

namespace MoneyShot.Platform.Linux;

/// <summary>
/// X11 XGrabKey-based global hotkeys — the Linux counterpart to Win32GlobalHotkeys. See
/// LINUX_PORT.md § Global hotkeys / Phase 2.
///
/// Only works under an X11 session. There is no Wayland equivalent of "grab this key combination
/// globally, regardless of which window has focus" — Wayland compositors deliberately don't let
/// clients do that for security reasons, and XGrabKey issued through XWayland does not reach
/// native-Wayland-surface focus the way a real global grab would. If XOpenDisplay fails (no X11
/// available at all) or a grab is rejected, this logs a warning and the corresponding hotkey
/// silently does nothing — mirroring Win32GlobalHotkeys' "combination may be in use" pattern
/// exactly, and matching this interface's own doc comment ("no direct Wayland equivalent").
///
/// Runs its own event-pump thread (XNextEvent blocks) rather than integrating with Avalonia's
/// dispatcher — same shape as Win32GlobalHotkeys' dedicated message-only window, and for the same
/// reason: hotkey delivery must not depend on any UI window existing.
/// </summary>
public sealed class LinuxGlobalHotkeys : IGlobalHotkeys, IDisposable
{
    private readonly Dictionary<(byte keycode, uint modifiers), Action> _hotKeyActions = new();
    private readonly Lock _lock = new();
    private IntPtr _display;
    private IntPtr _grabWindow;
    private Thread? _eventThread;
    private volatile bool _running;

    // Grabbing exactly the requested modifier mask misses the keypress whenever Caps Lock / Num
    // Lock happen to be on, because X11 treats them as just more modifier bits that must match
    // exactly — X11 has no "ignore lock modifiers" flag on XGrabKey itself. The conventional fix
    // (used by every X11 hotkey daemon) is to grab the same combination under all 4 permutations
    // of {none, CapsLock, NumLock, CapsLock+NumLock} added to the real modifier mask.
    private static readonly uint[] LockModifierCombinations = { 0, X11.LockMask, X11.Mod2Mask, X11.LockMask | X11.Mod2Mask };

    public void Initialize()
    {
        _display = X11.XOpenDisplay(null);
        if (_display == IntPtr.Zero)
        {
            Logger.Warn("LinuxGlobalHotkeys: could not open an X11 display — global hotkeys are unavailable (expected under a pure-Wayland session with no XWayland; use the tray menu or in-app buttons instead).");
            return;
        }

        _grabWindow = X11.XDefaultRootWindow(_display);
        X11.XSelectInput(_display, _grabWindow, X11.KeyPressMask);

        _running = true;
        _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "MoneyShot-X11-Hotkeys" };
        _eventThread.Start();
    }

    public bool RegisterHotKeyFromString(string hotkeyString, Action action)
    {
        if (_display == IntPtr.Zero)
        {
            // Already logged once in Initialize(); don't spam per-hotkey.
            return false;
        }

        if (string.IsNullOrWhiteSpace(hotkeyString))
        {
            Logger.Warn("Hotkey string is null or empty");
            return false;
        }

        var (winModifiers, winKey) = HotKeyParser.ParseHotKey(hotkeyString);
        if (winKey == 0)
        {
            Logger.Warn($"Invalid hotkey: {hotkeyString}");
            return false;
        }

        var keysymName = VirtualKeyToX11KeysymName(winKey);
        if (keysymName == null)
        {
            Logger.Warn($"Hotkey '{hotkeyString}' has no known X11 key mapping.");
            return false;
        }

        var keysym = X11.XStringToKeysym(keysymName);
        if (keysym == 0)
        {
            Logger.Warn($"X11 has no keysym named '{keysymName}' (hotkey '{hotkeyString}').");
            return false;
        }

        var keycode = X11.XKeysymToKeycode(_display, keysym);
        if (keycode == 0)
        {
            Logger.Warn($"X11 has no keycode for keysym '{keysymName}' (hotkey '{hotkeyString}') — the current keyboard layout may not have this key.");
            return false;
        }

        var x11Modifiers = WinModifiersToX11(winModifiers);

        lock (_lock)
        {
            // XGrabKey has no meaningful return-value failure signal for "already grabbed by
            // another client" — that arrives asynchronously as a BadAccess error via the X11
            // error handler, which this minimal implementation doesn't install a custom handler
            // for. Unlike Win32's synchronous RegisterHotKey, there's no cheap way to report
            // "combination already in use" back to the caller here; this always reports success
            // once the grab call has been issued for every lock-modifier permutation.
            foreach (var lockCombo in LockModifierCombinations)
            {
                var effectiveModifiers = x11Modifiers | lockCombo;
                X11.XGrabKey(_display, keycode, effectiveModifiers, _grabWindow, true, X11.GrabModeAsync, X11.GrabModeAsync);
                _hotKeyActions[(keycode, effectiveModifiers)] = action;
            }
            X11.XFlush(_display);
            return true;
        }
    }

    public void UnregisterAll()
    {
        if (_display == IntPtr.Zero) return;

        lock (_lock)
        {
            foreach (var (keycode, modifiers) in _hotKeyActions.Keys)
            {
                X11.XUngrabKey(_display, keycode, modifiers, _grabWindow);
            }
            _hotKeyActions.Clear();
            X11.XFlush(_display);
        }
    }

    private void EventLoop()
    {
        var buffer = new byte[X11.XEventBufferSize];
        while (_running)
        {
            // XNextEvent blocks until an event arrives; there is no clean non-blocking way to
            // interrupt it from another thread short of sending ourselves a dummy event, so
            // Dispose() just lets this thread die with the process (it's a background thread) —
            // matches Win32GlobalHotkeys' equivalent shutdown-by-process-exit behavior in practice.
            if (X11.XNextEvent(_display, buffer) != 0) continue;
            if (X11.ReadEventType(buffer) != X11.KeyPress) continue;

            var keycode = (byte)X11.ReadKeyEventKeycode(buffer);
            var state = X11.ReadKeyEventState(buffer);

            Action? action;
            lock (_lock)
            {
                _hotKeyActions.TryGetValue((keycode, state), out action);
            }

            if (action == null) continue;
            try
            {
                action.Invoke();
            }
            catch (Exception ex)
            {
                Logger.Error("Hotkey action threw", ex);
            }
        }
    }

    private static uint WinModifiersToX11(uint winModifiers)
    {
        uint result = 0;
        if ((winModifiers & HotKeyParser.MOD_CONTROL) != 0) result |= X11.ControlMask;
        if ((winModifiers & HotKeyParser.MOD_ALT) != 0) result |= X11.Mod1Mask;
        if ((winModifiers & HotKeyParser.MOD_SHIFT) != 0) result |= X11.ShiftMask;
        if ((winModifiers & HotKeyParser.MOD_WIN) != 0) result |= X11.Mod4Mask;
        return result;
    }

    private static string? VirtualKeyToX11KeysymName(uint vk) => vk switch
    {
        HotKeyParser.VK_SNAPSHOT => "Print",
        HotKeyParser.VK_0 => "0",
        HotKeyParser.VK_1 => "1",
        HotKeyParser.VK_2 => "2",
        HotKeyParser.VK_3 => "3",
        HotKeyParser.VK_4 => "4",
        HotKeyParser.VK_5 => "5",
        HotKeyParser.VK_6 => "6",
        HotKeyParser.VK_7 => "7",
        HotKeyParser.VK_8 => "8",
        HotKeyParser.VK_9 => "9",
        HotKeyParser.VK_F1 => "F1",
        HotKeyParser.VK_F2 => "F2",
        HotKeyParser.VK_F3 => "F3",
        HotKeyParser.VK_F4 => "F4",
        HotKeyParser.VK_F5 => "F5",
        HotKeyParser.VK_F6 => "F6",
        HotKeyParser.VK_F7 => "F7",
        HotKeyParser.VK_F8 => "F8",
        HotKeyParser.VK_F9 => "F9",
        HotKeyParser.VK_F10 => "F10",
        HotKeyParser.VK_F11 => "F11",
        HotKeyParser.VK_F12 => "F12",
        _ => null,
    };

    public void Dispose()
    {
        _running = false;
        UnregisterAll();
        if (_display != IntPtr.Zero)
        {
            X11.XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }
    }
}
