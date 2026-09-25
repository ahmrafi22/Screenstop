using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Screendrop.Core.Background;
using Screendrop.Core.Preview;

namespace Screendrop.App.Preview;

/// Floating preview stack (mac PreviewWindowView parity): a vertical stack of
/// rounded screenshot cards docked to a bottom screen corner. Cards slide in
/// from the edge; hovering reveals a frosted overlay with corner icon buttons
/// and center action pills, laid out by the user's OverlayCardLayout.
internal sealed class PreviewPanelWindow : Window
{
    internal const double CardWidthDip = 165;
    internal const double CardHeightDip = 124;
    internal const double CardSpacingDip = 15;
    internal const double CardRadius = 16;
    internal const double SlideOffsetDip = CardWidthDip + 76;

    private static readonly Typeface IconTypeface = new(
        new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
        FontStyles.Normal,
        FontWeights.Bold,
        FontStretches.Normal);

    private readonly StackPanel _cardsPanel;
    private readonly Border _peekPill;
    private readonly TextBlock _peekText;
    private readonly Border _errorBanner;
    private readonly TextBlock _errorText;
    private int _previousCardCount;
    private bool _collapsed;

    public event Action<PreviewEntry, CardAction>? ActionRequested;

    public event Action? DraggedOut;

    public PreviewPanelWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Title = "ScreendropPreviewPanel";

        _cardsPanel = new StackPanel { Orientation = Orientation.Vertical };

        _peekText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _peekPill = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(217, 28, 29, 34)),
            CornerRadius = new CornerRadius(17),
            BorderBrush = new SolidColorBrush(Color.FromArgb(31, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 7, 14, 7),
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed,
            Child = _peekText,
        };
        _peekPill.MouseLeftButtonUp += (_, _) => SetCollapsed(false);

        _errorText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _errorBanner = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(240, 176, 32, 32)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 6),
            Visibility = Visibility.Collapsed,
            Opacity = 0,
            IsHitTestVisible = false,
            Child = _errorText,
        };

        var root = new Grid();
        root.Children.Add(_cardsPanel);
        root.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { _peekPill },
        });
        root.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { _errorBanner },
        });
        Content = root;
    }

    public IntPtr WindowHandle => new WindowInteropHelper(this).Handle;

    public bool IsCollapsed => _collapsed;

    public void ShowPanel()
    {
        Show();
        Visibility = Visibility.Visible;
        Topmost = true;
    }

    public void HidePanel()
    {
        Visibility = Visibility.Collapsed;
        _collapsed = false;
        _cardsPanel.Visibility = Visibility.Visible;
        _peekPill.Visibility = Visibility.Collapsed;
    }

    public void SetCollapsed(bool collapsed)
    {
        _collapsed = collapsed;
        _cardsPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        _peekPill.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        CollapsedChanged?.Invoke();
    }

    public event Action? CollapsedChanged;

    public void SetPeekCount(int count)
    {
        _peekText.Text = count == 1 ? "1 screenshot" : $"{count} screenshots";
    }

    /// <summary>
    /// Reports a failed action on the card itself. The app no longer raises
    /// toasts, so without this a failed save would look exactly like a card
    /// that quietly vanished.
    /// </summary>
    public void ShowError(string message)
    {
        if (_errorBanner is null)
        {
            return;
        }

        _errorText.Text = message;
        _errorBanner.Visibility = Visibility.Visible;
        _errorBanner.Opacity = 1;
        _errorBanner.BeginAnimation(OpacityProperty, null);
    }

    public void ClearError()
    {
        _errorBanner?.BeginAnimation(OpacityProperty, null);
        if (_errorBanner is not null)
        {
            _errorBanner.Opacity = 0;
            _errorBanner.Visibility = Visibility.Collapsed;
        }
    }

    public void SetCards(
        IReadOnlyList<(PreviewEntry Entry, BitmapSource? Thumbnail)> cards,
        OverlayCardLayout layout,
        bool dockRight,
        int visibleCapacity)
    {
        _cardsPanel.Children.Clear();

        bool animateEntrance = cards.Count > _previousCardCount;
        _previousCardCount = cards.Count;

        // Oldest at the top, newest nearest the screen corner (mac parity).
        var ordered = cards.Reverse().Take(visibleCapacity).Reverse().ToList();
        foreach (var (entry, thumbnail) in ordered)
        {
            var card = BuildCard(entry, thumbnail, layout, dockRight);
            _cardsPanel.Children.Add(card);
        }

        if (animateEntrance && !_collapsed)
        {
            AnimateEntrance(dockRight);
        }
    }

    private void AnimateEntrance(bool dockRight)
    {
        double from = dockRight ? SlideOffsetDip : -SlideOffsetDip;
        foreach (UIElement child in _cardsPanel.Children)
        {
            var translate = new TranslateTransform(from, 0);
            child.RenderTransform = translate;
            child.Opacity = 0;

            var slide = new DoubleAnimation(0, TimeSpan.FromSeconds(0.3))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            var fade = new DoubleAnimation(1, TimeSpan.FromSeconds(0.22));
            translate.BeginAnimation(TranslateTransform.XProperty, slide);
            child.BeginAnimation(OpacityProperty, fade);
        }
    }

    private UIElement BuildCard(
        PreviewEntry entry,
        BitmapSource? thumbnail,
        OverlayCardLayout layout,
        bool dockRight)
    {
        // The card is a fixed 4:3 while captures are usually 16:9, so filling
        // would crop the capture's edges off. Fit the whole thing instead and
        // let the backdrop carry the letterbox.
        var image = new Image
        {
            Source = thumbnail,
            Stretch = Stretch.Uniform,
        };

        var hoverOverlay = BuildHoverOverlay(entry, thumbnail, layout);
        hoverOverlay.Opacity = 0;
        hoverOverlay.Visibility = Visibility.Collapsed;

        var content = new Grid();
        content.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(72, 0, 0, 0)),
            IsHitTestVisible = false,
        });
        content.Children.Add(image);
        content.Children.Add(hoverOverlay);

        var card = new Border
        {
            CornerRadius = new CornerRadius(CardRadius),
            BorderBrush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            ClipToBounds = false,
            Width = CardWidthDip,
            Height = CardHeightDip,
            Margin = new Thickness(0, 0, 0, CardSpacingDip),
            Cursor = Cursors.Hand,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.32,
                BlurRadius = 22,
                ShadowDepth = 6,
                Direction = 270,
            },
            Child = new Border
            {
                CornerRadius = new CornerRadius(CardRadius - 0.5),
                ClipToBounds = true,
                Child = content,
            },
        };

        card.MouseEnter += (_, _) =>
        {
            hoverOverlay.Visibility = Visibility.Visible;
            hoverOverlay.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromSeconds(0.15)));
        };
        card.MouseLeave += (_, _) =>
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromSeconds(0.18));
            fade.Completed += (_, _) => hoverOverlay.Visibility = Visibility.Collapsed;
            hoverOverlay.BeginAnimation(OpacityProperty, fade);
        };

        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is ButtonBase)
            {
                return;
            }

            BeginCardDrag(entry, dockRight);
        };

        return card;
    }

    /// Frosted hover overlay: a blurred copy of the thumbnail under a dark
    /// scrim stands in for SwiftUI's `.ultraThinMaterial`.
    private UIElement BuildHoverOverlay(
        PreviewEntry entry, BitmapSource? thumbnail, OverlayCardLayout layout)
    {
        var overlay = new Grid { IsHitTestVisible = true };

        if (thumbnail is not null)
        {
            overlay.Children.Add(new Image
            {
                Source = thumbnail,
                Stretch = Stretch.UniformToFill,
                Effect = new BlurEffect { Radius = 14, KernelType = KernelType.Gaussian },
                RenderTransform = new ScaleTransform(1.12, 1.12),
                RenderTransformOrigin = new Point(0.5, 0.5),
                IsHitTestVisible = false,
            });

        }

        overlay.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)),
            IsHitTestVisible = false,
        });

        AddCornerButton(overlay, layout.TopLeading, entry, HorizontalAlignment.Left, VerticalAlignment.Top);
        AddCornerButton(overlay, layout.TopTrailing, entry, HorizontalAlignment.Right, VerticalAlignment.Top);
        AddCornerButton(overlay, layout.BottomLeading, entry, HorizontalAlignment.Left, VerticalAlignment.Bottom);
        AddCornerButton(overlay, layout.BottomTrailing, entry, HorizontalAlignment.Right, VerticalAlignment.Bottom);

        if (layout.Center.Count > 0)
        {
            var pills = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            foreach (var action in layout.Center)
            {
                pills.Children.Add(BuildPill(entry, action));
            }

            overlay.Children.Add(pills);
        }

        return overlay;
    }

    private void AddCornerButton(
        Grid overlay,
        CardAction? action,
        PreviewEntry entry,
        HorizontalAlignment horizontal,
        VerticalAlignment vertical)
    {
        if (action is null)
        {
            return;
        }

        var button = new Button
        {
            Width = 26,
            Height = 26,
            Margin = new Thickness(10),
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            ToolTip = action.Value.Help(),
            Cursor = Cursors.Hand,
            Focusable = false,
            Template = CornerButtonTemplate(),
            Content = new TextBlock
            {
                Text = action.Value.Glyph(),
                FontFamily = IconTypeface.FontFamily,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        button.Click += (_, e) =>
        {
            e.Handled = true;
            ActionRequested?.Invoke(entry, action.Value);
        };
        overlay.Children.Add(button);
    }

    private static ControlTemplate CornerButtonTemplate()
    {
        // Frosted chip rather than a flat white disc: a translucent fill over
        // the screenshot, a hairline edge, and an accent wash on hover so the
        // action reads as live before you commit to the click.
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "PART_Chrome";
        border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(232, 255, 255, 255)));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(38, 0, 0, 0)));
        border.SetValue(Border.EffectProperty, new DropShadowEffect
        {
            Color = Colors.Black,
            Opacity = 0.24,
            BlurRadius = 6,
            ShadowDepth = 2,
            Direction = 270,
        });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;

        var hover = new Trigger
        {
            Property = IsMouseOverProperty,
            Value = true,
        };
        hover.Setters.Add(new Setter(
            Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)), "PART_Chrome"));
        hover.Setters.Add(new Setter(
            Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), "PART_Chrome"));
        template.Triggers.Add(hover);

        template.Triggers.Add(new Trigger
        {
            Property = ButtonBase.IsPressedProperty,
            Value = true,
            Setters = { new Setter(UIElement.OpacityProperty, 0.75) },
        });
        return template;
    }

    private UIElement BuildPill(PreviewEntry entry, CardAction action)
    {
        var pill = new Button
        {
            Margin = new Thickness(0, 3, 0, 3),
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = Cursors.Hand,
            Focusable = false,
            Template = PillTemplate(),
            Content = new TextBlock
            {
                Text = action.Title(),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(24, 24, 26)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        pill.Click += (_, e) =>
        {
            e.Handled = true;
            ActionRequested?.Invoke(entry, action);
        };
        return pill;
    }

    private static ControlTemplate PillTemplate()
    {
        // The primary action row. Frosted fill over the screenshot, a hairline
        // edge, and a full-white lift on hover so the pill reads as the
        // thing you are meant to press.
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "PART_Chrome";
        border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(228, 255, 255, 255)));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(34, 0, 0, 0)));
        border.SetValue(Border.PaddingProperty, new Thickness(15, 7, 15, 7));
        border.SetValue(Border.EffectProperty, new DropShadowEffect
        {
            Color = Colors.Black,
            Opacity = 0.24,
            BlurRadius = 7,
            ShadowDepth = 2,
            Direction = 270,
        });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;

        var hover = new Trigger
        {
            Property = IsMouseOverProperty,
            Value = true,
        };
        hover.Setters.Add(new Setter(
            Border.BackgroundProperty, Brushes.White, "PART_Chrome"));
        hover.Setters.Add(new Setter(
            Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), "PART_Chrome"));
        template.Triggers.Add(hover);

        template.Triggers.Add(new Trigger
        {
            Property = ButtonBase.IsPressedProperty,
            Value = true,
            Setters = { new Setter(UIElement.OpacityProperty, 0.78) },
        });
        return template;
    }

    /// Drags the capture out as a file (mac draggable(item.url) parity). When
    /// the drop lands in another app the presenter dismisses the preview if
    /// previewCloseAfterDragging is on.
    private void BeginCardDrag(PreviewEntry entry, bool dockRight)
    {
        try
        {
            var data = new DataObject();
            var files = new System.Collections.Specialized.StringCollection { entry.ImagePath };
            data.SetFileDropList(files);

            var result = DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);
            if (result != DragDropEffects.None)
            {
                DraggedOut?.Invoke();
            }
        }
        catch (Exception)
        {
        }
    }
}
