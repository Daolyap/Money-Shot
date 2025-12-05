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

    public EditorWindow(BitmapSource image)
    {
        InitializeComponent();
        _originalImage = image;
        _saveService = new SaveService();
        DisplayImage();
        SetupToolbar();
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
        if (_currentTool == AnnotationTool.None || e.LeftButton != MouseButtonState.Pressed)
            return;

        _isDrawing = true;
        _startPoint = e.GetPosition(DrawingCanvas);

        UIElement? element = _currentTool switch
        {
            AnnotationTool.Rectangle => CreateRectangle(),
            AnnotationTool.Circle => CreateEllipse(),
            AnnotationTool.Arrow => CreateArrow(),
            AnnotationTool.Line => CreateLine(),
            AnnotationTool.Number => CreateNumberLabel(),
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
        if (!_isDrawing || _currentShape == null)
            return;

        var currentPoint = e.GetPosition(DrawingCanvas);

        switch (_currentTool)
        {
            case AnnotationTool.Rectangle:
                UpdateRectangle(currentPoint);
                break;
            case AnnotationTool.Circle:
                UpdateEllipse(currentPoint);
                break;
            case AnnotationTool.Line:
            case AnnotationTool.Arrow:
                UpdateLine(currentPoint);
                break;
        }
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
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

    private void ToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string toolName)
        {
            _currentTool = Enum.Parse<AnnotationTool>(toolName);
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
