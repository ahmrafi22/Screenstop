using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Screenstop.Capture;
using Screenstop.Core.Geometry;

namespace Screenstop.App.AreaSelect;

internal sealed class AreaSelectionOverlay : Window
{
    private const int MinSelectionPixels = 2;

    private readonly int _originX;
    private readonly int _originY;
    private readonly double _scaleX;
    private readonly double _scaleY;

    private readonly Canvas _root;
    private readonly Rectangle _dim;
    private readonly Rectangle _selFill;
    private readonly Rectangle _selBorder;
    private readonly TextBlock _hint;
    private readonly TextBlock _hud;

    private Point _anchorDip;
    private Point _currentDip;
    private bool _dragging;

    public bool Confirmed { get; private set; }

    public PixelRect Selected { get; private set; }

    public AreaSelectionOverlay(MonitorInfo monitor, double scaleX, double scaleY)
    {
        _originX = monitor.PhysicalBounds.X;
        _originY = monitor.PhysicalBounds.Y;
        _scaleX = scaleX;
        _scaleY = scaleY;

        var bounds = monitor.PhysicalBounds;
        double widthDip = bounds.Width / scaleX;
        double heightDip = bounds.Height / scaleY;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        Focusable = true;
        Cursor = Cursors.Cross;
        Title = "ScreenstopAreaSelect";
        Left = bounds.X / scaleX;
        Top = bounds.Y / scaleY;
        Width = widthDip;
        Height = heightDip;

        _root = new Canvas { Width = widthDip, Height = heightDip };
        Content = _root;

        _dim = new Rectangle
        {
            Width = widthDip,
            Height = heightDip,
            Fill = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)),
            IsHitTestVisible = false,
        };
        _root.Children.Add(_dim);

        _selFill = new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
            StrokeThickness = 0,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _root.Children.Add(_selFill);

        _selBorder = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(47, 128, 237)),
            StrokeThickness = 2,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _root.Children.Add(_selBorder);

        _hint = new TextBlock
        {
            Text = "Drag to select a region  ·  Esc to cancel",
            Foreground = Brushes.White,
            FontSize = 14,
            Padding = new Thickness(10, 6, 10, 6),
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
        };
        Canvas.SetLeft(_hint, (widthDip - 260) / 2);
        Canvas.SetTop(_hint, (heightDip - 30) / 2);
        _root.Children.Add(_hint);

        _hud = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 13,
            Padding = new Thickness(6, 3, 6, 3),
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _root.Children.Add(_hud);

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public PixelRect? RunModal()
    {
        ShowDialog();
        return Confirmed ? Selected : null;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _anchorDip = e.GetPosition(this);
        _currentDip = _anchorDip;
        _dragging = true;
        CaptureMouse();
        _hint.Visibility = Visibility.Collapsed;
        UpdateSelectionVisuals();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _currentDip = e.GetPosition(this);
        UpdateSelectionVisuals();
        e.Handled = true;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();

        var region = CurrentSelection();
        if (region.Width < MinSelectionPixels || region.Height < MinSelectionPixels)
        {
            Close();
            return;
        }

        Selected = region;
        Confirmed = true;
        Close();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Close();
                break;
            case Key.Enter:
                if (_dragging)
                {
                    _dragging = false;
                    ReleaseMouseCapture();
                    var region = CurrentSelection();
                    if (region.Width >= MinSelectionPixels && region.Height >= MinSelectionPixels)
                    {
                        Selected = region;
                        Confirmed = true;
                    }
                }

                e.Handled = true;
                Close();
                break;
        }
    }

    private PixelRect CurrentSelection()
    {
        int ax = (int)Math.Round(_anchorDip.X * _scaleX) + _originX;
        int ay = (int)Math.Round(_anchorDip.Y * _scaleY) + _originY;
        int cx = (int)Math.Round(_currentDip.X * _scaleX) + _originX;
        int cy = (int)Math.Round(_currentDip.Y * _scaleY) + _originY;
        return PixelRect.FromMinMax(Math.Min(ax, cx), Math.Min(ay, cy), Math.Max(ax, cx), Math.Max(ay, cy));
    }

    private void UpdateSelectionVisuals()
    {
        var region = CurrentSelection();

        double leftDip = (region.X - _originX) / _scaleX;
        double topDip = (region.Y - _originY) / _scaleY;
        double widthDip = region.Width / _scaleX;
        double heightDip = region.Height / _scaleY;

        _selFill.Width = widthDip;
        _selFill.Height = heightDip;
        Canvas.SetLeft(_selFill, leftDip);
        Canvas.SetTop(_selFill, topDip);
        _selFill.Visibility = Visibility.Visible;

        _selBorder.Width = widthDip;
        _selBorder.Height = heightDip;
        Canvas.SetLeft(_selBorder, leftDip);
        Canvas.SetTop(_selBorder, topDip);
        _selBorder.Visibility = Visibility.Visible;

        _hud.Text = $"{region.X}, {region.Y}  ·  {region.Width} × {region.Height} px";
        double hudY = topDip + heightDip + 4;
        if (hudY + 24 > _root.Height)
        {
            hudY = topDip - 24;
        }

        Canvas.SetLeft(_hud, leftDip);
        Canvas.SetTop(_hud, hudY);
        _hud.Visibility = Visibility.Visible;
    }
}
