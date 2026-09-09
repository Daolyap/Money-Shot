using MoneyShot.Abstractions;
using MoneyShot.Services;

namespace MoneyShot.Platform.Linux;

/// <summary>
/// XDG autostart (~/.config/autostart/*.desktop) — the Linux counterpart to Win32AutoStart's
/// HKCU...\Run registry entry. See LINUX_PORT.md § Registry settings / Phase 2. This is the
/// freedesktop.org-standard mechanism honored by every mainstream desktop environment (GNOME, KDE,
/// XFCE, etc.) without any DE-specific code.
///
/// PrintScreen suppression has no Linux analogue (there's no single "the OS's screenshot tool"
/// binding to disable the way Windows' Snipping Tool PrintScreen association can be toggled via
/// registry — this varies per desktop environment and isn't attempted here); both methods
/// correctly no-op per IAutoStart's own doc comment.
/// </summary>
public sealed class LinuxAutoStart : IAutoStart
{
    private const string DesktopFileName = "moneyshot.desktop";

    private static string AutostartDirectory =>
        Path.Combine(AppDataPaths.GetConfigRoot(), "autostart");

    private static string DesktopFilePath => Path.Combine(AutostartDirectory, DesktopFileName);

    public void SetStartupWithApp(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                if (File.Exists(DesktopFilePath))
                {
                    File.Delete(DesktopFilePath);
                }
                return;
            }

            Directory.CreateDirectory(AutostartDirectory);

            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                throw new InvalidOperationException("Could not determine the running executable's path.");
            }

            // Exec value is quoted so a path containing spaces (e.g. under a user's home directory
            // with spaces, or an AppImage mounted under a spaced path) is passed as one argument —
            // the Exec key in the freedesktop.org desktop-entry spec follows shell-like quoting.
            // No extra CLI flag is needed here: MainWindow.InitializeApplication already reads the
            // persisted StartInTray setting itself regardless of how the process was launched.
            var contents =
                "[Desktop Entry]\n" +
                "Type=Application\n" +
                "Name=Money Shot\n" +
                "Comment=Screenshot capture and annotation tool\n" +
                $"Exec=\"{exePath}\"\n" +
                "Terminal=false\n" +
                "X-GNOME-Autostart-enabled=true\n";

            // Write-then-move, same atomicity reasoning as SettingsService's settings.json write —
            // a partially-written .desktop file being picked up mid-write by the session startup
            // scan is a real (if rare) failure mode worth avoiding for free.
            var tempPath = DesktopFilePath + ".tmp";
            File.WriteAllText(tempPath, contents);
            File.Move(tempPath, DesktopFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to update autostart setting: {ex.Message}", ex);
        }
    }

    public bool IsSetToRunOnStartup()
    {
        try
        {
            return File.Exists(DesktopFilePath);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to check autostart status: {ex.Message}");
            return false;
        }
    }

    public bool SetPrintScreenSuppressed(bool suppressed) => false;

    public bool TryGetPrintScreenSuppressed(out bool suppressed)
    {
        suppressed = false;
        return false;
    }
}
