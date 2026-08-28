using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Screendrop.App.Settings;

/// iOS-style sliding toggle switch rendered as a CheckBox so it participates
/// in normal form data-binding and keyboard focus.
internal sealed class SwitchToggle : CheckBox
{
    private const double ThumbTravel = 16;

    public SwitchToggle()
    {
        Template = BuildTemplate();
        Focusable = true;
        VerticalAlignment = VerticalAlignment.Center;
        Loaded += (_, _) => PositionThumb(animate: false);
    }

    private static ControlTemplate BuildTemplate()
    {
        var template = new ControlTemplate(typeof(SwitchToggle));

        var track = new FrameworkElementFactory(typeof(Border));
        track.Name = "Track";
        track.SetValue(Border.WidthProperty, 40.0);
        track.SetValue(Border.HeightProperty, 24.0);
        track.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        track.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(120, 120, 128)));
        track.SetValue(Border.OpacityProperty, 0.32);

        var thumb = new FrameworkElementFactory(typeof(Border));
        thumb.Name = "Thumb";
        thumb.SetValue(Border.WidthProperty, 20.0);
        thumb.SetValue(Border.HeightProperty, 20.0);
        thumb.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        thumb.SetValue(Border.BackgroundProperty, Brushes.White);
        thumb.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        thumb.SetValue(Border.MarginProperty, new Thickness(2, 0, 0, 0));
        thumb.SetValue(Border.EffectProperty, new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black,
            Opacity = 0.25,
            BlurRadius = 3,
            ShadowDepth = 1,
            Direction = 270,
        });
        thumb.SetValue(Border.RenderTransformProperty, new TranslateTransform(0, 0));

        track.AppendChild(thumb);
        template.VisualTree = track;

        // Checked: accent track at full opacity.
        var checkedTrigger = new Trigger { Property = IsCheckedProperty, Value = true };
        checkedTrigger.Setters.Add(new Setter(
            Border.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(49, 130, 237)),
            "Track"));
        checkedTrigger.Setters.Add(new Setter(
            Border.OpacityProperty, 1.0, "Track"));
        template.Triggers.Add(checkedTrigger);

        return template;
    }

    protected override void OnChecked(RoutedEventArgs e)
    {
        base.OnChecked(e);
        PositionThumb(animate: true);
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        base.OnUnchecked(e);
        PositionThumb(animate: true);
    }

    private void PositionThumb(bool animate)
    {
        if (Template.FindName("Thumb", this) is not Border thumb
            || thumb.RenderTransform is not TranslateTransform translate)
        {
            return;
        }

        double toX = IsChecked == true ? ThumbTravel : 0;
        if (!animate)
        {
            translate.X = toX;
            return;
        }

        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(
            toX, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }
}
