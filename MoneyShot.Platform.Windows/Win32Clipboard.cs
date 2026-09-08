using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MoneyShot.Abstractions;

namespace MoneyShot.Platform.Windows;

/// <summary>
/// WinForms-clipboard-based image clipboard (see LINUX_PORT.md § Clipboard image support for the
/// MIME-typed X11/Wayland selection equivalent via Avalonia's IClipboard). Uses
/// System.Windows.Forms.Clipboard rather than System.Windows.Clipboard so this project has no WPF
/// dependency — both work identically for image data, since it's the same OS clipboard underneath.
/// </summary>
public sealed class Win32Clipboard : IClipboard
{
    public void SetImage(CapturedImage image)
    {
        using var bitmap = ToBitmap(image);
        Clipboard.SetImage(bitmap);
    }

    private static Bitmap ToBitmap(CapturedImage image)
    {
        var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppPArgb);
        var rect = new Rectangle(0, 0, image.Width, image.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            // Row-by-row copy in case CapturedImage.Stride differs from the Bitmap's own stride.
            var rowBytes = Math.Min(image.Stride, data.Stride);
            for (var y = 0; y < image.Height; y++)
            {
                Marshal.Copy(image.PixelDataBgra32, y * image.Stride, data.Scan0 + y * data.Stride, rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }
}
