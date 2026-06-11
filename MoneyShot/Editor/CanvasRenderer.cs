using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MoneyShot.Editor;

/// <summary>
/// Pure rendering helpers extracted from EditorWindow. None of these depend on editor state
/// beyond their parameters, so they're easy to unit-test in isolation if we ever add WPF tests.
/// </summary>
internal static class CanvasRenderer
{
    private const int RenderDpi = 96;
    private const int PixelateBlockSize = 10;

    /// <summary>
    /// Renders the editor canvas to a bitmap matching the underlying image's pixel dimensions.
    /// Temporarily disables the zoom and pan transforms so the saved image captures the full
    /// frame at native resolution, regardless of how the user has panned/zoomed the editor view.
    /// Both transforms are restored afterwards so the user doesn't see their viewport jump.
    /// </summary>
    public static BitmapSource CaptureCanvasAsImage(FrameworkElement imageCanvas, BitmapSource originalImage, ScaleTransform zoomTransform, TranslateTransform panTransform)
    {
        var imageWidth = originalImage.PixelWidth;
        var imageHeight = originalImage.PixelHeight;

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
        imageCanvas.UpdateLayout();

        var renderBitmap = new RenderTargetBitmap(imageWidth, imageHeight, RenderDpi, RenderDpi, PixelFormats.Pbgra32);
        renderBitmap.Render(imageCanvas);

        zoomTransform.ScaleX = originalScaleX;
        zoomTransform.ScaleY = originalScaleY;
        panTransform.X = originalPanX;
        panTransform.Y = originalPanY;
        imageCanvas.UpdateLayout();

        return renderBitmap;
    }

    /// <summary>
    /// Builds an ImageBrush whose contents are a block-averaged version of the area beneath the
    /// supplied rectangle, producing the classic "censor bar" pixelation effect.
    ///
    /// Performance note: the previous implementation rendered the ENTIRE screenshot into a
    /// RenderTargetBitmap (~33 MB at 4K) and allocated a CroppedBitmap per block. This version
    /// copies only the covered region once and averages blocks in that single buffer, which is
    /// both faster on mouse-up and a large RAM saving.
    /// </summary>
    public static Brush CreatePixelatedBrush(Rectangle pixelateRect, BitmapSource originalImage)
    {
        var left = (int)Math.Round(CanvasPosition.GetLeft(pixelateRect));
        var top = (int)Math.Round(CanvasPosition.GetTop(pixelateRect));
        var width = (int)pixelateRect.Width;
        var height = (int)pixelateRect.Height;
        if (width <= 0 || height <= 0) return pixelateRect.Fill;

        try
        {
            // Clamp the sampled region to the image; the brush stretches to the rect, so a
            // rectangle that hangs off the image edge still gets full coverage.
            var srcX = Math.Max(0, Math.Min(left, originalImage.PixelWidth - 1));
            var srcY = Math.Max(0, Math.Min(top, originalImage.PixelHeight - 1));
            var srcW = Math.Min(width, originalImage.PixelWidth - srcX);
            var srcH = Math.Min(height, originalImage.PixelHeight - srcY);
            if (srcW <= 0 || srcH <= 0) return pixelateRect.Fill;

            BitmapSource source = originalImage.Format == PixelFormats.Bgra32 || originalImage.Format == PixelFormats.Pbgra32
                ? originalImage
                : new FormatConvertedBitmap(originalImage, PixelFormats.Bgra32, null, 0);

            var stride = srcW * 4;
            var pixels = new byte[stride * srcH];
            source.CopyPixels(new Int32Rect(srcX, srcY, srcW, srcH), pixels, stride, 0);

            // Average each block, then write the averaged colour back over the block's pixels.
            for (var blockTop = 0; blockTop < srcH; blockTop += PixelateBlockSize)
            {
                var blockH = Math.Min(PixelateBlockSize, srcH - blockTop);
                for (var blockLeft = 0; blockLeft < srcW; blockLeft += PixelateBlockSize)
                {
                    var blockW = Math.Min(PixelateBlockSize, srcW - blockLeft);

                    long sumB = 0, sumG = 0, sumR = 0;
                    for (var y = 0; y < blockH; y++)
                    {
                        var offset = (blockTop + y) * stride + blockLeft * 4;
                        for (var x = 0; x < blockW; x++)
                        {
                            sumB += pixels[offset];
                            sumG += pixels[offset + 1];
                            sumR += pixels[offset + 2];
                            offset += 4;
                        }
                    }

                    var count = blockW * blockH;
                    var b = (byte)(sumB / count);
                    var g = (byte)(sumG / count);
                    var r = (byte)(sumR / count);

                    for (var y = 0; y < blockH; y++)
                    {
                        var offset = (blockTop + y) * stride + blockLeft * 4;
                        for (var x = 0; x < blockW; x++)
                        {
                            pixels[offset] = b;
                            pixels[offset + 1] = g;
                            pixels[offset + 2] = r;
                            pixels[offset + 3] = 0xFF;
                            offset += 4;
                        }
                    }
                }
            }

            var pixelated = BitmapSource.Create(srcW, srcH, RenderDpi, RenderDpi, PixelFormats.Bgra32, null, pixels, stride);
            pixelated.Freeze();
            var brush = new ImageBrush(pixelated) { Stretch = Stretch.Fill };
            brush.Freeze();
            return brush;
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
