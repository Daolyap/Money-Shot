using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using MoneyShot.Models;

namespace MoneyShot.Services;

public class SettingsService
{
    private const string SettingsFileName = "settings.json";
    private readonly string _settingsPath;
    private readonly string _appDataPath;

    public SettingsService()
    {
        _appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MoneyShot"
        );
        Directory.CreateDirectory(_appDataPath);
        _settingsPath = Path.Combine(_appDataPath, SettingsFileName);
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
            
            // Use secure deserialization options
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
            
            var settings = JsonSerializer.Deserialize<AppSettings>(json, options);
            
            // Validate and sanitize loaded settings
            if (settings != null)
            {
                settings = ValidateAndSanitizeSettings(settings);
                return settings;
            }
            
            return new AppSettings();
        }
        catch (JsonException ex)
        {
            // Log the error (in a real app, use proper logging)
            System.Diagnostics.Debug.WriteLine($"Error deserializing settings: {ex.Message}");
            // Return default settings if deserialization fails
            return new AppSettings();
        }
        catch (Exception ex)
        {
            // Log unexpected errors
            System.Diagnostics.Debug.WriteLine($"Unexpected error loading settings: {ex.Message}");
            return new AppSettings();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            // Validate and sanitize before saving
            settings = ValidateAndSanitizeSettings(settings);
            
            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                // Don't serialize null values to reduce file size
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
            };
            
            var json = JsonSerializer.Serialize(settings, options);
            
            // Write to temp file first, then rename (atomic operation)
            var tempPath = _settingsPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _settingsPath, true);
        }
        catch (Exception ex)
        {
            // Log the error
            System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
            throw new InvalidOperationException("Failed to save settings. Please check file permissions.", ex);
        }
    }
    
    /// <summary>
    /// Validates and sanitizes settings to prevent path traversal and other security issues
    /// </summary>
    private AppSettings ValidateAndSanitizeSettings(AppSettings settings)
    {
        // Validate and sanitize save path to prevent path traversal
        if (!string.IsNullOrEmpty(settings.DefaultSavePath))
        {
            try
            {
                // Get the full path and ensure it's a valid, absolute path
                var fullPath = Path.GetFullPath(settings.DefaultSavePath);
                
                // Ensure the path doesn't contain any suspicious patterns
                if (fullPath.Contains("..") || fullPath.Contains("~"))
                {
                    // Reset to default if path looks suspicious
                    settings.DefaultSavePath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                }
                else
                {
                    settings.DefaultSavePath = fullPath;
                }
            }
            catch
            {
                // If path is invalid, reset to default
                settings.DefaultSavePath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            }
        }
        else
        {
            settings.DefaultSavePath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        }
        
        // Validate file format
        var validFormats = new[] { "PNG", "JPG", "JPEG", "BMP", "GIF" };
        if (!validFormats.Contains(settings.DefaultFileFormat.ToUpper()))
        {
            settings.DefaultFileFormat = "PNG";
        }
        
        // Ensure line thickness is reasonable
        if (settings.DefaultLineThickness < 1 || settings.DefaultLineThickness > 20)
        {
            settings.DefaultLineThickness = 3;
        }
        
        return settings;
    }

    public void SetStartupWithWindows(bool enabled)
    {
        const string appName = "MoneyShot";
        
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
                    key.SetValue(appName, $"\"{fullPath}\"", RegistryValueKind.String);
                }
                else
                {
                    throw new InvalidOperationException("Unable to determine application path.");
                }
            }
            else
            {
                key.DeleteValue(appName, false);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Registry access denied: {ex.Message}");
            throw new InvalidOperationException("Insufficient permissions to modify startup settings. Please run as administrator.", ex);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error setting startup configuration: {ex.Message}");
            throw new InvalidOperationException("Failed to modify startup settings.", ex);
        }
    }

    public bool IsSetToRunOnStartup()
    {
        const string appName = "MoneyShot";
        
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue(appName) != null;
        }
        catch (Exception ex)
        {
            // Log but don't throw - this is a read-only operation
            System.Diagnostics.Debug.WriteLine($"Error checking startup status: {ex.Message}");
            return false;
        }
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
            else
            {
                throw new InvalidOperationException("Unable to access keyboard settings in registry.");
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Registry access denied: {ex.Message}");
            // Don't throw - this is a non-critical feature
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error setting Print Screen configuration: {ex.Message}");
            // Don't throw - this is a non-critical feature
        }
    }
}
