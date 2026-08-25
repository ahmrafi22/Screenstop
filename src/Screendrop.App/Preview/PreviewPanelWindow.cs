using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Screendrop.Core.Preview;

namespace Screendrop.App.Preview;

internal enum PreviewAction { Save, Copy, Edit, Discard }

internal sealed class PreviewPanelWindow : Window
{
    internal const double CardWidthDip = 180;
    internal const double CardHeightDip = 138;
    internal const double CardPitchDip = 8;
    internal const double PaddingDip = 12;
    internal const double PanelHeightDip = CardHeightDip + PaddingDip * 2;

    private readonly StackPanel _cardsPanel;

    public event Action<PreviewEntry, PreviewAction>? ActionRequested;

    public PreviewPanelWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Title = "ScreendropPreviewPanel";

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(240, 31, 41, 55)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(PaddingDip),
        };

        _cardsPanel = new StackPanel { Orientation = Orientation.Horizontal };
        border.Child = _cardsPanel;

        Content = border;

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not ButtonBase)
            {
                DragMove();
            }
        };
    }

    public IntPtr WindowHandle => new WindowInteropHelper(this).Handle;

    public void ShowPanel()
    {
        Show();
        Visibility = Visibility.Visible;
        Topmost = true;
    }

    public void HidePanel()
    {
        Visibility = Visibility.Collapsed;
    }

    public void SetCards(IReadOnlyList<(PreviewEntry Entry, BitmapSource? Thumbnail)> cards)
    {
        _cardsPanel.Children.Clear();

        foreach (var (entry, thumbnail) in cards)
        {
            var card = BuildCard(entry, thumbnail);
            _cardsPanel.Children.Add(card);
        }
    }

    private UIElement BuildCard(PreviewEntry entry, BitmapSource? thumbnail)
    {
        var image = new Image
        {
            Source = thumbnail,
            Width = CardWidthDip,
            Height = 96,
            Stretch = Stretch.UniformToFill,
            VerticalAlignment = VerticalAlignment.Top,
            ClipToBounds = true,
        };

        var caption = new TextBlock
        {
            Text = $"{entry.CapturedAt:HH:mm:ss} · {entry.CaptureType}",
            Foreground = Brushes.White,
            FontSize = 10,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed,
        };

        var save = new Button { Content = "Save", FontSize = 9, Padding = new Thickness(3, 1, 3, 1), Tag = entry };
        save.Click += (_, e) => { e.Handled = true; ActionRequested?.Invoke(entry, PreviewAction.Save); };
        var copy = new Button { Content = "Copy", FontSize = 9, Padding = new Thickness(3, 1, 3, 1), Tag = entry, Margin = new Thickness(4, 0, 0, 0) };
        copy.Click += (_, e) => { e.Handled = true; ActionRequested?.Invoke(entry, PreviewAction.Copy); };
        var edit = new Button { Content = "Edit", FontSize = 9, Padding = new Thickness(3, 1, 3, 1), Tag = entry, Margin = new Thickness(4, 0, 0, 0) };
        edit.Click += (_, e) => { e.Handled = true; ActionRequested?.Invoke(entry, PreviewAction.Edit); };
        var discard = new Button { Content = "✕", FontSize = 9, Padding = new Thickness(3, 1, 3, 1), Tag = entry, Margin = new Thickness(4, 0, 0, 0) };
        discard.Click += (_, e) => { e.Handled = true; ActionRequested?.Invoke(entry, PreviewAction.Discard); };

        actions.Children.Add(save);
        actions.Children.Add(copy);
        actions.Children.Add(edit);
        actions.Children.Add(discard);

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(image);
        stack.Children.Add(caption);
        stack.Children.Add(actions);

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 55, 65, 81)),
            CornerRadius = new CornerRadius(8),
            Child = stack,
            Width = CardWidthDip,
            Height = CardHeightDip,
            Margin = new Thickness(0, 0, CardPitchDip, 0),
        };

        card.MouseEnter += (_, _) => actions.Visibility = Visibility.Visible;
        card.MouseLeave += (_, _) => actions.Visibility = Visibility.Collapsed;

        return card;
    }
}