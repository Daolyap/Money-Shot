using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DrawingRectangle = System.Drawing.Rectangle;

namespace MoneyShot.Views;

public partial class RegionSelector : Window
{
    private Point _startPoint;
    private Rectangle? _selectionRectangle;
    private bool _isSelecting;
    private int _virtualScreenLeft;
    private int _virtualScreenTop;
    private double _creationDpiScale = 1.0;
    private readonly BitmapSource _frozenScreen;

    public DrawingRectangle? SelectedRegion { get; private set; }
    public BitmapSource? CroppedScreenshot { get; private set; }

    public RegionSelector(BitmapSource frozenScreen)
    {
        InitializeComponent();
        _frozenScreen = frozenScreen;
        SetupFullScreenOverlay(frozenScreen);
    }

    private void SetupFullScreenOverlay(BitmapSource frozenScreen)
    {
        // Calculate virtual screen bounds (all monitors)
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

        _virtualScreenLeft = minX;
        _virtualScreenTop = minY;

        // WPF interprets Left/Top/Width/Height as DIPs, but minX/minY/maxX/maxY above are
        // physical pixels (WinForms Screen API). Before the HWND exists WPF has no
        // window-specific DPI to consult, so approximate with the process's system DPI, which
        // for a Per-Monitor-V2-aware process tracks the primary monitor's effective DPI. This is
        // a best-effort guess used only for sizing the overlay — the crop math in Window_MouseUp
        // re-queries the window's actual runtime DPI rather than trusting this value.
        _creationDpiScale = GetDpiForSystem() / 96.0;

        // Set window to cover all screens. Deliberately NOT AllowsTransparency: the frozen
        // screenshot fully covers the window, and a layered (transparent) window this size is
        // composed in software across every monitor — visibly laggy at 4K. The dimming effect
        // is done with the DimOverlay path instead.
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;

        // Position and size to cover entire virtual screen (physical pixels converted to DIPs)
        Left = minX / _creationDpiScale;
        Top = minY / _creationDpiScale;
        Width = (maxX - minX) / _creationDpiScale;
        Height = (maxY - minY) / _creationDpiScale;

        Cursor = Cursors.Cross;

        // Show immediately to prevent black screen
        ShowInTaskbar = false;

        // Display the frozen screen in the background
        try
        {
            if (BackgroundImage != null)
            {
                BackgroundImage.Source = frozenScreen;
                BackgroundImage.Stretch = Stretch.Fill;
            }
        }
        catch (Exception ex)
        {
            MoneyShot.Services.Logger.Error("Error displaying frozen screen", ex);
        }

        // Dim the whole surface until a selection exists.
        UpdateDimOverlay(null);
    }

    /// <summary>
    /// Dims everything outside <paramref name="selection"/>. Null dims the entire surface,
    /// which doubles as the "no selection yet" state.
    /// </summary>
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

    /// <summary>Moves the size badge under the selection (or above it near the bottom edge).</summary>
    private void UpdateSizeBadge(Rect selection)
    {
        SizeBadge.Visibility = Visibility.Visible;
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

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isSelecting = true;
            _startPoint = e.GetPosition(this);
            HintBadge.Visibility = Visibility.Collapsed;

            _selectionRectangle = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0xC2, 0x8E, 0x5C)),
                StrokeThickness = 1.5,
                Fill = Brushes.Transparent
            };

            Canvas.SetLeft(_selectionRectangle, _startPoint.X);
            Canvas.SetTop(_selectionRectangle, _startPoint.Y);
            SelectionCanvas.Children.Add(_selectionRectangle);
        }
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
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

    private void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isSelecting && _selectionRectangle != null)
        {
            _isSelecting = false;

            // Canvas coordinates are in DIPs. Keep them as doubles until the final physical-pixel
            // conversion below — rounding to int here first and scaling afterward would compound
            // rounding error by the DPI factor.
            var dipX = Canvas.GetLeft(_selectionRectangle);
            var dipY = Canvas.GetTop(_selectionRectangle);
            var dipWidth = _selectionRectangle.Width;
            var dipHeight = _selectionRectangle.Height;

            if (dipWidth > 10 && dipHeight > 10)
            {
                // Query the window's actual current DPI fresh, rather than reusing the
                // construction-time _creationDpiScale guess. This is correct even if WPF
                // auto-migrated the window to a different DPI context (WM_DPICHANGED) between
                // construction and this mouse-up.
                var dpi = VisualTreeHelper.GetDpi(this);
                var x = (int)Math.Round(dipX * dpi.DpiScaleX);
                var y = (int)Math.Round(dipY * dpi.DpiScaleY);
                var width = (int)Math.Round(dipWidth * dpi.DpiScaleX);
                var height = (int)Math.Round(dipHeight * dpi.DpiScaleY);

                // Adjust coordinates to account for virtual screen offset (both now physical pixels)
                // The canvas is positioned relative to the window, which starts at virtual screen origin
                var absoluteX = x + _virtualScreenLeft;
                var absoluteY = y + _virtualScreenTop;
                
                SelectedRegion = new DrawingRectangle(absoluteX, absoluteY, width, height);
                
                // Crop the selected region from the frozen screenshot
                try
                {
                    // The frozen screenshot coordinates are relative to the virtual screen
                    // Convert absolute screen coordinates to frozen screenshot coordinates
                    var cropX = absoluteX - _virtualScreenLeft;
                    var cropY = absoluteY - _virtualScreenTop;
                    
                    // Ensure coordinates are within bounds
                    cropX = Math.Max(0, Math.Min(cropX, _frozenScreen.PixelWidth - width));
                    cropY = Math.Max(0, Math.Min(cropY, _frozenScreen.PixelHeight - height));
                    width = Math.Min(width, _frozenScreen.PixelWidth - cropX);
                    height = Math.Min(height, _frozenScreen.PixelHeight - cropY);
                    
                    // Crop the image from the frozen screenshot
                    var croppedBitmap = new CroppedBitmap(_frozenScreen, new Int32Rect(cropX, cropY, width, height));
                    
                    // Freeze it to make it thread-safe
                    croppedBitmap.Freeze();
                    CroppedScreenshot = croppedBitmap;
                    
                    DialogResult = true;
                }
                catch (Exception ex)
                {
                    MoneyShot.Services.Logger.Error("Error cropping frozen screenshot", ex);
                    DialogResult = false;
                }
            }
            else
            {
                DialogResult = false;
            }
            Close();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
