using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Screendrop.App.Controls;

/// <summary>
/// One inspector input row. The label and its track share a pill, the live
/// value sits in a second pill beside it, and keyboard focus lights a 2px accent
/// ring around whichever half owns it.
///
/// This is the shared primitive behind every scrubber row in the editor. It
/// follows the iOS inspector (label+track pill plus an adjacent value pill, with
/// the focused part taking an accent border) and borrows TExP/dialkit's readout
/// treatment: a monospaced, tabular value so digits never shift the layout as
/// they change, on a 30px row with a 6px gap.
/// </summary>
internal sealed class PillSliderRow : UserControl
{
    private readonly Slider _slider;
    private readonly TextBox _valueBox;
    private readonly Border _trackRing;
    private readonly Border _trackPill;
    private readonly Border _valueRing;
    private readonly Border _valuePill;
    private readonly Func<double, string> _format;
    private readonly Action<double, bool> _apply;
    private readonly double _displayScale;
    private bool _dragging;
    private bool _syncing;

    public PillSliderRow(
        string label,
        double value,
        double min,
        double max,
        Func<double, string> format,
        Action<double, bool> apply,
        double displayScale = 1)
    {
        _format = format;
        _apply = apply;
        _displayScale = displayScale;
        Margin = new Thickness(0, 2, 0, 2);

        _trackRing = new Border
        {
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            SnapsToDevicePixels = true,
            Margin = new Thickness(0, 0, 6, 0),
        };
        _trackPill = new Border { Style = (Style)Application.Current.Resources["Sd.ScrubberPill"] };
        _trackRing.Child = _trackPill;

        _valueRing = new Border
        {
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            SnapsToDevicePixels = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _valuePill = new Border { Style = (Style)Application.Current.Resources["Sd.ValuePill"] };
        _valueRing.Child = _valuePill;

        _slider = new Slider
        {
            Style = (Style)Application.Current.Resources["Sd.PillSlider"],
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            SmallChange = (max - min) / 100,
            LargeChange = (max - min) / 10,
            IsMoveToPointEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
        };
        _slider.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, label);

        var name = new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["Sd.Text"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        // Label and track share one pill, so they need their own columns or
        // the track would paint straight through the label.
        var trackGrid = new Grid();
        trackGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        trackGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(name, 0);
        Grid.SetColumn(_slider, 1);
        trackGrid.Children.Add(name);
        trackGrid.Children.Add(_slider);
        _trackPill.Child = trackGrid;

        _valueBox = new TextBox
        {
            Style = (Style)Application.Current.Resources["Sd.ValueBox"],
            Text = format(_slider.Value),
            ToolTip = $"Enter a value between {format(min)} and {format(max)}",
        };
        _valueBox.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, $"{label} value");
        _valuePill.Child = _valueBox;

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_trackRing, 0);
        Grid.SetColumn(_valueRing, 1);
        row.Children.Add(_trackRing);
        row.Children.Add(_valueRing);
        Content = row;

        WireSlider();
        WireValueBox();
    }

    /// <summary>Raised when a drag starts, so the host can open an undo batch.</summary>
    public Action? BeginEdit { get; set; }

    /// <summary>Raised when a drag finishes, so the host can close the batch.</summary>
    public Action? EndEdit { get; set; }

    /// <summary>Pushes an externally-changed value into the row without re-firing <c>Apply</c>.</summary>
    public void SetValueSilently(double value)
    {
        _syncing = true;
        try
        {
            _slider.Value = Math.Clamp(value, _slider.Minimum, _slider.Maximum);
            _valueBox.Text = _format(_slider.Value);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void WireSlider()
    {
        _slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) =>
        {
            _dragging = true;
            BeginEdit?.Invoke();
        }));
        _slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            EndEdit?.Invoke();
        }));
        _slider.ValueChanged += (_, _) =>
        {
            if (_syncing)
            {
                return;
            }

            _valueBox.Text = _format(_slider.Value);
            _apply(_slider.Value, _dragging);
        };
        _slider.GotKeyboardFocus += (_, _) => SetRing(_trackRing, _trackPill, on: true);
        _slider.LostKeyboardFocus += (_, _) => SetRing(_trackRing, _trackPill, on: false);
    }

    private void WireValueBox()
    {
        _valueBox.GotKeyboardFocus += (_, _) =>
        {
            SetRing(_valueRing, _valuePill, on: true);
            _valueBox.SelectAll();
        };
        _valueBox.LostKeyboardFocus += (_, _) =>
        {
            SetRing(_valueRing, _valuePill, on: false);
            CommitTypedValue();
        };
        _valueBox.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter:
                    CommitTypedValue();
                    Keyboard.ClearFocus();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    _valueBox.Text = _format(_slider.Value);
                    Keyboard.ClearFocus();
                    e.Handled = true;
                    break;
                case Key.Up:
                    CommitTypedValue();
                    Nudge(1);
                    e.Handled = true;
                    break;
                case Key.Down:
                    CommitTypedValue();
                    Nudge(-1);
                    e.Handled = true;
                    break;
            }
        };
    }

    private void Nudge(int direction)
    {
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
            ? _slider.LargeChange
            : _slider.SmallChange;
        _slider.Value = Math.Clamp(_slider.Value + (step * direction), _slider.Minimum, _slider.Maximum);
    }

    private void CommitTypedValue()
    {
        if (TryParseValue(_valueBox.Text, out double typed))
        {
            _slider.Value = Math.Clamp(typed / _displayScale, _slider.Minimum, _slider.Maximum);
        }

        _valueBox.Text = _format(_slider.Value);
    }

    private static bool TryParseValue(string text, out double value)
    {
        string normalized = text.Trim().TrimEnd('%').Trim();
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            && !double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        return double.IsFinite(value);
    }

    /// <summary>Lights a 2px accent ring by tinting both the outer ring and the pill stroke.</summary>
    private static void SetRing(Border ring, Border pill, bool on)
    {
        var accent = (Brush)Application.Current.Resources["Sd.Accent"];
        var border = (Brush)Application.Current.Resources["Sd.Border"];
        ring.BorderBrush = on ? accent : Brushes.Transparent;
        pill.BorderBrush = on ? accent : border;
    }
}
