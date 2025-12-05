using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MoneyShot.Services;

public class SaveService
{
    public void SaveToClipboard(BitmapSource image)
    {
        Clipboard.SetImage(image);
    }

    public void SaveToFile(BitmapSource image, string filePath, string format = "PNG")
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

        using var fileStream = new FileStream(filePath, FileMode.Create);
        encoder.Save(fileStream);
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
}
