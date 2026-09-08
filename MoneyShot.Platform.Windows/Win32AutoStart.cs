using System.IO;
using Microsoft.Win32;
using MoneyShot.Abstractions;
using MoneyShot.Services;

namespace MoneyShot.Platform.Windows;

/// <summary>
/// The two registry-backed OS integrations formerly on SettingsService (see LINUX_PORT.md §
/// Registry settings). On Linux, SetStartupWithApp becomes a ~/.config/autostart/*.desktop file
/// and print-screen suppression has no analogue — an implementation there should simply return
/// false / no-op.
/// </summary>
public sealed class Win32AutoStart : IAutoStart
{
    private const string AppName = "MoneyShot";

    public void SetStartupWithApp(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

            if (key == null)
            {
                throw new InvalidOperationException("Unable to access registry key for startup configuration.");
            }

            if (enabled)
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (exePath != null)
                {
                    // Validate the path before writing to registry
                    var fullPath = Path.GetFullPath(exePath);
                    key.SetValue(AppName, $"\"{fullPath}\"", RegistryValueKind.String);
                }
                else
                {
                    throw new InvalidOperationException("Unable to determine application path.");
                }
            }
            else
            {
                key.DeleteValue(AppName, false);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Error("Registry access denied", ex);
            throw new InvalidOperationException("Insufficient permissions to modify startup settings. Please run as administrator.", ex);
        }
        catch (Exception ex)
        {
            Logger.Error("Error setting startup configuration", ex);
            throw new InvalidOperationException("Failed to modify startup settings.", ex);
        }
    }

    public bool IsSetToRunOnStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue(AppName) != null;
        }
        catch (Exception ex)
        {
            // Log but don't throw - this is a read-only operation
            Logger.Error("Error checking startup status", ex);
            return false;
        }
    }

    public bool SetPrintScreenSuppressed(bool suppressed)
    {
        try
        {
            // Open existing key first; fall back to creating it only when missing so this setting can still be applied.
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard", true)
                ?? Registry.CurrentUser.CreateSubKey(@"Control Panel\Keyboard", true);
            if (key == null)
            {
                Logger.Warn("Unable to access keyboard settings in registry.");
                return false;
            }

            // Set PrintScreenKeyForSnippingEnabled to 0 to disable, 1 to enable
            // This controls Windows' "Use Print Screen to open screen capture" setting
            key.SetValue("PrintScreenKeyForSnippingEnabled", suppressed ? 0 : 1, RegistryValueKind.DWord);
            key.Flush();

            return TryGetPrintScreenSuppressed(out var isDisabled) && isDisabled == suppressed;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Error("Registry access denied", ex);
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("Error setting Print Screen configuration", ex);
            return false;
        }
    }

    public bool TryGetPrintScreenSuppressed(out bool suppressed)
    {
        suppressed = false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard", false);
            if (key == null)
            {
                return false;
            }

            var value = key.GetValue("PrintScreenKeyForSnippingEnabled");
            if (value is int intValue)
            {
                suppressed = intValue == 0;
                return true;
            }

            if (value is string stringValue && int.TryParse(stringValue, out var parsedValue))
            {
                suppressed = parsedValue == 0;
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("Error reading Print Screen configuration", ex);
            return false;
        }
    }
}
