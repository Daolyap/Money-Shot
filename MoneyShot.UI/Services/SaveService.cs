using System;
using System.IO;
using Avalonia.Media.Imaging;
using MoneyShot.Abstractions;
using MoneyShot.Models;
using MoneyShot.UI.Interop;
using SkiaSharp;
using Logger = MoneyShot.Services.Logger;

namespace MoneyShot.UI.Services;

/// <summary>
/// Avalonia twin of MoneyShot/Services/SaveService.cs (WPF) — see LINUX_PORT.md Phase 1. Same
/// shape and same file-path validation logic (copied verbatim, it's pure and platform-neutral);
/// the WPF BitmapEncoder family is replaced with SkiaSharp encoding of the CapturedImage pixel
/// buffer, since Avalonia's own Bitmap.Save is PNG-only regardless of requested format.
/// </summary>
public class SaveService
{
    private readonly IClipboard _clipboard;

    public SaveService(IClipboard clipboard)
    {
        _clipboard = clipboard;
    }

    public void SaveToClipboard(Bitmap image)
    {
        try
        {
            _clipboard.SetImage(image.ToCapturedImage());
        }
        catch (Exception ex)
        {
            Logger.Error("Error saving to clipboard", ex);
            throw new InvalidOperationException("Failed to save image to clipboard.", ex);
        }
    }

    public void SaveToFile(Bitmap image, string filePath, string format = "PNG")
    {
        ValidateFilePath(filePath);

        try
        {
            var captured = image.ToCapturedImage();
            var info = new SKImageInfo(captured.Width, captured.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

            using var skBitmap = new SKBitmap();
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(captured.PixelDataBgra32, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                skBitmap.InstallPixels(info, handle.AddrOfPinnedObject(), captured.Stride);

                var (skFormat, quality) = format.ToUpperInvariant() switch
                {
                    // Default JPEG quality (75) visibly smears text in screenshots; 90 keeps UI
                    // text legible at a still-reasonable file size — matches the WPF build's choice.
                    "JPG" or "JPEG" => (SKEncodedImageFormat.Jpeg, 90),
                    "BMP" => (SKEncodedImageFormat.Bmp, 100),
                    "GIF" => (SKEncodedImageFormat.Gif, 100),
                    _ => (SKEncodedImageFormat.Png, 100)
                };

                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var data = skBitmap.Encode(skFormat, quality);
                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                data.SaveTo(fileStream);
            }
            finally
            {
                handle.Free();
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Error("Access denied when saving file", ex);
            throw new InvalidOperationException("Access denied. Check file permissions.", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            Logger.Error("Error saving file", ex);
            throw new InvalidOperationException($"Failed to save image to file: {ex.Message}", ex);
        }
    }

    public string GenerateFileName(string format = "PNG")
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        return $"Screenshot_{timestamp}.{format.ToLowerInvariant()}";
    }

    public void SaveImage(Bitmap image, SaveDestination destination, string? filePath = null, string format = "PNG")
    {
        switch (destination)
        {
            case SaveDestination.Clipboard:
                SaveToClipboard(image);
                break;
            case SaveDestination.File:
                if (filePath != null)
                    SaveToFile(image, filePath, format);
                break;
            case SaveDestination.Both:
                SaveToClipboard(image);
                if (filePath != null)
                    SaveToFile(image, filePath, format);
                break;
        }
    }

    /// <summary>
    /// Validates file path to prevent path traversal and other security issues. Copied verbatim
    /// from the WPF SaveService — pure path logic, no UI-framework dependency.
    /// </summary>
    private void ValidateFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be empty.", nameof(filePath));
        }

        try
        {
            var fullPath = Path.GetFullPath(filePath);

            if (!Path.IsPathRooted(fullPath))
            {
                throw new ArgumentException("Path must be absolute.", nameof(filePath));
            }

            var directory = Path.GetDirectoryName(fullPath);

            var systemDirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };

            foreach (var sysDir in systemDirs)
            {
                if (string.IsNullOrEmpty(sysDir) || string.IsNullOrEmpty(directory))
                {
                    continue;
                }

                var sysDirWithSeparator = Path.TrimEndingDirectorySeparator(sysDir) + Path.DirectorySeparatorChar;
                var directoryWithSeparator = Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar;
                if (directoryWithSeparator.StartsWith(sysDirWithSeparator, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("Cannot save to system directories.", nameof(filePath));
                }
            }

            if (Path.GetFileName(fullPath) == string.Empty)
            {
                throw new ArgumentException("Cannot save to a directory. Please specify a file name.", nameof(filePath));
            }
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (NotSupportedException ex)
        {
            throw new ArgumentException("Path format not supported.", nameof(filePath), ex);
        }
        catch (PathTooLongException ex)
        {
            throw new ArgumentException("Path is too long.", nameof(filePath), ex);
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Invalid file path.", nameof(filePath), ex);
        }
    }
}
