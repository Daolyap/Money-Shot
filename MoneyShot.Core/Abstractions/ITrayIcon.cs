namespace MoneyShot.Abstractions;

/// <summary>One entry in the tray icon's context menu. IsSeparator entries ignore every other field.</summary>
public sealed record TrayMenuItem(string Label, Action? OnClick, bool IsSeparator = false)
{
    public static TrayMenuItem Separator() => new(string.Empty, null, IsSeparator: true);
}

/// <summary>
/// System tray presence, abstracted from the OS mechanism (System.Windows.Forms.NotifyIcon on
/// Windows, StatusNotifierItem/D-Bus via Avalonia's TrayIcon on Linux — see LINUX_PORT.md § System
/// tray). A single instance represents one tray icon for the app's lifetime.
/// </summary>
public interface ITrayIcon : IDisposable
{
    /// <summary>Creates and shows the icon with the given tooltip and context menu. Call once.</summary>
    void Show(string tooltip, IReadOnlyList<TrayMenuItem> menuItems);

    /// <summary>Transient notification popup from the tray icon (e.g. "minimized to tray").</summary>
    void ShowBalloonTip(int timeoutMilliseconds, string title, string text);

    /// <summary>Raised when the user double-clicks the tray icon (conventionally: show main window).</summary>
    event Action? DoubleClicked;
}
