using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MoneyShot.Models;
using MoneyShot.Platform.Windows;
using MoneyShot.UI.Editor;
using MoneyShot.UI.Interop;
using MoneyShot.UI.Services;
using Logger = MoneyShot.Services.Logger;

namespace MoneyShot.UI.Views;

/// <summary>
/// Avalonia port of MoneyShot/Views/EditorWindow.xaml.cs — see LINUX_PORT.md Phase 1. This is the
/// single highest-risk file in the port: the largest, most stateful piece of mouse-interaction
/// code in the app (see CLAUDE.md's "Resize/drag — design notes", which documents multiple past
/// regressions in the WPF version's equivalent logic). The structure and every rule from that
/// section is preserved exactly; only the framework APIs differ (Avalonia's unified Pointer
/// events instead of separate Mouse events, Geometry.FillContains/StrokeContains instead of
/// WPF's, async StorageProvider/dialogs instead of blocking SaveFileDialog/MessageBox). This has
/// been verified to compile and launch, NOT exhaustively verified interactively across every
/// tool/resize/undo/crop/zoom combination — see the verification notes wherever this is reported
/// back to the user.
/// </summary>
public partial class EditorWindow : Window
{
    private Bitmap _originalImage;
    private AnnotationTool _currentTool = AnnotationTool.None;
    private Color _currentColor = Colors.Red;
    private Color _currentTextBackgroundColor = Colors.Transparent;
    private int _lineThickness = 3;
    private Point _startPoint;
    private Shape? _currentShape;
    private bool _isDrawing;
    private readonly SaveService _saveService;
    private readonly UndoController _undo = new();
    private int _numberCounter = 1;

    private Control? _selectedElement;
    private Point _dragStartPoint;
    private bool _isDragging;
    private Border? _selectionBorder;
    private double _zoomLevel = 1.0;
    private const double ZoomIncrement = 0.25;
    private const double MinZoom = 0.25;
    private const double MaxZoom = 4.0;

    private const string PixelateTag = "pixelate";
    private const string NumberLabelTag = "numberLabel";

    private bool _isResizing;
    private ElementResizeMode _resizeMode = ElementResizeMode.None;
    private Point _resizeStartPoint;
    private double _originalWidth;
    private double _originalHeight;
    private double _originalLeft;
    private double _originalTop;
    private double _originalTextFontSize;
    private ElementState? _resizeStartState;
    private readonly List<Rectangle> _resizeHandles = new();
    private bool _isEndpointResizing;
    private bool _isResizingEndpointStart;
    private Point _originalEndpointStart;
    private Point _originalEndpointEnd;

    private const int FreehandMinDistance = 2;
    private const double ShapeUpdateMinDistancePixels = 1.5;
    private const double MinResizeDimension = 10;
    private const double MinTextScaleFactor = 0.5;
    private const double MinTextFontSize = 8;
    private const double HandleVisualSize = 12;
    private const double HandleHitZoneSize = 24;

    private Rectangle? _cropRectangle;
    private bool _isCropping;

    private Polyline? _currentPolyline;
    private Point _lastDrawPoint;

    private static readonly IPen HitTestPen = new Pen(Brushes.Black, 10);
    private static readonly SolidColorBrush SelectionBrush = new(Color.FromRgb(0xE8, 0xA8, 0x5C));

    private bool _isPanning;
    private Point _panStartPoint;
    private double _panStartTranslateX;
    private double _panStartTranslateY;
    private Cursor? _savedCursorBeforePan;

    private readonly ScaleTransform ZoomTransform = new() { ScaleX = 1, ScaleY = 1 };
    private readonly TranslateTransform PanTransform = new();

    public EditorWindow(Bitmap image)
    {
        InitializeComponent();
        ImageCanvas.RenderTransform = new TransformGroup { Children = { ZoomTransform, PanTransform } };
        _originalImage = image;
        _saveService = new SaveService(new Win32Clipboard());
        DisplayImage();

        KeyDown += EditorWindow_KeyDown;

        AddHandler(PointerWheelChangedEvent, EditorWindow_PointerWheelChanged, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, EditorWindow_PreviewPointerPressed_Pan, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, EditorWindow_PreviewPointerMoved_Pan, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, EditorWindow_PreviewPointerReleased_Pan, RoutingStrategies.Tunnel);

        Closed += EditorWindow_Closed;
    }

    private void EditorWindow_Closed(object? sender, EventArgs e)
    {
        try
        {
            KeyDown -= EditorWindow_KeyDown;

            DrawingCanvas.Children.Clear();
            _resizeHandles.Clear();
            _selectionBorder = null;
            _selectedElement = null;
            _currentShape = null;
            _currentPolyline = null;
            _cropRectangle = null;
            _undo.Clear();

            if (ImageDisplay != null)
            {
                ImageDisplay.Source = null;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Editor teardown encountered a non-fatal error", ex);
        }
    }

    private void EditorWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (ShortcutsOverlay != null && ShortcutsOverlay.IsVisible)
        {
            ShortcutsOverlay.IsVisible = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.OemQuestion && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            ToggleShortcutsOverlay();
            e.Handled = true;
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            switch (e.Key)
            {
                case Key.R:
                    SelectTool(AnnotationTool.Rectangle);
                    e.Handled = true;
                    break;
                case Key.C:
                    SelectTool(AnnotationTool.Circle);
                    e.Handled = true;
                    break;
                case Key.A:
                    SelectTool(AnnotationTool.Arrow);
                    e.Handled = true;
                    break;
                case Key.L:
                    SelectTool(AnnotationTool.Line);
                    e.Handled = true;
                    break;
                case Key.F:
                    SelectTool(AnnotationTool.Freehand);
                    e.Handled = true;
                    break;
                case Key.T:
                    SelectTool(AnnotationTool.Text);
                    e.Handled = true;
                    break;
                case Key.P:
                    SelectTool(AnnotationTool.Blur);
                    e.Handled = true;
                    break;
                case Key.D1:
                case Key.NumPad1:
                    SelectTool(AnnotationTool.Number);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;
                case Key.Delete:
                    if (_selectedElement != null)
                    {
                        DeleteSelectedElement();
                        e.Handled = true;
                    }
                    break;
            }
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.Z:
                    Undo_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.C:
                    SaveToClipboard_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.S:
                    Save_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.OemPlus:
                case Key.Add:
                    ZoomIn_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.OemMinus:
                case Key.Subtract:
                    ZoomOut_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.D0:
                case Key.NumPad0:
                    ZoomReset_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
            }
        }
    }

    private void EditorWindow_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control)
        {
            if (e.Delta.Y > 0)
            {
                if (_zoomLevel < MaxZoom) { _zoomLevel += ZoomIncrement; ApplyZoom(); }
            }
            else
            {
                if (_zoomLevel > MinZoom) { _zoomLevel -= ZoomIncrement; ApplyZoom(); }
            }
            e.Handled = true;
        }
    }

    private void EditorWindow_PreviewPointerPressed_Pan(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.MiddleButtonPressed) return;

        _isPanning = true;
        _panStartPoint = e.GetPosition(this);
        _panStartTranslateX = PanTransform.X;
        _panStartTranslateY = PanTransform.Y;
        _savedCursorBeforePan = Cursor;
        Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void EditorWindow_PreviewPointerMoved_Pan(object? sender, PointerEventArgs e)
    {
        if (!_isPanning) return;

        var current = e.GetPosition(this);
        PanTransform.X = _panStartTranslateX + (current.X - _panStartPoint.X);
        PanTransform.Y = _panStartTranslateY + (current.Y - _panStartPoint.Y);
        e.Handled = true;
    }

    private void EditorWindow_PreviewPointerReleased_Pan(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPanning) return;

        _isPanning = false;
        e.Pointer.Capture(null);
        Cursor = _savedCursorBeforePan;
        _savedCursorBeforePan = null;
        e.Handled = true;
    }

    private void DeleteSelectedElement()
    {
        if (_selectedElement != null)
        {
            var element = _selectedElement;
            var index = DrawingCanvas.Children.IndexOf(element);
            _undo.Push(new UndoController.RemoveElementUndoAction(element, index));
            DrawingCanvas.Children.Remove(element);
            ClearSelection();
            SyncNumberCounterAfterRemoval(element);
        }
    }

    private void SyncNumberCounterAfterRemoval(Control element)
    {
        if (element is TextBlock { Tag: NumberLabelTag })
        {
            _numberCounter = Math.Min(_numberCounter, HighestNumberLabelOnCanvas() + 1);
        }
    }

    private void SyncNumberCounterAfterRestore(Control element)
    {
        if (element is TextBlock { Tag: NumberLabelTag })
        {
            _numberCounter = Math.Max(_numberCounter, HighestNumberLabelOnCanvas() + 1);
        }
    }

    private int HighestNumberLabelOnCanvas()
    {
        var max = 0;
        foreach (var child in DrawingCanvas.Children)
        {
            if (child is TextBlock { Tag: NumberLabelTag } label &&
                int.TryParse(label.Text, out var value) && value > max)
            {
                max = value;
            }
        }
        return max;
    }

    internal void UndoAddElement(Control element)
    {
        DrawingCanvas.Children.Remove(element);
        if (_selectedElement == element)
        {
            ClearSelection();
        }
        SyncNumberCounterAfterRemoval(element);
    }

    internal void UndoRemoveElement(Control element, int index)
    {
        if (DrawingCanvas.Children.Contains(element)) return;
        var targetIndex = Math.Max(0, Math.Min(index, DrawingCanvas.Children.Count));
        DrawingCanvas.Children.Insert(targetIndex, element);
        SyncNumberCounterAfterRestore(element);
    }

    internal void UndoCrop(Bitmap previousImage, IReadOnlyList<Control> previousElements, int previousNumberCounter)
    {
        _originalImage = previousImage;
        DisplayImage();
        DrawingCanvas.Children.Clear();
        foreach (var element in previousElements)
        {
            DrawingCanvas.Children.Add(element);
        }
        _cropRectangle = null;
        _isCropping = false;
        _numberCounter = previousNumberCounter;
        SelectTool(AnnotationTool.Cursor);
        ClearSelection();
    }

    internal void UndoResize(Control element, ElementState previousState)
    {
        ApplyElementState(element, previousState);
    }

    private void DisplayImage()
    {
        ImageDisplay.Source = _originalImage;
        ImageDisplay.Width = _originalImage.PixelSize.Width;
        ImageDisplay.Height = _originalImage.PixelSize.Height;

        DrawingCanvas.Width = _originalImage.PixelSize.Width;
        DrawingCanvas.Height = _originalImage.PixelSize.Height;
    }

    private Point ClampToCanvasBounds(Point point)
    {
        var clampedX = Math.Max(0, Math.Min(point.X, DrawingCanvas.Width));
        var clampedY = Math.Max(0, Math.Min(point.Y, DrawingCanvas.Height));
        return new Point(clampedX, clampedY);
    }

    private static bool AreElementStatesEqual(ElementState first, ElementState second)
    {
        const double epsilon = 0.01;
        var fontSizeEqual = (!first.FontSize.HasValue && !second.FontSize.HasValue) ||
                            (first.FontSize.HasValue && second.FontSize.HasValue &&
                             Math.Abs(first.FontSize.Value - second.FontSize.Value) < epsilon);

        return Math.Abs(first.Left - second.Left) < epsilon &&
               Math.Abs(first.Top - second.Top) < epsilon &&
               Math.Abs(first.Width - second.Width) < epsilon &&
               Math.Abs(first.Height - second.Height) < epsilon &&
               fontSizeEqual;
    }

    private static ElementState? CaptureElementState(Control element)
    {
        if (element is Shape shape && element is not Line && element is not Avalonia.Controls.Shapes.Path)
        {
            return new ElementState(CanvasPosition.GetLeft(shape), CanvasPosition.GetTop(shape), shape.Width, shape.Height, null);
        }

        if (element is TextBlock textBlock)
        {
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var width = textBlock.Bounds.Width > 0 ? textBlock.Bounds.Width : textBlock.DesiredSize.Width;
            var height = textBlock.Bounds.Height > 0 ? textBlock.Bounds.Height : textBlock.DesiredSize.Height;
            return new ElementState(CanvasPosition.GetLeft(textBlock), CanvasPosition.GetTop(textBlock), width, height, textBlock.FontSize);
        }

        return null;
    }

    private void ApplyElementState(Control element, ElementState state)
    {
        if (element is Shape shape && element is not Line)
        {
            shape.Width = state.Width;
            shape.Height = state.Height;
            Canvas.SetLeft(shape, state.Left);
            Canvas.SetTop(shape, state.Top);
        }
        else if (element is TextBlock textBlock)
        {
            if (state.FontSize.HasValue)
            {
                textBlock.FontSize = state.FontSize.Value;
            }
            Canvas.SetLeft(textBlock, state.Left);
            Canvas.SetTop(textBlock, state.Top);
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }
        else
        {
            return;
        }

        if (_selectedElement == element && _selectionBorder != null)
        {
            Canvas.SetLeft(_selectionBorder, state.Left - 2);
            Canvas.SetTop(_selectionBorder, state.Top - 2);
            _selectionBorder.Width = state.Width + 4;
            _selectionBorder.Height = state.Height + 4;
            UpdateResizeHandles(state.Left, state.Top, state.Width, state.Height);
        }
    }

    private void Canvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(DrawingCanvas).Properties.IsLeftButtonPressed)
            return;

        var rawPoint = e.GetPosition(DrawingCanvas);
        var clickPoint = _currentTool == AnnotationTool.Cursor
            ? rawPoint
            : ClampToCanvasBounds(rawPoint);

        if (_currentTool == AnnotationTool.Cursor)
        {
            var hitElement = FindElementAtPoint(rawPoint);

            if (hitElement != null)
            {
                SelectElement(hitElement);
                _isDragging = true;
                _dragStartPoint = rawPoint;
                e.Pointer.Capture(DrawingCanvas);
            }
            else
            {
                ClearSelection();
            }
            return;
        }

        if (_currentTool == AnnotationTool.Crop)
        {
            _isCropping = true;
            _startPoint = clickPoint;

            if (_cropRectangle != null)
            {
                DrawingCanvas.Children.Remove(_cropRectangle);
            }

            _cropRectangle = new Rectangle
            {
                Stroke = new SolidColorBrush(Colors.Yellow),
                StrokeThickness = 2,
                StrokeDashArray = new AvaloniaList<double>(new[] { 4.0, 2.0 }),
                Fill = new SolidColorBrush(Color.FromArgb(50, 255, 255, 0))
            };

            DrawingCanvas.Children.Add(_cropRectangle);
            e.Pointer.Capture(DrawingCanvas);
            return;
        }

        if (_currentTool == AnnotationTool.None)
            return;

        if (_currentTool == AnnotationTool.Text)
        {
            _ = HandleTextToolAsync();
            return;
        }

        _isDrawing = true;
        _startPoint = clickPoint;
        _lastDrawPoint = clickPoint;
        e.Pointer.Capture(DrawingCanvas);

        Control? element = _currentTool switch
        {
            AnnotationTool.Rectangle => CreateRectangle(),
            AnnotationTool.Circle => CreateEllipse(),
            AnnotationTool.Arrow => CreateArrow(),
            AnnotationTool.Line => CreateLine(),
            AnnotationTool.Freehand => CreatePolyline(),
            AnnotationTool.Number => CreateNumberLabel(),
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
            else
            {
                _undo.Push(new UndoController.AddElementUndoAction(element));
            }
        }
    }

    private async Task HandleTextToolAsync()
    {
        var textBlock = await CreateTextLabelAsync();
        if (textBlock != null)
        {
            DrawingCanvas.Children.Add(textBlock);
            _undo.Push(new UndoController.AddElementUndoAction(textBlock));
        }
    }

    private void Canvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        var rawPoint = e.GetPosition(DrawingCanvas);
        var currentPoint = ClampToCanvasBounds(rawPoint);

        if (_currentTool == AnnotationTool.Cursor && _isResizing && _selectedElement != null)
        {
            ResizeElement(_selectedElement, rawPoint);
            return;
        }

        if (_currentTool == AnnotationTool.Cursor && _isEndpointResizing && _selectedElement != null)
        {
            ApplyEndpointResize(_selectedElement, rawPoint);
            return;
        }

        if (_currentTool == AnnotationTool.Cursor && _isDragging && _selectedElement != null)
        {
            var deltaX = rawPoint.X - _dragStartPoint.X;
            var deltaY = rawPoint.Y - _dragStartPoint.Y;

            MoveElement(_selectedElement, deltaX, deltaY);
            _dragStartPoint = rawPoint;
            return;
        }

        if (_currentTool == AnnotationTool.Crop && _isCropping && _cropRectangle != null)
        {
            var x = Math.Min(_startPoint.X, currentPoint.X);
            var y = Math.Min(_startPoint.Y, currentPoint.Y);
            var width = Math.Abs(_startPoint.X - currentPoint.X);
            var height = Math.Abs(_startPoint.Y - currentPoint.Y);

            Canvas.SetLeft(_cropRectangle, x);
            Canvas.SetTop(_cropRectangle, y);
            _cropRectangle.Width = width;
            _cropRectangle.Height = height;
            return;
        }

        if (!_isDrawing || _currentShape == null)
            return;

        if (_currentTool != AnnotationTool.Freehand)
        {
            var dx = currentPoint.X - _lastDrawPoint.X;
            var dy = currentPoint.Y - _lastDrawPoint.Y;
            if ((dx * dx) + (dy * dy) < ShapeUpdateMinDistancePixels * ShapeUpdateMinDistancePixels)
            {
                return;
            }
        }

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
            case AnnotationTool.Freehand:
                UpdatePolyline(currentPoint);
                break;
        }

        _lastDrawPoint = currentPoint;
    }

    private async void Canvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);

        if (_currentTool == AnnotationTool.Cursor)
        {
            if (_isResizing && _selectedElement != null && _resizeStartState != null)
            {
                var resizeEndState = CaptureElementState(_selectedElement);
                if (resizeEndState != null && !AreElementStatesEqual(_resizeStartState, resizeEndState))
                {
                    _undo.Push(new UndoController.ResizeUndoAction(_selectedElement, _resizeStartState));
                }
            }
            else if (_isEndpointResizing && _selectedElement != null)
            {
                var element = _selectedElement;
                ClearSelection();
                SelectElement(element);
            }

            _isDragging = false;
            _isResizing = false;
            _isEndpointResizing = false;
            _resizeMode = ElementResizeMode.None;
            _resizeStartState = null;
            return;
        }

        if (_currentTool == AnnotationTool.Crop && _isCropping)
        {
            _isCropping = false;
            if (_cropRectangle != null && _cropRectangle.Width > 10 && _cropRectangle.Height > 10)
            {
                var result = await SimpleMessageBox.ShowAsync(this,
                    "Apply crop to image? This will remove all annotations and crop the image.",
                    "Confirm Crop", SimpleMessageBoxButtons.YesNo);

                if (result == SimpleMessageBoxResult.Yes)
                {
                    ApplyCrop();
                }
                else
                {
                    DrawingCanvas.Children.Remove(_cropRectangle);
                    _cropRectangle = null;
                }
            }
            return;
        }

        if (_isDrawing && _currentShape != null)
        {
            if (_currentTool == AnnotationTool.Blur && _currentShape is Rectangle pixelateRect)
            {
                if (pixelateRect.Width > 5 && pixelateRect.Height > 5)
                {
                    pixelateRect.Fill = CanvasRenderer.CreatePixelatedBrush(pixelateRect, _originalImage);
                }
            }
            _undo.Push(new UndoController.AddElementUndoAction(_currentShape));
        }

        if (_isDrawing && _currentPolyline != null)
        {
            _undo.Push(new UndoController.AddElementUndoAction(_currentPolyline));
            _currentPolyline = null;
        }

        _isDrawing = false;
        _currentShape = null;
    }

    private Rectangle CreateRectangle()
    {
        var rect = new Rectangle
        {
            Stroke = new SolidColorBrush(_currentColor),
            StrokeThickness = _lineThickness,
            Fill = Brushes.Transparent,
            Width = 0,
            Height = 0
        };
        Canvas.SetLeft(rect, _startPoint.X);
        Canvas.SetTop(rect, _startPoint.Y);
        return rect;
    }

    private Ellipse CreateEllipse()
    {
        var ellipse = new Ellipse
        {
            Stroke = new SolidColorBrush(_currentColor),
            StrokeThickness = _lineThickness,
            Fill = Brushes.Transparent,
            Width = 0,
            Height = 0
        };
        Canvas.SetLeft(ellipse, _startPoint.X);
        Canvas.SetTop(ellipse, _startPoint.Y);
        return ellipse;
    }

    private Line CreateLine()
    {
        return new Line
        {
            Stroke = new SolidColorBrush(_currentColor),
            StrokeThickness = _lineThickness,
            StartPoint = _startPoint,
            EndPoint = _startPoint
        };
    }

    private Avalonia.Controls.Shapes.Path CreateArrow()
    {
        return new Avalonia.Controls.Shapes.Path
        {
            Stroke = new SolidColorBrush(_currentColor),
            StrokeThickness = _lineThickness,
            Fill = new SolidColorBrush(_currentColor)
        };
    }

    private Polyline CreatePolyline()
    {
        var polyline = new Polyline
        {
            Stroke = new SolidColorBrush(_currentColor),
            StrokeThickness = _lineThickness,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
        };
        polyline.Points.Add(_startPoint);
        _currentPolyline = polyline;
        return polyline;
    }

    private TextBlock CreateNumberLabel()
    {
        var textBlock = new TextBlock
        {
            Text = _numberCounter.ToString(),
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(_currentColor),
            Background = new SolidColorBrush(Colors.White),
            Padding = new Thickness(5),
            Tag = NumberLabelTag
        };
        Canvas.SetLeft(textBlock, _startPoint.X);
        Canvas.SetTop(textBlock, _startPoint.Y);
        _numberCounter++;
        _isDrawing = false;
        return textBlock;
    }

    private async Task<TextBlock?> CreateTextLabelAsync()
    {
        var resources = Avalonia.Application.Current!.Resources;
        var inputDialog = new Window
        {
            Title = "Add text",
            Width = 340,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (IBrush)resources["Cocoa.WindowBrush"]!,
            Foreground = (IBrush)resources["Cocoa.TextBrush"]!
        };

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "Label text",
            Foreground = (IBrush)resources["Cocoa.TextSecondaryBrush"]!,
            Margin = new Thickness(0, 0, 0, 6)
        };
        Grid.SetRow(label, 0);
        grid.Children.Add(label);

        var textBox = new TextBox { Margin = new Thickness(0, 0, 0, 14) };
        Grid.SetRow(textBox, 1);
        grid.Children.Add(textBox);

        var okButton = new Button
        {
            Content = "Add",
            Theme = (Avalonia.Styling.ControlTheme)resources["AccentButton"]!,
            MinWidth = 76,
            IsDefault = true
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Theme = (Avalonia.Styling.ControlTheme)resources["SubtleButton"]!,
            MinWidth = 76,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true
        };

        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(okButton);
        Grid.SetRow(buttonPanel, 2);
        grid.Children.Add(buttonPanel);

        inputDialog.Content = grid;

        okButton.Click += (_, _) => inputDialog.Close(true);
        cancelButton.Click += (_, _) => inputDialog.Close(false);
        inputDialog.Opened += (_, _) => textBox.Focus();

        var confirmed = await inputDialog.ShowDialog<bool>(this);

        if (confirmed && !string.IsNullOrWhiteSpace(textBox.Text))
        {
            var textBlock = new TextBlock
            {
                Text = textBox.Text,
                FontSize = 16,
                FontWeight = FontWeight.Normal,
                Foreground = new SolidColorBrush(_currentColor),
                Background = new SolidColorBrush(_currentTextBackgroundColor),
                Padding = new Thickness(5)
            };

            Canvas.SetLeft(textBlock, _startPoint.X);
            Canvas.SetTop(textBlock, _startPoint.Y);
            _isDrawing = false;
            return textBlock;
        }

        _isDrawing = false;
        return null;
    }

    private Rectangle CreateBlurRectangle()
    {
        var rect = new Rectangle
        {
            Stroke = new SolidColorBrush(Colors.Transparent),
            StrokeThickness = 0,
            Fill = new SolidColorBrush(Color.FromArgb(128, 128, 128, 128)),
            Tag = PixelateTag,
            Width = 0,
            Height = 0
        };
        Canvas.SetLeft(rect, _startPoint.X);
        Canvas.SetTop(rect, _startPoint.Y);
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
            line.EndPoint = currentPoint;
        }
    }

    private void UpdatePolyline(Point currentPoint)
    {
        if (_currentPolyline != null && _currentPolyline.Points.Count > 0)
        {
            var lastPoint = _currentPolyline.Points[_currentPolyline.Points.Count - 1];
            var distance = Math.Sqrt(Math.Pow(currentPoint.X - lastPoint.X, 2) + Math.Pow(currentPoint.Y - lastPoint.Y, 2));

            if (distance > FreehandMinDistance)
            {
                _currentPolyline.Points.Add(currentPoint);
            }
        }
    }

    private void UpdateArrow(Point currentPoint)
    {
        if (_currentShape is not Avalonia.Controls.Shapes.Path arrow) return;

        var dx = currentPoint.X - _startPoint.X;
        var dy = currentPoint.Y - _startPoint.Y;
        var angle = Math.Atan2(dy, dx);
        var length = Math.Sqrt(dx * dx + dy * dy);

        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = _startPoint, IsClosed = false };

        figure.Segments!.Add(new LineSegment { Point = currentPoint });

        var arrowHeadLength = Math.Min(20, length / 3);
        var arrowHeadAngle = Math.PI / 6;

        var leftPoint = new Point(
            currentPoint.X - arrowHeadLength * Math.Cos(angle - arrowHeadAngle),
            currentPoint.Y - arrowHeadLength * Math.Sin(angle - arrowHeadAngle)
        );
        var rightPoint = new Point(
            currentPoint.X - arrowHeadLength * Math.Cos(angle + arrowHeadAngle),
            currentPoint.Y - arrowHeadLength * Math.Sin(angle + arrowHeadAngle)
        );

        figure.Segments.Add(new LineSegment { Point = leftPoint });
        figure.Segments.Add(new LineSegment { Point = currentPoint });
        figure.Segments.Add(new LineSegment { Point = rightPoint });
        figure.Segments.Add(new LineSegment { Point = currentPoint });

        geometry.Figures!.Add(figure);
        arrow.Data = geometry;
    }

    private Control? FindElementAtPoint(Point point)
    {
        for (int i = DrawingCanvas.Children.Count - 1; i >= 0; i--)
        {
            var element = DrawingCanvas.Children[i];

            if (element == _selectionBorder || _resizeHandles.Contains(element))
                continue;

            if (IsPointInElement(element, point))
            {
                return element;
            }
        }
        return null;
    }

    private bool IsPointInElement(Control element, Point point)
    {
        var left = CanvasPosition.GetLeft(element);
        var top = CanvasPosition.GetTop(element);

        if (element is Avalonia.Controls.Shapes.Path path)
        {
            if (path.Data is PathGeometry pathGeometry)
            {
                var adjustedPoint = new Point(point.X - left, point.Y - top);
                return pathGeometry.FillContains(adjustedPoint) ||
                       pathGeometry.StrokeContains(HitTestPen, adjustedPoint);
            }
            return false;
        }
        else if (element is Shape shape && element is not Line)
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
            var width = textBlock.Bounds.Width;
            var height = textBlock.Bounds.Height;

            return point.X >= left && point.X <= left + width &&
                   point.Y >= top && point.Y <= top + height;
        }
        else if (element is Line line)
        {
            var distance = DistanceFromPointToLine(point, line.StartPoint, line.EndPoint);
            return distance < 10;
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

    private void SelectElement(Control element)
    {
        ClearSelection();
        _selectedElement = element;

        _selectionBorder = new Border
        {
            BorderBrush = SelectionBrush,
            BorderThickness = new Thickness(2),
            IsHitTestVisible = false
        };

        var left = CanvasPosition.GetLeft(element);
        var top = CanvasPosition.GetTop(element);
        double width = 0, height = 0;

        if (element is Avalonia.Controls.Shapes.Path path && path.Data is { } pathData)
        {
            var bounds = pathData.Bounds;
            left += bounds.Left;
            top += bounds.Top;
            width = bounds.Width;
            height = bounds.Height;
        }
        else if (element is Shape shape && element is not Line && element is not Avalonia.Controls.Shapes.Path)
        {
            width = shape.Width;
            height = shape.Height;
        }
        else if (element is TextBlock textBlock)
        {
            width = textBlock.Bounds.Width;
            height = textBlock.Bounds.Height;
        }
        else if (element is Line line)
        {
            left = Math.Min(line.StartPoint.X, line.EndPoint.X);
            top = Math.Min(line.StartPoint.Y, line.EndPoint.Y);
            width = Math.Abs(line.EndPoint.X - line.StartPoint.X);
            height = Math.Abs(line.EndPoint.Y - line.StartPoint.Y);
        }

        Canvas.SetLeft(_selectionBorder, left - 2);
        Canvas.SetTop(_selectionBorder, top - 2);
        _selectionBorder.Width = width + 4;
        _selectionBorder.Height = height + 4;

        DrawingCanvas.Children.Add(_selectionBorder);

        if ((element is Shape && element is not Line && element is not Avalonia.Controls.Shapes.Path) || element is TextBlock)
        {
            CreateResizeHandles(left, top, width, height);
        }
        else if (element is Line lineEl)
        {
            CreateEndpointHandles(lineEl.StartPoint, lineEl.EndPoint);
        }
        else if (element is Avalonia.Controls.Shapes.Path arrowEl)
        {
            var (start, end) = TryGetArrowEndpoints(arrowEl);
            if (start.HasValue && end.HasValue)
            {
                CreateEndpointHandles(start.Value, end.Value);
            }
        }
    }

    private void CreateResizeHandles(double left, double top, double width, double height)
    {
        ClearResizeHandlesOnly();

        var handleColor = SelectionBrush;

        _resizeHandles.Add(CreateResizeHandle(left - 2, top - 2, handleColor, ElementResizeMode.TopLeft));
        _resizeHandles.Add(CreateResizeHandle(left + width + 2, top - 2, handleColor, ElementResizeMode.TopRight));
        _resizeHandles.Add(CreateResizeHandle(left - 2, top + height + 2, handleColor, ElementResizeMode.BottomLeft));
        _resizeHandles.Add(CreateResizeHandle(left + width + 2, top + height + 2, handleColor, ElementResizeMode.BottomRight));
        _resizeHandles.Add(CreateResizeHandle(left + width / 2, top - 2, handleColor, ElementResizeMode.Top));
        _resizeHandles.Add(CreateResizeHandle(left + width / 2, top + height + 2, handleColor, ElementResizeMode.Bottom));
        _resizeHandles.Add(CreateResizeHandle(left - 2, top + height / 2, handleColor, ElementResizeMode.Left));
        _resizeHandles.Add(CreateResizeHandle(left + width + 2, top + height / 2, handleColor, ElementResizeMode.Right));
    }

    private void CreateEndpointHandles(Point start, Point end)
    {
        ClearResizeHandlesOnly();
        var handleColor = SelectionBrush;
        var startHandle = CreateEndpointHandle(start, handleColor, isStart: true);
        var endHandle = CreateEndpointHandle(end, handleColor, isStart: false);
        _resizeHandles.Add(startHandle);
        _resizeHandles.Add(endHandle);
    }

    private void ClearResizeHandlesOnly()
    {
        foreach (var handle in _resizeHandles)
        {
            DrawingCanvas.Children.Remove(handle);
        }
        _resizeHandles.Clear();
    }

    private static void ShiftPathGeometry(PathGeometry geometry, double deltaX, double deltaY)
    {
        foreach (var figure in geometry.Figures!)
        {
            figure.StartPoint = new Point(figure.StartPoint.X + deltaX, figure.StartPoint.Y + deltaY);
            foreach (var seg in figure.Segments!)
            {
                if (seg is LineSegment ls)
                {
                    ls.Point = new Point(ls.Point.X + deltaX, ls.Point.Y + deltaY);
                }
            }
        }
    }

    private static (Point? start, Point? end) TryGetArrowEndpoints(Avalonia.Controls.Shapes.Path arrow)
    {
        if (arrow.Data is not PathGeometry geometry || geometry.Figures == null || geometry.Figures.Count == 0) return (null, null);
        var figure = geometry.Figures[0];
        if (figure.Segments == null || figure.Segments.Count == 0 || figure.Segments[0] is not LineSegment line) return (null, null);
        return (figure.StartPoint, line.Point);
    }

    private void UpdateResizeHandles(double left, double top, double width, double height)
    {
        if (_resizeHandles.Count != 8)
        {
            CreateResizeHandles(left, top, width, height);
            return;
        }

        PositionHandle(_resizeHandles[0], left - 2, top - 2);
        PositionHandle(_resizeHandles[1], left + width + 2, top - 2);
        PositionHandle(_resizeHandles[2], left - 2, top + height + 2);
        PositionHandle(_resizeHandles[3], left + width + 2, top + height + 2);
        PositionHandle(_resizeHandles[4], left + width / 2, top - 2);
        PositionHandle(_resizeHandles[5], left + width / 2, top + height + 2);
        PositionHandle(_resizeHandles[6], left - 2, top + height / 2);
        PositionHandle(_resizeHandles[7], left + width + 2, top + height / 2);
    }

    private static void PositionHandle(Rectangle handle, double centerX, double centerY)
    {
        Canvas.SetLeft(handle, centerX - handle.Width / 2);
        Canvas.SetTop(handle, centerY - handle.Height / 2);
    }

    private static StandardCursorType GetResizeCursor(ElementResizeMode resizeMode)
    {
        return resizeMode switch
        {
            ElementResizeMode.TopLeft => StandardCursorType.TopLeftCorner,
            ElementResizeMode.BottomRight => StandardCursorType.BottomRightCorner,
            ElementResizeMode.TopRight => StandardCursorType.TopRightCorner,
            ElementResizeMode.BottomLeft => StandardCursorType.BottomLeftCorner,
            ElementResizeMode.Top => StandardCursorType.TopSide,
            ElementResizeMode.Bottom => StandardCursorType.BottomSide,
            ElementResizeMode.Left => StandardCursorType.LeftSide,
            ElementResizeMode.Right => StandardCursorType.RightSide,
            _ => StandardCursorType.SizeAll
        };
    }

    private Rectangle CreateResizeHandle(double centerX, double centerY, SolidColorBrush color, ElementResizeMode resizeMode)
    {
        var handle = new Rectangle
        {
            Width = HandleHitZoneSize,
            Height = HandleHitZoneSize,
            Fill = Brushes.Transparent,
            Cursor = new Cursor(GetResizeCursor(resizeMode)),
            Tag = resizeMode
        };
        var inset = (HandleHitZoneSize - HandleVisualSize) / 2;
        var drawingGroup = new DrawingGroup();
        drawingGroup.Children.Add(new GeometryDrawing
        {
            Brush = color,
            Pen = new Pen(Brushes.White, 1),
            Geometry = new RectangleGeometry(new Rect(inset, inset, HandleVisualSize, HandleVisualSize))
        });
        handle.Fill = new DrawingBrush { Drawing = drawingGroup, Stretch = Stretch.None };

        handle.PointerPressed += ResizeHandle_PointerPressed;
        PositionHandle(handle, centerX, centerY);
        DrawingCanvas.Children.Add(handle);
        return handle;
    }

    private Rectangle CreateEndpointHandle(Point center, SolidColorBrush color, bool isStart)
    {
        var handle = new Rectangle
        {
            Width = HandleHitZoneSize,
            Height = HandleHitZoneSize,
            Fill = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Cross),
            Tag = isStart ? EndpointTagStart : EndpointTagEnd
        };
        var inset = (HandleHitZoneSize - HandleVisualSize) / 2;
        var drawingGroup = new DrawingGroup();
        drawingGroup.Children.Add(new GeometryDrawing
        {
            Brush = color,
            Pen = new Pen(Brushes.White, 1),
            Geometry = new EllipseGeometry(new Rect(inset, inset, HandleVisualSize, HandleVisualSize))
        });
        handle.Fill = new DrawingBrush { Drawing = drawingGroup, Stretch = Stretch.None };
        handle.PointerPressed += EndpointHandle_PointerPressed;
        PositionHandle(handle, center.X, center.Y);
        DrawingCanvas.Children.Add(handle);
        return handle;
    }

    private const string EndpointTagStart = "endpoint:start";
    private const string EndpointTagEnd = "endpoint:end";

    private void ResizeHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Rectangle handle || handle.Tag is not ElementResizeMode mode)
            return;
        if (_selectedElement == null)
            return;

        BeginResize(_selectedElement, mode, e.GetPosition(DrawingCanvas));
        e.Handled = true;
    }

    private void EndpointHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Rectangle handle || handle.Tag is not string tag)
            return;
        if (_selectedElement == null)
            return;

        var isStart = tag == EndpointTagStart;
        BeginEndpointResize(_selectedElement, isStart);
        e.Handled = true;
        e.Pointer.Capture(DrawingCanvas);
    }

    private void BeginEndpointResize(Control element, bool isStart)
    {
        if (element is Line line)
        {
            _originalEndpointStart = line.StartPoint;
            _originalEndpointEnd = line.EndPoint;
        }
        else if (element is Avalonia.Controls.Shapes.Path arrow)
        {
            var (start, end) = TryGetArrowEndpoints(arrow);
            if (!start.HasValue || !end.HasValue) return;
            _originalEndpointStart = start.Value;
            _originalEndpointEnd = end.Value;
        }
        else
        {
            return;
        }

        _isEndpointResizing = true;
        _isResizingEndpointStart = isStart;
    }

    private void BeginResize(Control element, ElementResizeMode mode, Point startPoint)
    {
        _isResizing = true;
        _resizeMode = mode;
        _resizeStartPoint = startPoint;
        _resizeStartState = CaptureElementState(element);

        if (element is Shape shape && element is not Line && element is not Avalonia.Controls.Shapes.Path)
        {
            var width = shape.Width;
            var height = shape.Height;
            if (double.IsNaN(width) || width <= 0) width = shape.Bounds.Width;
            if (double.IsNaN(height) || height <= 0) height = shape.Bounds.Height;
            if (double.IsNaN(width) || width <= 0) width = MinResizeDimension;
            if (double.IsNaN(height) || height <= 0) height = MinResizeDimension;

            _originalWidth = width;
            _originalHeight = height;
            _originalLeft = Canvas.GetLeft(shape);
            _originalTop = Canvas.GetTop(shape);
            if (double.IsNaN(_originalLeft)) _originalLeft = 0;
            if (double.IsNaN(_originalTop)) _originalTop = 0;
        }
        else if (element is TextBlock textBlock)
        {
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _originalWidth = textBlock.Bounds.Width > 0 ? textBlock.Bounds.Width : textBlock.DesiredSize.Width;
            _originalHeight = textBlock.Bounds.Height > 0 ? textBlock.Bounds.Height : textBlock.DesiredSize.Height;
            if (_originalWidth <= 0) _originalWidth = MinResizeDimension;
            if (_originalHeight <= 0) _originalHeight = MinResizeDimension;
            _originalLeft = Canvas.GetLeft(textBlock);
            _originalTop = Canvas.GetTop(textBlock);
            _originalTextFontSize = textBlock.FontSize;
            if (double.IsNaN(_originalLeft)) _originalLeft = 0;
            if (double.IsNaN(_originalTop)) _originalTop = 0;
        }
        else
        {
            _isResizing = false;
            _resizeStartState = null;
            return;
        }
    }

    private void ResizeElement(Control element, Point currentPoint)
    {
        var deltaX = currentPoint.X - _resizeStartPoint.X;
        var deltaY = currentPoint.Y - _resizeStartPoint.Y;

        var newLeft = _originalLeft;
        var newTop = _originalTop;
        var newWidth = _originalWidth;
        var newHeight = _originalHeight;

        switch (_resizeMode)
        {
            case ElementResizeMode.BottomRight:
                newWidth = Math.Max(MinResizeDimension, _originalWidth + deltaX);
                newHeight = Math.Max(MinResizeDimension, _originalHeight + deltaY);
                break;
            case ElementResizeMode.BottomLeft:
                newWidth = Math.Max(MinResizeDimension, _originalWidth - deltaX);
                newHeight = Math.Max(MinResizeDimension, _originalHeight + deltaY);
                newLeft = _originalLeft + (_originalWidth - newWidth);
                break;
            case ElementResizeMode.TopRight:
                newWidth = Math.Max(MinResizeDimension, _originalWidth + deltaX);
                newHeight = Math.Max(MinResizeDimension, _originalHeight - deltaY);
                newTop = _originalTop + (_originalHeight - newHeight);
                break;
            case ElementResizeMode.TopLeft:
                newWidth = Math.Max(MinResizeDimension, _originalWidth - deltaX);
                newHeight = Math.Max(MinResizeDimension, _originalHeight - deltaY);
                newLeft = _originalLeft + (_originalWidth - newWidth);
                newTop = _originalTop + (_originalHeight - newHeight);
                break;
            case ElementResizeMode.Right:
                newWidth = Math.Max(MinResizeDimension, _originalWidth + deltaX);
                break;
            case ElementResizeMode.Left:
                newWidth = Math.Max(MinResizeDimension, _originalWidth - deltaX);
                newLeft = _originalLeft + (_originalWidth - newWidth);
                break;
            case ElementResizeMode.Bottom:
                newHeight = Math.Max(MinResizeDimension, _originalHeight + deltaY);
                break;
            case ElementResizeMode.Top:
                newHeight = Math.Max(MinResizeDimension, _originalHeight - deltaY);
                newTop = _originalTop + (_originalHeight - newHeight);
                break;
        }

        if (element is Shape shape && element is not Line && element is not Avalonia.Controls.Shapes.Path)
        {
            shape.Width = newWidth;
            shape.Height = newHeight;
            Canvas.SetLeft(shape, newLeft);
            Canvas.SetTop(shape, newTop);
        }
        else if (element is TextBlock textBlock)
        {
            var scale = _resizeMode switch
            {
                ElementResizeMode.Left or ElementResizeMode.Right => _originalWidth > 0 ? newWidth / _originalWidth : 1,
                ElementResizeMode.Top or ElementResizeMode.Bottom => _originalHeight > 0 ? newHeight / _originalHeight : 1,
                _ => Math.Min(
                    _originalWidth > 0 ? newWidth / _originalWidth : 1,
                    _originalHeight > 0 ? newHeight / _originalHeight : 1)
            };
            scale = Math.Max(MinTextScaleFactor, scale);
            textBlock.FontSize = Math.Max(MinTextFontSize, _originalTextFontSize * scale);
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(textBlock, newLeft);
            Canvas.SetTop(textBlock, newTop);
            newWidth = textBlock.DesiredSize.Width;
            newHeight = textBlock.DesiredSize.Height;
        }
        else
        {
            return;
        }

        if (_selectionBorder != null)
        {
            Canvas.SetLeft(_selectionBorder, newLeft - 2);
            Canvas.SetTop(_selectionBorder, newTop - 2);
            _selectionBorder.Width = newWidth + 4;
            _selectionBorder.Height = newHeight + 4;
        }

        UpdateResizeHandles(newLeft, newTop, newWidth, newHeight);
    }

    private void ClearSelection()
    {
        if (_selectionBorder != null)
        {
            DrawingCanvas.Children.Remove(_selectionBorder);
            _selectionBorder = null;
        }

        ClearResizeHandlesOnly();
        _selectedElement = null;
    }

    private void ApplyEndpointResize(Control element, Point newEndpoint)
    {
        var clamped = ClampToCanvasBounds(newEndpoint);
        var start = _isResizingEndpointStart ? clamped : _originalEndpointStart;
        var end = _isResizingEndpointStart ? _originalEndpointEnd : clamped;

        if (element is Line line)
        {
            line.StartPoint = start;
            line.EndPoint = end;
        }
        else if (element is Avalonia.Controls.Shapes.Path arrow)
        {
            var savedShape = _currentShape;
            var savedStart = _startPoint;
            _currentShape = arrow;
            _startPoint = start;
            UpdateArrow(end);
            _currentShape = savedShape;
            _startPoint = savedStart;
        }

        if (_resizeHandles.Count == 2)
        {
            var movingHandle = _isResizingEndpointStart ? _resizeHandles[0] : _resizeHandles[1];
            PositionHandle(movingHandle, clamped.X, clamped.Y);
        }
    }

    private void MoveElement(Control element, double deltaX, double deltaY)
    {
        if (element is Shape shape && element is not Line)
        {
            Canvas.SetLeft(shape, CanvasPosition.GetLeft(shape) + deltaX);
            Canvas.SetTop(shape, CanvasPosition.GetTop(shape) + deltaY);
        }
        else if (element is TextBlock textBlock)
        {
            Canvas.SetLeft(textBlock, CanvasPosition.GetLeft(textBlock) + deltaX);
            Canvas.SetTop(textBlock, CanvasPosition.GetTop(textBlock) + deltaY);
        }
        else if (element is Line line)
        {
            line.StartPoint = new Point(line.StartPoint.X + deltaX, line.StartPoint.Y + deltaY);
            line.EndPoint = new Point(line.EndPoint.X + deltaX, line.EndPoint.Y + deltaY);
        }
        else if (element is Avalonia.Controls.Shapes.Path pathEl && pathEl.Data is PathGeometry geometry)
        {
            ShiftPathGeometry(geometry, deltaX, deltaY);
        }

        if (_selectionBorder != null)
        {
            Canvas.SetLeft(_selectionBorder, CanvasPosition.GetLeft(_selectionBorder) + deltaX);
            Canvas.SetTop(_selectionBorder, CanvasPosition.GetTop(_selectionBorder) + deltaY);
        }

        if ((element is Shape && element is not Line && element is not Avalonia.Controls.Shapes.Path) || element is TextBlock)
        {
            var elementLeft = CanvasPosition.GetLeft(element);
            var elementTop = CanvasPosition.GetTop(element);

            double elementWidth;
            double elementHeight;
            if (element is TextBlock textBlock2)
            {
                elementWidth = textBlock2.Bounds.Width;
                elementHeight = textBlock2.Bounds.Height;
            }
            else if (element is Shape shapeElement)
            {
                elementWidth = shapeElement.Width;
                elementHeight = shapeElement.Height;
            }
            else
            {
                return;
            }

            if (elementWidth > 0 && elementHeight > 0)
            {
                UpdateResizeHandles(elementLeft, elementTop, elementWidth, elementHeight);
            }
        }
        else if (element is Line || element is Avalonia.Controls.Shapes.Path)
        {
            ClearResizeHandlesOnly();
            if (_selectionBorder != null) DrawingCanvas.Children.Remove(_selectionBorder);
            _selectionBorder = null;
            SelectElement(element);
        }
    }

    private void ToolButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string toolName)
        {
            if (Enum.TryParse<AnnotationTool>(toolName, out var tool))
            {
                SelectTool(tool);
            }
        }
    }

    private void SelectTool(AnnotationTool tool)
    {
        _currentTool = tool;
        var resources = Avalonia.Application.Current!.Resources;
        var normalTheme = (Avalonia.Styling.ControlTheme)resources["ToolButton"]!;
        var activeTheme = (Avalonia.Styling.ControlTheme)resources["ToolButtonActive"]!;
        var toolName = tool.ToString();
        foreach (var child in ToolButtonsPanel.Children)
        {
            if (child is Button b && b.Tag is string tag)
            {
                b.Theme = tag == toolName ? activeTheme : normalTheme;
            }
        }
    }

    private void CustomColorButton_Click(object? sender, RoutedEventArgs e)
    {
        // Re-use the WinForms ColorDialog (already referenced for this reason — see
        // MoneyShot.UI.csproj) rather than hand-rolling an HSL picker — the OS picker is what
        // most users expect and supports the full palette. Matches the WPF build's reasoning.
        using var dlg = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = System.Drawing.Color.FromArgb(_currentColor.A, _currentColor.R, _currentColor.G, _currentColor.B)
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        var picked = Color.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
        _currentColor = picked;
        if (CustomColorButton != null)
        {
            CustomColorButton.Background = new SolidColorBrush(picked);
        }
        if (_selectedElement != null)
        {
            ChangeElementColor(_selectedElement, _currentColor);
        }
    }

    private void StrokeThicknessSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        _lineThickness = (int)Math.Round(e.NewValue);
        if (StrokeThicknessLabel != null) StrokeThicknessLabel.Text = _lineThickness.ToString();
        if (_selectedElement is Shape shape && _selectedElement is not Avalonia.Controls.Shapes.Path)
        {
            shape.StrokeThickness = _lineThickness;
        }
    }

    private void ShortcutsHelp_Click(object? sender, RoutedEventArgs e) => ToggleShortcutsOverlay();

    private void ShortcutsOverlay_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ShortcutsOverlay != null) ShortcutsOverlay.IsVisible = false;
    }

    private void ToggleShortcutsOverlay()
    {
        if (ShortcutsOverlay == null) return;
        ShortcutsOverlay.IsVisible = !ShortcutsOverlay.IsVisible;
    }

    private void ColorButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Background is SolidColorBrush brush)
        {
            _currentColor = brush.Color;

            if (_selectedElement != null)
            {
                ChangeElementColor(_selectedElement, _currentColor);
            }
        }
    }

    private void TextBackgroundComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TextBackgroundComboBox.SelectedItem is not ComboBoxItem selectedItem)
        {
            return;
        }

        var colorName = selectedItem.Content?.ToString();
        _currentTextBackgroundColor = colorName switch
        {
            "White" => Colors.White,
            "Black" => Colors.Black,
            "Yellow" => Colors.Yellow,
            "Red" => Colors.Red,
            "Blue" => Colors.Blue,
            "Green" => Colors.Green,
            _ => Colors.Transparent
        };

        if (_selectedElement is TextBlock selectedText)
        {
            selectedText.Background = new SolidColorBrush(_currentTextBackgroundColor);
        }
    }

    private void ChangeElementColor(Control element, Color newColor)
    {
        if (!IsColorChangeableElement(element))
            return;

        var brush = new SolidColorBrush(newColor);

        if (element is Shape shape)
        {
            shape.Stroke = brush;
            if (element is Avalonia.Controls.Shapes.Path path)
            {
                path.Fill = brush;
            }
        }
        else if (element is TextBlock textBlock)
        {
            textBlock.Foreground = brush;
        }
    }

    private bool IsColorChangeableElement(Control element)
    {
        if (element is Rectangle { Tag: PixelateTag })
            return false;

        return element is Shape || element is TextBlock;
    }

    private void Undo_Click(object? sender, RoutedEventArgs e)
    {
        _undo.Undo(this);
    }

    private void ResetNumbering_Click(object? sender, RoutedEventArgs e)
    {
        _numberCounter = 1;
    }

    private async void ApplyCrop()
    {
        if (_cropRectangle == null) return;

        var cropX = Canvas.GetLeft(_cropRectangle);
        var cropY = Canvas.GetTop(_cropRectangle);
        var cropWidth = _cropRectangle.Width;
        var cropHeight = _cropRectangle.Height;

        cropX = Math.Max(0, cropX);
        cropY = Math.Max(0, cropY);
        cropX = Math.Min(cropX, _originalImage.PixelSize.Width - 1);
        cropY = Math.Min(cropY, _originalImage.PixelSize.Height - 1);

        cropWidth = Math.Min(cropWidth, _originalImage.PixelSize.Width - cropX);
        cropHeight = Math.Min(cropHeight, _originalImage.PixelSize.Height - cropY);

        var intCropX = (int)Math.Round(cropX);
        var intCropY = (int)Math.Round(cropY);
        var intCropWidth = (int)Math.Round(cropWidth);
        var intCropHeight = (int)Math.Round(cropHeight);

        if (intCropWidth <= 0 || intCropHeight <= 0)
        {
            await SimpleMessageBox.ShowAsync(this, "Invalid crop dimensions.", "Crop Error");
            DrawingCanvas.Children.Remove(_cropRectangle);
            _cropRectangle = null;
            return;
        }

        try
        {
            var previousImage = _originalImage;
            var previousElements = new List<Control>();
            foreach (Control element in DrawingCanvas.Children)
            {
                if (element != _cropRectangle && element != _selectionBorder && !_resizeHandles.Contains(element))
                {
                    previousElements.Add(element);
                }
            }
            var previousNumberCounter = _numberCounter;

            // Crop via the same raw-pixel path as RegionSelector, rather than Avalonia's
            // CroppedBitmap (which has no Save/pixel-access API of its own — see LINUX_PORT.md
            // Phase 1 risk notes), so the cropped image is a real, independently usable Bitmap.
            var captured = _originalImage.ToCapturedImage();
            var croppedBitmap = CropCapturedImage(captured, intCropX, intCropY, intCropWidth, intCropHeight).ToAvaloniaBitmap();

            _originalImage = croppedBitmap;
            DisplayImage();

            DrawingCanvas.Children.Clear();
            _selectedElement = null;
            _selectionBorder = null;
            _resizeHandles.Clear();
            _cropRectangle = null;
            _numberCounter = 1;

            SelectTool(AnnotationTool.Cursor);
            _undo.Push(new UndoController.CropUndoAction(previousImage, previousElements, previousNumberCounter));
        }
        catch (Exception ex)
        {
            await SimpleMessageBox.ShowAsync(this, $"Failed to apply crop: {ex.Message}", "Crop Error");
            DrawingCanvas.Children.Remove(_cropRectangle);
            _cropRectangle = null;
        }
    }

    private static MoneyShot.Abstractions.CapturedImage CropCapturedImage(MoneyShot.Abstractions.CapturedImage source, int x, int y, int width, int height)
    {
        const int bytesPerPixel = 4;
        var destStride = width * bytesPerPixel;
        var dest = new byte[destStride * height];
        for (var row = 0; row < height; row++)
        {
            var srcOffset = (y + row) * source.Stride + x * bytesPerPixel;
            Buffer.BlockCopy(source.PixelDataBgra32, srcOffset, dest, row * destStride, destStride);
        }
        return new MoneyShot.Abstractions.CapturedImage(width, height, destStride, dest);
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var finalImage = CaptureCanvasAsImage();

            var settings = new MoneyShot.Services.SettingsService().LoadSettings();
            var defaultFormat = settings.DefaultFileFormat.ToUpperInvariant();
            var extension = defaultFormat switch { "JPG" or "JPEG" => "jpg", "BMP" => "bmp", _ => "png" };

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save screenshot",
                SuggestedFileName = _saveService.GenerateFileName(settings.DefaultFileFormat),
                DefaultExtension = extension,
                SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(new Uri(settings.DefaultSavePath)),
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } },
                    new FilePickerFileType("JPEG Image") { Patterns = new[] { "*.jpg", "*.jpeg" } },
                    new FilePickerFileType("Bitmap Image") { Patterns = new[] { "*.bmp" } },
                }
            });

            var path = file?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                var fileExtension = System.IO.Path.GetExtension(path).TrimStart('.');
                var format = GetFileFormat(fileExtension);
                _saveService.SaveToFile(finalImage, path, format);
                await SimpleMessageBox.ShowAsync(this, "Image saved successfully!", "Success");
            }
        }
        catch (Exception ex)
        {
            await SimpleMessageBox.ShowAsync(this, $"Failed to save image: {ex.Message}", "Error");
        }
    }

    private string GetFileFormat(string extension)
    {
        return extension.ToUpperInvariant() switch
        {
            "JPG" or "JPEG" => "JPG",
            "BMP" => "BMP",
            _ => "PNG"
        };
    }

    private async void SaveToClipboard_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var finalImage = CaptureCanvasAsImage();
            _saveService.SaveToClipboard(finalImage);
            await SimpleMessageBox.ShowAsync(this, "Image copied to clipboard!", "Success");
        }
        catch (Exception ex)
        {
            await SimpleMessageBox.ShowAsync(this, $"Failed to copy image to clipboard: {ex.Message}", "Error");
        }
    }

    private void ZoomIn_Click(object? sender, RoutedEventArgs e)
    {
        if (_zoomLevel < MaxZoom)
        {
            _zoomLevel += ZoomIncrement;
            ApplyZoom();
        }
    }

    private void ZoomOut_Click(object? sender, RoutedEventArgs e)
    {
        if (_zoomLevel > MinZoom)
        {
            _zoomLevel -= ZoomIncrement;
            ApplyZoom();
        }
    }

    private void ZoomReset_Click(object? sender, RoutedEventArgs e)
    {
        _zoomLevel = 1.0;
        ApplyZoom();
        PanTransform.X = 0;
        PanTransform.Y = 0;
    }

    private void ApplyZoom()
    {
        ZoomTransform.ScaleX = _zoomLevel;
        ZoomTransform.ScaleY = _zoomLevel;
        if (ZoomLevelLabel != null)
        {
            ZoomLevelLabel.Text = $"{Math.Round(_zoomLevel * 100)}%";
        }
    }

    private Bitmap CaptureCanvasAsImage() =>
        CanvasRenderer.CaptureCanvasAsImage(ImageCanvas, _originalImage, ZoomTransform, PanTransform);

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeRestore_Click(sender, new RoutedEventArgs());
        }
        else if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestore_Click(object? sender, RoutedEventArgs e)
    {
        var resources = Avalonia.Application.Current!.Resources;
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            MaximizeRestoreIcon.Data = (Geometry)resources["Icon.WindowMaximize"]!;
        }
        else
        {
            WindowState = WindowState.Maximized;
            MaximizeRestoreIcon.Data = (Geometry)resources["Icon.WindowRestore"]!;
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
