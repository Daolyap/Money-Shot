using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MoneyShot.Abstractions;
using MoneyShot.UI.Interop;

namespace MoneyShot.UI.Editor;

/// <summary>
/// Avalonia port of MoneyShot/Editor/CanvasRenderer.cs — see LINUX_PORT.md Phase 1. Pure
/// rendering helpers extracted from EditorWindow; none depend on editor state beyond their
/// parameters.
/// </summary>
internal static class CanvasRenderer
{
    private const int PixelateBlockSize = 10;

    /// <summary>
    /// Renders the editor canvas to a bitmap matching the underlying image's pixel dimensions.
    /// Temporarily disables the zoom and pan transforms so the saved image captures the full
    /// frame at native resolution, regardless of how the user has panned/zoomed the editor view.
    /// Both transforms are restored afterwards so the user doesn't see their viewport jump.
    /// </summary>
    public static Bitmap CaptureCanvasAsImage(Control imageCanvas, Bitmap originalImage, ScaleTransform zoomTransform, TranslateTransform panTransform)
    {
        var imageWidth = originalImage.PixelSize.Width;
        var imageHeight = originalImage.PixelSize.Height;

        var originalScaleX = zoomTransform.ScaleX;
        var originalScaleY = zoomTransform.ScaleY;
        var originalPanX = panTransform.X;
        var originalPanY = panTransform.Y;
        zoomTransform.ScaleX = 1;
        zoomTransform.ScaleY = 1;
        panTransform.X = 0;
        panTransform.Y = 0;

        imageCanvas.Measure(new Size(imageWidth, imageHeight));
        imageCanvas.Arrange(new Rect(0, 0, imageWidth, imageHeight));

        var renderBitmap = new RenderTargetBitmap(new PixelSize(imageWidth, imageHeight), new Vector(96, 96));
        renderBitmap.Render(imageCanvas);

        zoomTransform.ScaleX = originalScaleX;
        zoomTransform.ScaleY = originalScaleY;
        panTransform.X = originalPanX;
        panTransform.Y = originalPanY;

        return renderBitmap;
    }

    /// <summary>
    /// Builds an ImageBrush whose contents are a block-averaged version of the area beneath the
    /// supplied rectangle, producing the classic "censor bar" pixelation effect. Operates on the
    /// raw CapturedImage pixel buffer (via ToCapturedImage) rather than Avalonia's own
    /// BitmapSource.CopyPixels equivalent, since that's what's already available and tested from
    /// the capture pipeline — same block-averaging math as the WPF version.
    /// </summary>
    public static IBrush CreatePixelatedBrush(Rectangle pixelateRect, Bitmap originalImage)
    {
        var left = (int)Math.Round(CanvasPosition.GetLeft(pixelateRect));
        var top = (int)Math.Round(CanvasPosition.GetTop(pixelateRect));
        var width = (int)pixelateRect.Width;
        var height = (int)pixelateRect.Height;
        if (width <= 0 || height <= 0) return pixelateRect.Fill ?? Brushes.Transparent;

        try
        {
            var captured = originalImage.ToCapturedImage();

            var srcX = Math.Max(0, Math.Min(left, captured.Width - 1));
            var srcY = Math.Max(0, Math.Min(top, captured.Height - 1));
            var srcW = Math.Min(width, captured.Width - srcX);
            var srcH = Math.Min(height, captured.Height - srcY);
            if (srcW <= 0 || srcH <= 0) return pixelateRect.Fill ?? Brushes.Transparent;

            const int bytesPerPixel = 4;
            var stride = srcW * bytesPerPixel;
            var pixels = new byte[stride * srcH];
            for (var row = 0; row < srcH; row++)
            {
                var srcOffset = (srcY + row) * captured.Stride + srcX * bytesPerPixel;
                Buffer.BlockCopy(captured.PixelDataBgra32, srcOffset, pixels, row * stride, stride);
            }

            for (var blockTop = 0; blockTop < srcH; blockTop += PixelateBlockSize)
            {
                var blockH = Math.Min(PixelateBlockSize, srcH - blockTop);
                for (var blockLeft = 0; blockLeft < srcW; blockLeft += PixelateBlockSize)
                {
                    var blockW = Math.Min(PixelateBlockSize, srcW - blockLeft);

                    long sumB = 0, sumG = 0, sumR = 0;
                    for (var y = 0; y < blockH; y++)
                    {
                        var offset = (blockTop + y) * stride + blockLeft * bytesPerPixel;
                        for (var x = 0; x < blockW; x++)
                        {
                            sumB += pixels[offset];
                            sumG += pixels[offset + 1];
                            sumR += pixels[offset + 2];
                            offset += bytesPerPixel;
                        }
                    }

                    var count = blockW * blockH;
                    var b = (byte)(sumB / count);
                    var g = (byte)(sumG / count);
                    var r = (byte)(sumR / count);

                    for (var y = 0; y < blockH; y++)
                    {
                        var offset = (blockTop + y) * stride + blockLeft * bytesPerPixel;
                        for (var x = 0; x < blockW; x++)
                        {
                            pixels[offset] = b;
                            pixels[offset + 1] = g;
                            pixels[offset + 2] = r;
                            pixels[offset + 3] = 0xFF;
                            offset += bytesPerPixel;
                        }
                    }
                }
            }

            var pixelated = new CapturedImage(srcW, srcH, stride, pixels).ToAvaloniaBitmap();
            return new ImageBrush(pixelated) { Stretch = Stretch.Fill };
        }
        catch (ArgumentException)
        {
            return new SolidColorBrush(Color.FromArgb(200, 128, 128, 128));
        }
        catch (InvalidOperationException)
        {
            return new SolidColorBrush(Color.FromArgb(200, 128, 128, 128));
        }
    }
}
