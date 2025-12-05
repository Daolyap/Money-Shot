using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    public DrawingRectangle? SelectedRegion { get; private set; }

    public RegionSelector()
    {
        InitializeComponent();
        SetupFullScreenOverlay();
    }

    private void SetupFullScreenOverlay()
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

        // Set window to cover all screens
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Topmost = true;
        
        // Position and size to cover entire virtual screen
        Left = minX;
        Top = minY;
        Width = maxX - minX;
        Height = maxY - minY;
        
        Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));
        Cursor = Cursors.Cross;
        
        // Show immediately to prevent black screen
        ShowInTaskbar = false;
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isSelecting = true;
            _startPoint = e.GetPosition(this);

            _selectionRectangle = new Rectangle
            {
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(50, 255, 0, 0))
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
        }
    }

    private void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isSelecting && _selectionRectangle != null)
        {
            _isSelecting = false;

            var x = (int)Canvas.GetLeft(_selectionRectangle);
            var y = (int)Canvas.GetTop(_selectionRectangle);
            var width = (int)_selectionRectangle.Width;
            var height = (int)_selectionRectangle.Height;

            if (width > 10 && height > 10)
            {
                // Adjust coordinates to account for virtual screen offset
                // The canvas is positioned relative to the window, which starts at virtual screen origin
                var absoluteX = x + _virtualScreenLeft;
                var absoluteY = y + _virtualScreenTop;
                
                SelectedRegion = new DrawingRectangle(absoluteX, absoluteY, width, height);
                DialogResult = true;
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
}
