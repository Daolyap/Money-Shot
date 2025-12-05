using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MoneyShot.Models;
using MoneyShot.Services;

namespace MoneyShot.Views;

public partial class EditorWindow : Window
{
    private BitmapSource _originalImage;
    private AnnotationTool _currentTool = AnnotationTool.None;
    private Color _currentColor = Colors.Red;
    private int _lineThickness = 3;
    private Point _startPoint;
    private Shape? _currentShape;
    private bool _isDrawing;
    private readonly SaveService _saveService;
    private readonly Stack<UIElement> _undoStack = new();
    private int _numberCounter = 1;
    
    // Selection/move fields
    private UIElement? _selectedElement;
    private Point _dragStartPoint;
    private bool _isDragging;
    private Border? _selectionBorder;
    private double _zoomLevel = 1.0;
    private const double ZoomIncrement = 0.25;
    private const double MinZoom = 0.25;
    private const double MaxZoom = 4.0;
    
    // Cached pen for hit testing to avoid repeated allocations
    private static readonly Pen HitTestPen = new(Brushes.Black, 10);

    public EditorWindow(BitmapSource image)
    {
        InitializeComponent();
        _originalImage = image;
        _saveService = new SaveService();
        DisplayImage();
        SetupToolbar();
        
        // Add keyboard event handler for Delete key
        KeyDown += EditorWindow_KeyDown;
    }
    
    private void EditorWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && _selectedElement != null)
        {
            DeleteSelectedElement();
        }
    }
    
    private void DeleteSelectedElement()
    {
        if (_selectedElement != null)
        {
            DrawingCanvas.Children.Remove(_selectedElement);
            ClearSelection();
        }
    }

    private void DisplayImage()
    {
        ImageDisplay.Source = _originalImage;
        ImageDisplay.Width = _originalImage.PixelWidth;
        ImageDisplay.Height = _originalImage.PixelHeight;
    }

    private void SetupToolbar()
    {
        // Tool buttons will be set up in XAML
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        var clickPoint = e.GetPosition(DrawingCanvas);

        // Handle cursor mode for selection and moving
        if (_currentTool == AnnotationTool.Cursor)
        {
            // Try to find an element at the click position
            var hitElement = FindElementAtPoint(clickPoint);
            
            if (hitElement != null)
            {
                SelectElement(hitElement);
                _isDragging = true;
                _dragStartPoint = clickPoint;
            }
            else
            {
                ClearSelection();
            }
            return;
        }

        if (_currentTool == AnnotationTool.None)
            return;

        _isDrawing = true;
        _startPoint = clickPoint;

        UIElement? element = _currentTool switch
        {
            AnnotationTool.Rectangle => CreateRectangle(),
            AnnotationTool.Circle => CreateEllipse(),
            AnnotationTool.Arrow => CreateArrow(),
            AnnotationTool.Line => CreateLine(),
            AnnotationTool.Number => CreateNumberLabel(),
            AnnotationTool.Text => CreateTextLabel(),
            AnnotationTool.Blur => CreateBlurRectangle(),
            _ => null
        };

        if (element != null)
        {
            DrawingCanvas.Children.Add(element);
            if (element is Shape shape)
            {
                _currentShape = shape;
            }
        }
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        var currentPoint = e.GetPosition(DrawingCanvas);

        // Handle cursor mode for dragging elements
        if (_currentTool == AnnotationTool.Cursor && _isDragging && _selectedElement != null)
        {
            var deltaX = currentPoint.X - _dragStartPoint.X;
            var deltaY = currentPoint.Y - _dragStartPoint.Y;
            
            MoveElement(_selectedElement, deltaX, deltaY);
            _dragStartPoint = currentPoint;
            return;
        }

        if (!_isDrawing || _currentShape == null)
            return;

        switch (_currentTool)
        {
            case AnnotationTool.Rectangle:
            case AnnotationTool.Blur:
                UpdateRectangle(currentPoint);
                break;
            case AnnotationTool.Circle:
                UpdateEllipse(currentPoint);
                break;
            case AnnotationTool.Line:
                UpdateLine(currentPoint);
                break;
            case AnnotationTool.Arrow:
                UpdateArrow(currentPoint);
                break;
        }
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_currentTool == AnnotationTool.Cursor)
        {
            _isDragging = false;
            return;
        }

        if (_isDrawing && _currentShape != null)
        {
            _undoStack.Push(_currentShape);
        }
        _isDrawing = false;
        _currentShape = null;
    }

    private Rectangle CreateRectangle()
    {
        return new Rectangle
        {
            Stroke = new SolidColorBrush(_currentColor),
            StrokeThickness = _lineThickness,
            Fill = Brushes.Transparent
        };
    }

    private Ellipse CreateEllipse()
    {
        return new Ellipse
        {
            Stroke = new SolidColorBrush(_currentColor),
            StrokeThickness = _lineThickness,
            Fill = Brushes.Transparent
        };
    }

    private Line CreateLine()
    {
        return new Line
        {
            Stroke = new SolidColorBrush(_currentColor),
            StrokeThickness = _lineThickness,
            X1 = _startPoint.X,
            Y1 = _startPoint.Y
        };
    }

    private Path CreateArrow()
    {
        var path = new Path
        {
            Stroke = new SolidColorBrush(_currentColor),
            StrokeThickness = _lineThickness,
            Fill = new SolidColorBrush(_currentColor)
        };
        return path;
    }

    private TextBlock CreateNumberLabel()
    {
        var textBlock = new TextBlock
        {
            Text = _numberCounter.ToString(),
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(_currentColor),
            Background = new SolidColorBrush(Colors.White),
            Padding = new Thickness(5)
        };
        Canvas.SetLeft(textBlock, _startPoint.X);
        Canvas.SetTop(textBlock, _startPoint.Y);
        _numberCounter++;
        _isDrawing = false; // Numbers don't need drag
        return textBlock;
    }

    private TextBlock CreateTextLabel()
    {
        // Show a simple input dialog
        var inputDialog = new Window
        {
            Title = "Enter Text",
            Width = 300,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48))
        };

        var grid = new Grid { Margin = new Thickness(10) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "Enter text:",
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 5)
        };
        Grid.SetRow(label, 0);
        grid.Children.Add(label);

        var textBox = new TextBox
        {
            Margin = new Thickness(0, 5, 0, 10),
            Padding = new Thickness(5),
            Background = new SolidColorBrush(Color.FromRgb(62, 62, 66)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 85))
        };
        Grid.SetRow(textBox, 1);
        grid.Children.Add(textBox);

        var okButton = new Button
        {
            Content = "OK",
            Padding = new Thickness(20, 5, 20, 5),
            Background = new SolidColorBrush(Color.FromRgb(14, 99, 156)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(17, 119, 187)),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        okButton.Click += (s, e) => inputDialog.DialogResult = true;
        Grid.SetRow(okButton, 2);
        grid.Children.Add(okButton);

        inputDialog.Content = grid;
        textBox.Focus();

        var textBlock = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeights.Normal,
            Foreground = new SolidColorBrush(_currentColor),
            Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            Padding = new Thickness(5)
        };

        if (inputDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(textBox.Text))
        {
            textBlock.Text = textBox.Text;
        }
        else
        {
            textBlock.Text = "Text";
        }

        Canvas.SetLeft(textBlock, _startPoint.X);
        Canvas.SetTop(textBlock, _startPoint.Y);
        _isDrawing = false; // Text doesn't need drag
        return textBlock;
    }

    private Rectangle CreateBlurRectangle()
    {
        // Create an effective blur/pixelate effect that obscures content
        var rect = new Rectangle
        {
            Stroke = new SolidColorBrush(Colors.Transparent),
            StrokeThickness = 0,
            // Use a solid opaque fill with strong blur for effective obscuring
            Fill = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
            Effect = new System.Windows.Media.Effects.BlurEffect 
            { 
                Radius = 60, 
                KernelType = System.Windows.Media.Effects.KernelType.Box 
            }
        };
        return rect;
    }

    private void UpdateRectangle(Point currentPoint)
    {
        if (_currentShape is not Rectangle rect) return;

        var x = Math.Min(_startPoint.X, currentPoint.X);
        var y = Math.Min(_startPoint.Y, currentPoint.Y);
        var width = Math.Abs(_startPoint.X - currentPoint.X);
        var height = Math.Abs(_startPoint.Y - currentPoint.Y);

        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        rect.Width = width;
        rect.Height = height;
    }

    private void UpdateEllipse(Point currentPoint)
    {
        if (_currentShape is not Ellipse ellipse) return;

        var x = Math.Min(_startPoint.X, currentPoint.X);
        var y = Math.Min(_startPoint.Y, currentPoint.Y);
        var width = Math.Abs(_startPoint.X - currentPoint.X);
        var height = Math.Abs(_startPoint.Y - currentPoint.Y);

        Canvas.SetLeft(ellipse, x);
        Canvas.SetTop(ellipse, y);
        ellipse.Width = width;
        ellipse.Height = height;
    }

    private void UpdateLine(Point currentPoint)
    {
        if (_currentShape is Line line)
        {
            line.X2 = currentPoint.X;
            line.Y2 = currentPoint.Y;
        }
    }

    private void UpdateArrow(Point currentPoint)
    {
        if (_currentShape is not Path arrow) return;

        var dx = currentPoint.X - _startPoint.X;
        var dy = currentPoint.Y - _startPoint.Y;
        var angle = Math.Atan2(dy, dx);
        var length = Math.Sqrt(dx * dx + dy * dy);

        // Create arrow geometry
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = _startPoint };

        // Arrow line
        figure.Segments.Add(new LineSegment(currentPoint, true));

        // Arrow head
        var arrowHeadLength = Math.Min(20, length / 3);
        var arrowHeadAngle = Math.PI / 6; // 30 degrees

        var leftPoint = new Point(
            currentPoint.X - arrowHeadLength * Math.Cos(angle - arrowHeadAngle),
            currentPoint.Y - arrowHeadLength * Math.Sin(angle - arrowHeadAngle)
        );
        var rightPoint = new Point(
            currentPoint.X - arrowHeadLength * Math.Cos(angle + arrowHeadAngle),
            currentPoint.Y - arrowHeadLength * Math.Sin(angle + arrowHeadAngle)
        );

        figure.Segments.Add(new LineSegment(leftPoint, false));
        figure.Segments.Add(new LineSegment(currentPoint, true));
        figure.Segments.Add(new LineSegment(rightPoint, true));
        figure.Segments.Add(new LineSegment(currentPoint, true));

        geometry.Figures.Add(figure);
        arrow.Data = geometry;
    }

    private UIElement? FindElementAtPoint(Point point)
    {
        // Search through canvas children in reverse order (top to bottom)
        for (int i = DrawingCanvas.Children.Count - 1; i >= 0; i--)
        {
            var element = DrawingCanvas.Children[i];
            
            // Skip the selection border itself
            if (element == _selectionBorder)
                continue;
            
            // Check if point is within element bounds
            if (IsPointInElement(element, point))
            {
                return element;
            }
        }
        return null;
    }

    private bool IsPointInElement(UIElement element, Point point)
    {
        var left = Canvas.GetLeft(element);
        var top = Canvas.GetTop(element);
        
        // Handle NaN values (elements without explicit positioning)
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;

        if (element is Path path)
        {
            // For paths (arrows), use geometry-based hit testing
            if (path.Data is PathGeometry pathGeometry)
            {
                // Adjust point for canvas positioning
                var adjustedPoint = new Point(point.X - left, point.Y - top);
                return pathGeometry.FillContains(adjustedPoint) || 
                       pathGeometry.StrokeContains(HitTestPen, adjustedPoint);
            }
            return false;
        }
        else if (element is Shape shape && !(element is Line))
        {
            var width = shape.Width;
            var height = shape.Height;
            
            if (double.IsNaN(width) || double.IsNaN(height))
                return false;
                
            return point.X >= left && point.X <= left + width &&
                   point.Y >= top && point.Y <= top + height;
        }
        else if (element is TextBlock textBlock)
        {
            var width = textBlock.ActualWidth;
            var height = textBlock.ActualHeight;
            
            return point.X >= left && point.X <= left + width &&
                   point.Y >= top && point.Y <= top + height;
        }
        else if (element is Line line)
        {
            // Check if point is near the line
            var distance = DistanceFromPointToLine(point, new Point(line.X1, line.Y1), new Point(line.X2, line.Y2));
            return distance < 10; // 10 pixel tolerance
        }
        
        return false;
    }

    private double DistanceFromPointToLine(Point p, Point lineStart, Point lineEnd)
    {
        var dx = lineEnd.X - lineStart.X;
        var dy = lineEnd.Y - lineStart.Y;
        var lengthSquared = dx * dx + dy * dy;
        
        if (lengthSquared == 0)
            return Math.Sqrt((p.X - lineStart.X) * (p.X - lineStart.X) + (p.Y - lineStart.Y) * (p.Y - lineStart.Y));
        
        var t = Math.Max(0, Math.Min(1, ((p.X - lineStart.X) * dx + (p.Y - lineStart.Y) * dy) / lengthSquared));
        var projX = lineStart.X + t * dx;
        var projY = lineStart.Y + t * dy;
        
        return Math.Sqrt((p.X - projX) * (p.X - projX) + (p.Y - projY) * (p.Y - projY));
    }

    private void SelectElement(UIElement element)
    {
        ClearSelection();
        _selectedElement = element;
        
        // Add visual indicator for selection
        _selectionBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Colors.Blue),
            BorderThickness = new Thickness(2),
            IsHitTestVisible = false
        };
        
        var left = Canvas.GetLeft(element);
        var top = Canvas.GetTop(element);
        
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;
        
        double width = 0, height = 0;
        
        if (element is Path path)
        {
            // For paths, get bounds from geometry
            var bounds = path.Data.Bounds;
            left += bounds.Left;
            top += bounds.Top;
            width = bounds.Width;
            height = bounds.Height;
        }
        else if (element is Shape shape && !(element is Line))
        {
            width = shape.Width;
            height = shape.Height;
        }
        else if (element is TextBlock textBlock)
        {
            width = textBlock.ActualWidth;
            height = textBlock.ActualHeight;
        }
        else if (element is Line line)
        {
            left = Math.Min(line.X1, line.X2);
            top = Math.Min(line.Y1, line.Y2);
            width = Math.Abs(line.X2 - line.X1);
            height = Math.Abs(line.Y2 - line.Y1);
        }
        
        Canvas.SetLeft(_selectionBorder, left - 2);
        Canvas.SetTop(_selectionBorder, top - 2);
        _selectionBorder.Width = width + 4;
        _selectionBorder.Height = height + 4;
        
        DrawingCanvas.Children.Add(_selectionBorder);
    }

    private void ClearSelection()
    {
        if (_selectionBorder != null)
        {
            DrawingCanvas.Children.Remove(_selectionBorder);
            _selectionBorder = null;
        }
        _selectedElement = null;
    }

    private void MoveElement(UIElement element, double deltaX, double deltaY)
    {
        if (element is Shape shape && !(element is Line))
        {
            var left = Canvas.GetLeft(shape);
            var top = Canvas.GetTop(shape);
            
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;
            
            Canvas.SetLeft(shape, left + deltaX);
            Canvas.SetTop(shape, top + deltaY);
        }
        else if (element is TextBlock textBlock)
        {
            var left = Canvas.GetLeft(textBlock);
            var top = Canvas.GetTop(textBlock);
            
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;
            
            Canvas.SetLeft(textBlock, left + deltaX);
            Canvas.SetTop(textBlock, top + deltaY);
        }
        else if (element is Line line)
        {
            line.X1 += deltaX;
            line.Y1 += deltaY;
            line.X2 += deltaX;
            line.Y2 += deltaY;
        }
        else if (element is Path path)
        {
            // For paths (arrows), use Canvas positioning like other elements
            var left = Canvas.GetLeft(path);
            var top = Canvas.GetTop(path);
            
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;
            
            Canvas.SetLeft(path, left + deltaX);
            Canvas.SetTop(path, top + deltaY);
        }
        
        // Update selection border position
        if (_selectionBorder != null)
        {
            var borderLeft = Canvas.GetLeft(_selectionBorder);
            var borderTop = Canvas.GetTop(_selectionBorder);
            
            if (double.IsNaN(borderLeft)) borderLeft = 0;
            if (double.IsNaN(borderTop)) borderTop = 0;
            
            Canvas.SetLeft(_selectionBorder, borderLeft + deltaX);
            Canvas.SetTop(_selectionBorder, borderTop + deltaY);
        }
    }

    private void ToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string toolName)
        {
            if (Enum.TryParse<AnnotationTool>(toolName, out var tool))
            {
                _currentTool = tool;
            }
        }
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Background is SolidColorBrush brush)
        {
            _currentColor = brush.Color;
        }
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undoStack.Count > 0)
        {
            var element = _undoStack.Pop();
            DrawingCanvas.Children.Remove(element);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var finalImage = CaptureCanvasAsImage();
        
        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG Image|*.png|JPEG Image|*.jpg|Bitmap Image|*.bmp",
            DefaultExt = ".png",
            FileName = _saveService.GenerateFileName("PNG")
        };

        if (saveDialog.ShowDialog() == true)
        {
            var extension = System.IO.Path.GetExtension(saveDialog.FileName).TrimStart('.');
            var format = GetFileFormat(extension);
            _saveService.SaveToFile(finalImage, saveDialog.FileName, format);
            MessageBox.Show("Image saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private string GetFileFormat(string extension)
    {
        return extension.ToUpper() switch
        {
            "JPG" or "JPEG" => "JPG",
            "BMP" => "BMP",
            _ => "PNG"
        };
    }

    private void SaveToClipboard_Click(object sender, RoutedEventArgs e)
    {
        var finalImage = CaptureCanvasAsImage();
        _saveService.SaveToClipboard(finalImage);
        MessageBox.Show("Image copied to clipboard!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        if (_zoomLevel < MaxZoom)
        {
            _zoomLevel += ZoomIncrement;
            ApplyZoom();
        }
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        if (_zoomLevel > MinZoom)
        {
            _zoomLevel -= ZoomIncrement;
            ApplyZoom();
        }
    }

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
    {
        _zoomLevel = 1.0;
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        ZoomTransform.ScaleX = _zoomLevel;
        ZoomTransform.ScaleY = _zoomLevel;
    }

    private BitmapSource CaptureCanvasAsImage()
    {
        var renderBitmap = new RenderTargetBitmap(
            (int)ImageCanvas.ActualWidth,
            (int)ImageCanvas.ActualHeight,
            96, 96,
            PixelFormats.Pbgra32);

        renderBitmap.Render(ImageCanvas);
        return renderBitmap;
    }
}
