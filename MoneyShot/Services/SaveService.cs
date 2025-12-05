using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MoneyShot.Services;

public class SaveService
{
    public void SaveToClipboard(BitmapSource image)
    {
        try
        {
            Clipboard.SetImage(image);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving to clipboard: {ex.Message}");
            throw new InvalidOperationException("Failed to save image to clipboard.", ex);
        }
    }

    public void SaveToFile(BitmapSource image, string filePath, string format = "PNG")
    {
        // Validate the file path to prevent path traversal
        ValidateFilePath(filePath);
        
        try
        {
            BitmapEncoder? encoder = format.ToUpper() switch
            {
                "PNG" => new PngBitmapEncoder(),
                "JPG" or "JPEG" => new JpegBitmapEncoder(),
                "BMP" => new BmpBitmapEncoder(),
                "GIF" => new GifBitmapEncoder(),
                _ => new PngBitmapEncoder()
            };

            encoder.Frames.Add(BitmapFrame.Create(image));

            // Ensure directory exists
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(fileStream);
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Access denied when saving file: {ex.Message}");
            throw new InvalidOperationException("Access denied. Check file permissions.", ex);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving file: {ex.Message}");
            throw new InvalidOperationException($"Failed to save image to file: {ex.Message}", ex);
        }
    }

    public string GenerateFileName(string format = "PNG")
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        return $"Screenshot_{timestamp}.{format.ToLower()}";
    }

    public void SaveImage(BitmapSource image, Models.SaveDestination destination, string? filePath = null, string format = "PNG")
    {
        switch (destination)
        {
            case Models.SaveDestination.Clipboard:
                SaveToClipboard(image);
                break;
            case Models.SaveDestination.File:
                if (filePath != null)
                    SaveToFile(image, filePath, format);
                break;
            case Models.SaveDestination.Both:
                SaveToClipboard(image);
                if (filePath != null)
                    SaveToFile(image, filePath, format);
                break;
        }
    }
    
    /// <summary>
    /// Validates file path to prevent path traversal and other security issues
    /// </summary>
    private void ValidateFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be empty.", nameof(filePath));
        }
        
        try
        {
            // Get the full path and ensure it's valid
            var fullPath = Path.GetFullPath(filePath);
            
            // Check for path traversal attempts
            if (fullPath.Contains(".."))
            {
                throw new ArgumentException("Path traversal detected in file path.", nameof(filePath));
            }
            
            // Ensure the path is not a root directory
            if (Path.GetFileName(fullPath) == string.Empty)
            {
                throw new ArgumentException("Cannot save to a directory. Please specify a file name.", nameof(filePath));
            }
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new ArgumentException("Invalid file path.", nameof(filePath), ex);
        }
    }
}
