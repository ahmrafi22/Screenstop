using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Screendrop.App.Infrastructure;
using Screendrop.Capture;
using Screendrop.Core.Geometry;

namespace Screendrop.App.WindowPicker;

internal sealed class WindowPickerOverlay : Window
{
    private readonly PickerLayout _layout;
    private readonly IReadOnlyList<WindowInfo> _candidates;

    private readonly Canvas _root;
    private readonly Rectangle _ring;
    private readonly TextBlock _titleLabel;

    private WindowInfo? _hovered;

    public WindowInfo? Picked { get; private set; }

    public WindowPickerOverlay(PickerLayout layout, IReadOnlyList<WindowInfo> candidates)
    {
        _layout = layout;
        _candidates = candidates;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        Focusable = true;
        Cursor = Cursors.Cross;
        Title = "ScreendropWindowPicker";
        Left = layout.Left;
        Top = layout.Top;
        Width = layout.Width;
        Height = layout.Height;

        _root = new Canvas { Width = layout.Width, Height = layout.Height };
        Content = _root;

        var dim = new Rectangle
        {
            Width = layout.Width,
            Height = layout.Height,
            Fill = new SolidColorBrush(Color.FromArgb(45, 0, 0, 0)),
            IsHitTestVisible = false,
        };
        _root.Children.Add(dim);

        _ring = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(47, 128, 237)),
            StrokeThickness = 3,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _root.Children.Add(_ring);

        _titleLabel = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 12,
            Padding = new Thickness(6, 3, 6, 3),
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _root.Children.Add(_titleLabel);

        var hint = new TextBlock
        {
            Text = "Click a window to capture it  ·  Esc to cancel",
            Foreground = Brushes.White,
            FontSize = 14,
            Padding = new Thickness(10, 6, 10, 6),
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(hint, (layout.Width - 300) / 2);
        Canvas.SetTop(hint, 14);
        _root.Children.Add(hint);

        MouseMove += OnMouseMove;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public WindowInfo? RunModal()
    {
        ShowDialog();
        return Picked;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var dip = e.GetPosition(this);
        var point = _layout.ToPhysical(_layout.Left + dip.X, _layout.Top + dip.Y);

        WindowInfo? hit = null;
        long bestArea = long.MaxValue;
        foreach (var candidate in _candidates)
        {
            if (!candidate.Bounds.Contains(point.X, point.Y))
            {
                continue;
            }

            long area = (long)candidate.Bounds.Width * candidate.Bounds.Height;
            if (area < bestArea)
            {
                bestArea = area;
                hit = candidate;
            }
        }

        if (!Equals(hit, _hovered))
        {
            _hovered = hit;
            UpdateHighlight();
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_hovered is not null)
        {
            Picked = _hovered;
            Close();
        }

        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void UpdateHighlight()
    {
        if (_hovered is null)
        {
            _ring.Visibility = Visibility.Collapsed;
            _titleLabel.Visibility = Visibility.Collapsed;
            return;
        }

        var (x, y, width, height) = _layout.ToDip(_hovered.Bounds);

        _ring.Width = width;
        _ring.Height = height;
        Canvas.SetLeft(_ring, x);
        Canvas.SetTop(_ring, y);
        _ring.Visibility = Visibility.Visible;

        _titleLabel.Text = _hovered.Title;
        double labelY = y - 24;
        if (labelY < 2)
        {
            labelY = y + height + 4;
        }

        Canvas.SetLeft(_titleLabel, x);
        Canvas.SetTop(_titleLabel, labelY);
        _titleLabel.Visibility = Visibility.Visible;
    }
}
