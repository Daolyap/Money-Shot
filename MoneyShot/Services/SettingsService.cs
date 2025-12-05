using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using MoneyShot.Models;

namespace MoneyShot.Services;

public class SettingsService
{
    private const string SettingsFileName = "settings.json";
    private readonly string _settingsPath;

    public SettingsService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MoneyShot"
        );
        Directory.CreateDirectory(appDataPath);
        _settingsPath = Path.Combine(appDataPath, SettingsFileName);
    }

    public AppSettings LoadSettings()
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, json);
    }

    public void SetStartupWithWindows(bool enabled)
    {
        const string appName = "MoneyShot";
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
        
        if (key == null) return;

        if (enabled)
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath != null)
            {
                key.SetValue(appName, $"\"{exePath}\"");
            }
        }
        else
        {
            key.DeleteValue(appName, false);
        }
    }

    public bool IsSetToRunOnStartup()
    {
        const string appName = "MoneyShot";
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
        return key?.GetValue(appName) != null;
    }

    public void SetWindowsPrintScreenDisabled(bool disabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard", true);
            if (key != null)
            {
                // Set PrintScreenKeyForSnippingEnabled to 0 to disable, 1 to enable
                // Note: This is the registry key that controls "Use Print Screen to open screen capture"
                key.SetValue("PrintScreenKeyForSnippingEnabled", disabled ? 0 : 1, RegistryValueKind.DWord);
            }
        }
        catch
        {
            // Fail silently if we don't have permission
        }
    }
}
