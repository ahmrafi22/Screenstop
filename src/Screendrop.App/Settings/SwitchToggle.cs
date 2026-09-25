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
        // No RenderTransform here: anything declared in a template is frozen
        // once applied, and a frozen transform can neither be animated nor
        // assigned. PositionThumb creates a live one instead.

        track.AppendChild(thumb);
        template.VisualTree = track;

        // Checked: accent track at full opacity, taken from the shared token so
        // the switch matches every other live control.
        var checkedTrigger = new Trigger { Property = IsCheckedProperty, Value = true };
        checkedTrigger.Setters.Add(new Setter(
            Border.BackgroundProperty,
            (Brush)Application.Current.Resources["Sd.Accent"],
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
        if (Template.FindName("Thumb", this) is not Border thumb)
        {
            return;
        }

        // The template cannot supply a usable transform, so make one the first
        // time the thumb is positioned.
        if (thumb.RenderTransform is not TranslateTransform translate)
        {
            translate = new TranslateTransform(0, 0);
            thumb.RenderTransform = translate;
        }

        double toX = IsChecked == true ? ThumbTravel : 0;

        // An animation holds its target property in a read-only state until it
        // is removed, so clear any in-flight animation before assigning.
        translate.BeginAnimation(TranslateTransform.XProperty, null);

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
