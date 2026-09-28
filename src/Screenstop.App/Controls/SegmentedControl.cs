using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Screenstop.App.Controls;

/// <summary>
/// A segmented picker: every option shares one neutral track and the selected
/// option lifts out of it as a raised white tile. Used wherever the editor
/// offers a short, mutually exclusive set of choices (Pixelate/Blur,
/// Color/Gradient/Image, Radial/Directional).
/// </summary>
internal sealed class SegmentedControl : Border
{
    private readonly UniformGrid _grid;
    private readonly List<(ToggleButton Button, object? Tag)> _options = new();

    public SegmentedControl(params (string Label, object? Tag)[] options)
        : this((IEnumerable<(string, object?)>)options)
    {
    }

    public SegmentedControl(IEnumerable<(string Label, object? Tag)> options)
    {
        Style = (Style)Application.Current.Resources["Sd.SegmentedTrack"];
        var list = options.ToList();

        _grid = new UniformGrid { Columns = Math.Max(1, list.Count) };
        foreach (var (label, tag) in list)
        {
            var button = new ToggleButton
            {
                Style = (Style)Application.Current.Resources["Sd.Segment"],
                Content = label,
                Tag = tag,
                Margin = new Thickness(1, 0, 1, 0),
            };
            button.Click += (_, _) =>
            {
                if (button.IsChecked == true)
                {
                    SelectionChanged?.Invoke(tag);
                }
            };
            _options.Add((button, tag));
            _grid.Children.Add(button);
        }

        Child = _grid;
    }

    /// <summary>Raised after the user picks an option.</summary>
    public event Action<object?>? SelectionChanged;

    /// <summary>Tag of the currently selected option, or null when none is set.</summary>
    public object? SelectedTag { get; private set; }

    /// <summary>Moves the selection without raising <see cref="SelectionChanged"/>.</summary>
    public void Select(object? tag, bool raise = true)
    {
        SelectedTag = tag;
        foreach (var (button, optionTag) in _options)
        {
            bool selected = Equals(optionTag, tag);
            button.IsChecked = selected;
            if (selected && raise)
            {
                SelectionChanged?.Invoke(tag);
            }
        }
    }
}
