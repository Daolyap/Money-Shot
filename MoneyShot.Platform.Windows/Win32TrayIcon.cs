using System.IO;
using System.Windows.Forms;
using MoneyShot.Abstractions;
using MoneyShot.Services;

namespace MoneyShot.Platform.Windows;

/// <summary>
/// NotifyIcon-based system tray (see LINUX_PORT.md § System tray for the StatusNotifierItem/D-Bus
/// equivalent on Linux, wrapped by Avalonia's TrayIcon there).
/// </summary>
public sealed class Win32TrayIcon : ITrayIcon
{
    private NotifyIcon? _notifyIcon;

    public event Action? DoubleClicked;

    public void Show(string tooltip, IReadOnlyList<TrayMenuItem> menuItems)
    {
        var icon = ResolveIcon();

        var contextMenu = new ContextMenuStrip();
        foreach (var item in menuItems)
        {
            if (item.IsSeparator)
            {
                contextMenu.Items.Add("-");
                continue;
            }

            var onClick = item.OnClick;
            contextMenu.Items.Add(item.Label, null, (_, _) => onClick?.Invoke());
        }

        _notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Visible = true,
            Text = tooltip,
            ContextMenuStrip = contextMenu
        };
        _notifyIcon.DoubleClick += (_, _) => DoubleClicked?.Invoke();
    }

    public void ShowBalloonTip(int timeoutMilliseconds, string title, string text)
    {
        _notifyIcon?.ShowBalloonTip(timeoutMilliseconds, title, text, ToolTipIcon.Info);
    }

    private static System.Drawing.Icon ResolveIcon()
    {
        try
        {
            var processModule = System.Diagnostics.Process.GetCurrentProcess().MainModule;
            var iconPath = processModule?.FileName;

            if (iconPath != null && File.Exists(iconPath))
            {
                var extracted = System.Drawing.Icon.ExtractAssociatedIcon(iconPath);
                if (extracted != null)
                {
                    return extracted;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Error extracting tray icon", ex);
        }

        // Fallback to default icon if extraction fails
        return System.Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _notifyIcon?.Dispose();
    }
}
