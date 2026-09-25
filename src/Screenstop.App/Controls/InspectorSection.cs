using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Screenstop.App.Controls;

/// <summary>
/// A titled inspector group whose body can collapse behind a chevron, with an
/// optional action in the header slot. Matches the disclosure sections in the
/// iOS inspector: a 38px header, a hairline divider beneath, and a short
/// collapse transition that is skipped when animation is turned off in Windows.
/// </summary>
internal sealed class InspectorSection : ContentControl
{
    private readonly ContentControl _body = new();
    private readonly RotateTransform _chevron = new();
    private readonly Grid _header = new();
    private FrameworkElement? _accessory;
    private bool _expanded = true;

    /// <summary>The section heading, e.g. "Camera".</summary>
    public string Title { get; }

    public InspectorSection(string title, object content)
    {
        Title = title;

        var root = new StackPanel();

        _header.Height = 38;
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleButton = new Button
        {
            Content = new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["Sd.SectionText"],
                HorizontalAlignment = HorizontalAlignment.Left,
            },
            Style = (Style)Application.Current.Resources["Sd.GhostButton"],
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Cursor = Cursors.Hand,
        };
        titleButton.Click += (_, _) => SetExpanded(!_expanded);

        var chevronButton = new Button
        {
            Content = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M0,0 L4,4.5 L8,0"),
                Stroke = (Brush)Application.Current.Resources["Sd.TextMuted"],
                StrokeThickness = 1.4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = 8,
                Height = 5,
                SnapsToDevicePixels = true,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = _chevron,
            },
            Style = (Style)Application.Current.Resources["Sd.GhostButton"],
            Padding = new Thickness(7),
            Cursor = Cursors.Hand,
        };
        chevronButton.Click += (_, _) => SetExpanded(!_expanded);

        Grid.SetColumn(titleButton, 0);
        Grid.SetColumn(chevronButton, 2);
        _header.Children.Add(titleButton);
        _header.Children.Add(chevronButton);

        _body.Content = content;

        root.Children.Add(_header);
        root.Children.Add(_body);
        root.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)Application.Current.Resources["Sd.BorderSoft"],
            Margin = new Thickness(0, 2, 0, 0),
        });

        Content = root;
    }

    /// <summary>
    /// Optional control pinned to the right of the header, between the title and
    /// the chevron.
    /// </summary>
    public FrameworkElement? Accessory
    {
        get => _accessory;
        set
        {
            if (_accessory is not null)
            {
                _header.Children.Remove(_accessory);
                Grid.SetColumn(_accessory, 0);
            }

            _accessory = value;
            if (value is null)
            {
                return;
            }

            value.Margin = new Thickness(0, 0, 2, 0);
            value.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(value, 1);
            _header.Children.Add(value);
        }
    }

    /// <summary>Raised after the body is expanded or collapsed.</summary>
    public event EventHandler? ExpandedChanged;

    /// <summary>Whether the section body is currently visible.</summary>
    public bool IsExpanded
    {
        get => _expanded;
        set => SetExpanded(value);
    }

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded)
        {
            return;
        }

        _expanded = expanded;
        var duration = SystemParameters.ClientAreaAnimation
            ? TimeSpan.FromSeconds(0.18)
            : TimeSpan.Zero;

        if (expanded)
        {
            _body.Visibility = Visibility.Visible;
            _body.BeginAnimation(OpacityProperty, new DoubleAnimation(_body.Opacity, 1, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }
        else
        {
            var fade = new DoubleAnimation(_body.Opacity, 0, duration);
            fade.Completed += (_, _) => _body.Visibility = Visibility.Collapsed;
            _body.BeginAnimation(OpacityProperty, fade);
        }

        _chevron.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(expanded ? -90 : 0, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });

        ExpandedChanged?.Invoke(this, EventArgs.Empty);
    }
}
