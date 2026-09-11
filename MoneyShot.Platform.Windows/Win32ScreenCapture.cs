using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MoneyShot.Abstractions;

namespace MoneyShot.Platform.Windows;

/// <summary>
/// GDI+ screen capture (the Windows implementation of IScreenCapture — see LINUX_PORT.md § Capture
/// pipeline for the X11/Wayland equivalents). Reads pixels directly via Bitmap.LockBits rather than
/// the old GetHbitmap→CreateBitmapSourceFromHBitmap→DeleteObject dance, so there's no HBITMAP handle
/// to leak.
/// </summary>
public sealed class Win32ScreenCapture : IScreenCapture
{
    public CapturedImage CaptureFullScreen()
    {
        return CaptureRegion(GetVirtualScreenBounds());
    }

    public CapturedImage CaptureMonitor(int monitorIndex)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (monitorIndex < 0 || monitorIndex >= screens.Length)
            throw new ArgumentOutOfRangeException(nameof(monitorIndex));

        return CaptureRegion(screens[monitorIndex].Bounds);
    }

    public IReadOnlyList<MonitorInfo> GetAllMonitors()
    {
        return System.Windows.Forms.Screen.AllScreens
            .Select(s => new MonitorInfo(s.Bounds.Left, s.Bounds.Top, s.Bounds.Width, s.Bounds.Height, s.Primary, s.DeviceName))
            .ToList();
    }

    private static CapturedImage CaptureRegion(Rectangle region)
    {
        using var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(region.Left, region.Top, 0, 0, region.Size, CopyPixelOperation.SourceCopy);
        }

        return ToCapturedImage(bitmap);
    }

    private static CapturedImage ToCapturedImage(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var stride = bitmapData.Stride;
            var bytes = new byte[stride * bitmap.Height];
            Marshal.Copy(bitmapData.Scan0, bytes, 0, bytes.Length);
            return new CapturedImage(bitmap.Width, bitmap.Height, stride, bytes);
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

    private static Rectangle GetVirtualScreenBounds()
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
}
