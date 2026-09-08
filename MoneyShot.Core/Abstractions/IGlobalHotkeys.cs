namespace MoneyShot.Abstractions;

/// <summary>
/// Global (system-wide) hotkey registration, abstracted from the OS mechanism (Win32 RegisterHotKey
/// on Windows, XGrabKey on X11 — no direct Wayland equivalent, see LINUX_PORT.md § Global hotkeys).
/// </summary>
public interface IGlobalHotkeys
{
    /// <summary>
    /// Prepares hotkey registration. Must be called once before registering hotkeys. Implementations
    /// own whatever OS-level plumbing this needs (e.g. a dedicated message-only window on Windows) —
    /// callers don't need to have a visible window, or any window at all, for this to work.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Parses a hotkey string like "Ctrl+PrintScreen" or "Ctrl+Shift+1" and registers it, invoking
    /// <paramref name="action"/> when pressed. Returns false (and logs) if the string is invalid or
    /// the combination is already claimed by another application.
    /// </summary>
    bool RegisterHotKeyFromString(string hotkeyString, Action action);

    void UnregisterAll();
}
