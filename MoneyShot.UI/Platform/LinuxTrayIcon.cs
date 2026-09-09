using Avalonia.Controls;
using Avalonia.Platform;
using MoneyShot.Abstractions;

namespace MoneyShot.UI.Platform;

/// <summary>
/// Wraps Avalonia's own cross-platform <see cref="TrayIcon"/>/<see cref="NativeMenu"/> for the
/// Linux build, rather than hand-rolling the StatusNotifierItem D-Bus protocol directly — Avalonia
/// (via its Avalonia.FreeDesktop assembly) already implements SNI registration correctly, so
/// reusing it is both less code and more likely to be correct than a hand-rolled, untested D-Bus
/// service. See LINUX_PORT.md § System tray / Phase 2. This lives in MoneyShot.UI rather than
/// MoneyShot.Platform.Linux specifically because it depends on Avalonia — Platform.Linux stays
/// Avalonia-free, mirroring how Win32TrayIcon in Platform.Windows stays WPF-free.
///
/// Note on GNOME: vanilla GNOME Shell has no built-in StatusNotifierItem host — it needs the
/// "AppIndicator and KStatusNotifierItem Support" extension (ships by default on Ubuntu, not on
/// stock Fedora Workstation) for a tray icon to be visible at all. This is a real, known Linux
/// desktop-ecosystem gap, not a bug in this implementation; there is no portable workaround short
/// of also implementing the legacy XEmbed systray protocol as a fallback, which Avalonia's own
/// tray support does not do either.
///
/// Linux tray icons have no standard "double-click" concept (SNI defines Activate/
/// SecondaryActivate/ContextMenu, not click-count) — a single left-click (Activate) fires
/// <see cref="DoubleClicked"/> here, since requiring an actual double-click on Linux would be an
/// unfamiliar, undiscoverable gesture for a tray icon on that platform.
/// </summary>
public sealed class LinuxTrayIcon : ITrayIcon
{
    private TrayIcon? _trayIcon;

    public event Action? DoubleClicked;

    public void Show(string tooltip, IReadOnlyList<TrayMenuItem> menuItems)
    {
        var menu = new NativeMenu();
        foreach (var item in menuItems)
        {
            if (item.IsSeparator)
            {
                menu.Items.Add(new NativeMenuItemSeparator());
                continue;
            }

            var onClick = item.OnClick;
            var nativeItem = new NativeMenuItem(item.Label);
            nativeItem.Click += (_, _) => onClick?.Invoke();
            menu.Items.Add(nativeItem);
        }

        _trayIcon = new TrayIcon
        {
            Icon = ResolveIcon(),
            ToolTipText = tooltip,
            Menu = menu,
            IsVisible = true
        };
        _trayIcon.Clicked += (_, _) => DoubleClicked?.Invoke();
    }

    public void ShowBalloonTip(int timeoutMilliseconds, string title, string text)
    {
        // Avalonia's TrayIcon has no cross-platform balloon/notification API — a real desktop
        // notification would need the org.freedesktop.Notifications D-Bus interface (the
        // "notify-send" mechanism), which is a reasonable follow-up but out of scope for the
        // minimum-viable Linux port; silently doing nothing here is preferable to throwing, since
        // callers treat this as a best-effort courtesy notification, never a required one.
    }

    private static WindowIcon? ResolveIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://MoneyShot.UI/Assets/icon.png"));
            return new WindowIcon(stream);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
        }
    }
}
