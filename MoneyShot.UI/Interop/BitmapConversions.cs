using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MoneyShot.Abstractions;

namespace MoneyShot.UI.Interop;

/// <summary>
/// Converts between the platform-neutral MoneyShot.Abstractions.CapturedImage (raw BGRA32 pixels)
/// and Avalonia's WriteableBitmap/Bitmap, at the UI-layer boundary — the Avalonia twin of
/// MoneyShot/Interop/BitmapConversions.cs (WPF). See LINUX_PORT.md Phase 1.
/// </summary>
public static class BitmapConversions
{
    public static Bitmap ToAvaloniaBitmap(this CapturedImage image)
    {
        var writeable = new WriteableBitmap(
            new PixelSize(image.Width, image.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using (var buffer = writeable.Lock())
        {
            for (var y = 0; y < image.Height; y++)
            {
                Marshal.Copy(image.PixelDataBgra32, y * image.Stride,
                    buffer.Address + y * buffer.RowBytes, Math.Min(image.Stride, buffer.RowBytes));
            }
        }

        return writeable;
    }

    public static CapturedImage ToCapturedImage(this Bitmap bitmap)
    {
        // WriteableBitmap gives direct pixel access via Lock(). Anything else (e.g. a
        // RenderTargetBitmap straight off a rendered canvas) is round-tripped through a PNG encode
        // + decode to reach a WriteableBitmap — slower than a direct pixel blit, but uses only the
        // two most well-established Bitmap APIs (Save/Decode) rather than a lower-level drawing
        // API this port hasn't independently verified. Only used for save-to-clipboard, which is
        // not a hot path.
        WriteableBitmap writeable;
        var ownsWriteable = false;
        if (bitmap is WriteableBitmap wb)
        {
            writeable = wb;
        }
        else
        {
            using var stream = new System.IO.MemoryStream();
            // Save(Stream) is the only overload for "just encode PNG, default settings" — its
            // replacement (a BitmapEncoderOptions overload) isn't otherwise needed here since we
            // want the default encoding anyway.
#pragma warning disable CS0618
            bitmap.Save(stream);
#pragma warning restore CS0618
            stream.Position = 0;
            writeable = WriteableBitmap.Decode(stream);
            ownsWriteable = true;
        }

        try
        {
            using var buffer = writeable.Lock();
            var pixelSize = writeable.PixelSize;
            var stride = buffer.RowBytes;
            var bytes = new byte[stride * pixelSize.Height];
            Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
            return new CapturedImage(pixelSize.Width, pixelSize.Height, stride, bytes);
        }
        finally
        {
            if (ownsWriteable) writeable.Dispose();
        }
    }
}
