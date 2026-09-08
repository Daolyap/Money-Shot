using System.Windows.Media;
using System.Windows.Media.Imaging;
using MoneyShot.Abstractions;

namespace MoneyShot.Interop;

/// <summary>
/// Converts between the platform-neutral MoneyShot.Abstractions.CapturedImage (raw BGRA32 pixels)
/// and WPF's BitmapSource, at the UI-layer boundary. Once the UI ports to Avalonia (LINUX_PORT.md
/// Phase 1) this file gets an Avalonia-side twin; MoneyShot.Core and MoneyShot.Platform.* never
/// reference either bitmap type.
/// </summary>
public static class BitmapConversions
{
    public static BitmapSource ToBitmapSource(this CapturedImage image)
    {
        var bitmapSource = BitmapSource.Create(
            image.Width, image.Height,
            96, 96,
            PixelFormats.Pbgra32,
            null,
            image.PixelDataBgra32,
            image.Stride);
        bitmapSource.Freeze();
        return bitmapSource;
    }

    public static CapturedImage ToCapturedImage(this BitmapSource source)
    {
        var converted = source.Format == PixelFormats.Pbgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);

        var stride = converted.PixelWidth * 4;
        var buffer = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(buffer, stride, 0);
        return new CapturedImage(converted.PixelWidth, converted.PixelHeight, stride, buffer);
    }
}
