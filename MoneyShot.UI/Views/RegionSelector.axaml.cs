using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MoneyShot.Abstractions;
using MoneyShot.UI.Interop;
using DrawingRectangle = System.Drawing.Rectangle;

namespace MoneyShot.UI.Views;

/// <summary>
/// Avalonia port of MoneyShot/Views/RegionSelector.xaml.cs — see LINUX_PORT.md Phase 1. Ports the
/// ALREADY-DPI-FIXED version of the WPF window (see Opus-Speaks.md § E2 / CLAUDE.md), not the
/// original buggy one, and is actually simpler here: Avalonia's Screens API exposes each monitor's
/// scale factor directly (Screen.Scaling), so there's no Win32 P/Invoke needed for the
/// window-creation-time DIP conversion the WPF version required.
/// </summary>
public partial class RegionSelector : Window
{
    private Point _startPoint;
    private Rectangle? _selectionRectangle;
    private bool _isSelecting;
    private int _virtualScreenLeft;
    private int _virtualScreenTop;
    private double _creationDpiScale = 1.0;
    private readonly Bitmap _frozenScreen;

    public DrawingRectangle? SelectedRegion { get; private set; }
    public Bitmap? CroppedScreenshot { get; private set; }

    public RegionSelector(Bitmap frozenScreen)
    {
        InitializeComponent();
        _frozenScreen = frozenScreen;
        SetupFullScreenOverlay(frozenScreen);
    }

    private void SetupFullScreenOverlay(Bitmap frozenScreen)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

        foreach (var screen in Screens.All)
        {
            minX = Math.Min(minX, screen.Bounds.X);
            minY = Math.Min(minY, screen.Bounds.Y);
            maxX = Math.Max(maxX, screen.Bounds.X + screen.Bounds.Width);
            maxY = Math.Max(maxY, screen.Bounds.Y + screen.Bounds.Height);
        }

        _virtualScreenLeft = minX;
        _virtualScreenTop = minY;

        // Avalonia's Window Position/Width/Height are DIPs, but the monitor bounds above are
        // physical pixels. Unlike WPF, Avalonia's Screens collection is queryable before the
        // window is shown and gives each monitor's own scale directly (no P/Invoke needed) — use
        // the primary screen's as the best-effort guess for sizing, same reasoning as the WPF fix:
        // the crop math at mouse-up re-queries the window's actual live scale rather than trusting
        // this value.
        _creationDpiScale = Screens.Primary?.Scaling ?? 1.0;

        Position = new PixelPoint((int)Math.Round(minX / _creationDpiScale), (int)Math.Round(minY / _creationDpiScale));
        Width = (maxX - minX) / _creationDpiScale;
        Height = (maxY - minY) / _creationDpiScale;

        Cursor = new Cursor(StandardCursorType.Cross);

        if (BackgroundImage != null)
        {
            BackgroundImage.Source = frozenScreen;
            BackgroundImage.Stretch = Stretch.Fill;
        }

        UpdateDimOverlay(null);
    }

    private void UpdateDimOverlay(Rect? selection)
    {
        var full = new RectangleGeometry(new Rect(0, 0, Width, Height));
        if (selection is { } rect)
        {
            DimOverlay.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(rect));
        }
        else
        {
            DimOverlay.Data = full;
        }
    }

    private void UpdateSizeBadge(Rect selection)
    {
        SizeBadge.IsVisible = true;
        SizeBadgeText.Text = $"{(int)selection.Width} × {(int)selection.Height}";

        SizeBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var badgeWidth = SizeBadge.DesiredSize.Width;
        var badgeHeight = SizeBadge.DesiredSize.Height;

        var x = Math.Max(4, Math.Min(selection.Right - badgeWidth, Width - badgeWidth - 4));
        var y = selection.Bottom + 8;
        if (y + badgeHeight > Height - 4)
        {
            y = selection.Top - badgeHeight - 8;
        }
        SizeBadge.Margin = new Thickness(x, Math.Max(4, y), 0, 0);
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _isSelecting = true;
        _startPoint = e.GetPosition(this);
        HintBadge.IsVisible = false;

        _selectionRectangle = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0xC2, 0x8E, 0x5C)),
            StrokeThickness = 1.5,
            Fill = Brushes.Transparent
        };

        Canvas.SetLeft(_selectionRectangle, _startPoint.X);
        Canvas.SetTop(_selectionRectangle, _startPoint.Y);
        SelectionCanvas.Children.Add(_selectionRectangle);
        e.Pointer.Capture(SelectionCanvas);
    }

    private void Window_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isSelecting && _selectionRectangle != null)
        {
            var currentPoint = e.GetPosition(this);

            var x = Math.Min(_startPoint.X, currentPoint.X);
            var y = Math.Min(_startPoint.Y, currentPoint.Y);
            var width = Math.Abs(_startPoint.X - currentPoint.X);
            var height = Math.Abs(_startPoint.Y - currentPoint.Y);

            Canvas.SetLeft(_selectionRectangle, x);
            Canvas.SetTop(_selectionRectangle, y);
            _selectionRectangle.Width = width;
            _selectionRectangle.Height = height;

            var selection = new Rect(x, y, width, height);
            UpdateDimOverlay(selection);
            UpdateSizeBadge(selection);
        }
    }

    private void Window_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isSelecting && _selectionRectangle != null)
        {
            _isSelecting = false;
            e.Pointer.Capture(null);

            var dipX = Canvas.GetLeft(_selectionRectangle);
            var dipY = Canvas.GetTop(_selectionRectangle);
            var dipWidth = _selectionRectangle.Width;
            var dipHeight = _selectionRectangle.Height;

            if (dipWidth > 10 && dipHeight > 10)
            {
                // Fresh query of the window's actual current scale, same reasoning as the WPF fix:
                // correct even if the window somehow ended up on a different-scaled monitor than
                // the creation-time guess.
                var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? _creationDpiScale;
                var x = (int)Math.Round(dipX * scale);
                var y = (int)Math.Round(dipY * scale);
                var width = (int)Math.Round(dipWidth * scale);
                var height = (int)Math.Round(dipHeight * scale);

                var absoluteX = x + _virtualScreenLeft;
                var absoluteY = y + _virtualScreenTop;

                SelectedRegion = new DrawingRectangle(absoluteX, absoluteY, width, height);

                try
                {
                    var cropX = absoluteX - _virtualScreenLeft;
                    var cropY = absoluteY - _virtualScreenTop;

                    cropX = Math.Max(0, Math.Min(cropX, _frozenScreen.PixelSize.Width - width));
                    cropY = Math.Max(0, Math.Min(cropY, _frozenScreen.PixelSize.Height - height));
                    width = Math.Min(width, _frozenScreen.PixelSize.Width - cropX);
                    height = Math.Min(height, _frozenScreen.PixelSize.Height - cropY);

                    var captured = _frozenScreen.ToCapturedImage();
                    CroppedScreenshot = CropCapturedImage(captured, cropX, cropY, width, height).ToAvaloniaBitmap();

                    Close(true);
                    return;
                }
                catch (Exception ex)
                {
                    MoneyShot.Services.Logger.Error("Error cropping frozen screenshot", ex);
                }
            }

            Close(false);
        }
    }

    private static CapturedImage CropCapturedImage(CapturedImage source, int x, int y, int width, int height)
    {
        // CapturedImage is always BGRA32 (4 bytes/pixel) per its contract — deriving this from
        // source.Stride/source.Width instead would be wrong whenever a row has alignment padding.
        const int bytesPerPixel = 4;
        var destStride = width * bytesPerPixel;
        var dest = new byte[destStride * height];
        for (var row = 0; row < height; row++)
        {
            var srcOffset = (y + row) * source.Stride + x * bytesPerPixel;
            System.Buffer.BlockCopy(source.PixelDataBgra32, srcOffset, dest, row * destStride, destStride);
        }
        return new CapturedImage(width, height, destStride, dest);
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(false);
        }
    }
}
