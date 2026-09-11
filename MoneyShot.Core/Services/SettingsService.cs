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
        _appDataPath = Path.Combine(AppDataPaths.GetConfigRoot(), "MoneyShot");
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
                return ValidateAndSanitizeSettings(settings);
            }

            return new AppSettings();
        }
        catch (JsonException ex)
        {
            // Log the error (in a real app, use proper logging)
            Logger.Error("Error deserializing settings", ex);
            // Return default settings if deserialization fails
            return new AppSettings();
        }
        catch (Exception ex)
        {
            // Log unexpected errors
            Logger.Error("Unexpected error loading settings", ex);
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
            Logger.Error("Error saving settings", ex);
            throw new InvalidOperationException("Failed to save settings. Please check file permissions.", ex);
        }
    }

    private static readonly char[] WindowsForbiddenPathChars = { '<', '>', '|', '?', '*', '"' };

    /// <summary>
    /// Validates and sanitizes settings to prevent path traversal and other security issues
    /// </summary>
    internal static AppSettings ValidateAndSanitizeSettings(AppSettings settings)
    {
        var fallbackSavePath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        // Validate and sanitize save path to prevent path traversal
        if (!string.IsNullOrEmpty(settings.DefaultSavePath)
            && settings.DefaultSavePath.IndexOfAny(WindowsForbiddenPathChars) < 0
            && !ContainsControlChar(settings.DefaultSavePath))
        {
            try
            {
                // Get the full path and ensure it's a valid, absolute path
                var fullPath = Path.GetFullPath(settings.DefaultSavePath);

                // Ensure the path is rooted (absolute) and doesn't use relative components
                if (Path.IsPathRooted(fullPath) && Path.IsPathFullyQualified(settings.DefaultSavePath))
                {
                    settings.DefaultSavePath = fullPath;
                }
                else
                {
                    settings.DefaultSavePath = fallbackSavePath;
                }
            }
            catch (ArgumentException)
            {
                settings.DefaultSavePath = fallbackSavePath;
            }
            catch (NotSupportedException)
            {
                settings.DefaultSavePath = fallbackSavePath;
            }
            catch (PathTooLongException)
            {
                settings.DefaultSavePath = fallbackSavePath;
            }
        }
        else
        {
            settings.DefaultSavePath = fallbackSavePath;
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

        // Clamp history retention to the range the UI offers (0 disables retention
        // enforcement) so a hand-edited settings.json can't request absurd values.
        settings.HistoryRetentionCount = Math.Clamp(settings.HistoryRetentionCount, 0, 500);

        return settings;
    }

    private static bool ContainsControlChar(string value)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c)) return true;
        }
        return false;
    }
}
