using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace MoneyShot.Services;

public class ScreenshotService
{
    public BitmapSource CaptureFullScreen()
    {
        var bounds = GetVirtualScreenBounds();
        return CaptureRegion(bounds);
    }

    public BitmapSource CaptureRegion(Rectangle region)
    {
        using var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(region.Left, region.Top, 0, 0, region.Size, CopyPixelOperation.SourceCopy);
        }

        return ConvertToBitmapSource(bitmap);
    }

    public BitmapSource CaptureScreen(int screenIndex)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screenIndex < 0 || screenIndex >= screens.Length)
            throw new ArgumentOutOfRangeException(nameof(screenIndex));

        var screen = screens[screenIndex];
        var bounds = screen.Bounds;

        return CaptureRegion(bounds);
    }

    public List<System.Windows.Forms.Screen> GetAllScreens()
    {
        return new List<System.Windows.Forms.Screen>(System.Windows.Forms.Screen.AllScreens);
    }

    private Rectangle GetVirtualScreenBounds()
    {
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;

        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            minX = Math.Min(minX, screen.Bounds.Left);
            minY = Math.Min(minY, screen.Bounds.Top);
            maxX = Math.Max(maxX, screen.Bounds.Right);
            maxY = Math.Max(maxY, screen.Bounds.Bottom);
        }

        return new Rectangle(minX, minY, maxX - minX, maxY - minY);
    }

    private BitmapSource ConvertToBitmapSource(Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            return Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
