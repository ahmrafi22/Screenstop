using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Screenstop.App.Controls;
using Screenstop.Core.Annotations;

namespace Screenstop.App.Editor;

/// <summary>A compact mac-inspired inspector backed only by real editor state.</summary>
internal sealed class EditorSidebar : UserControl
{
    private const string DeleteGlyph = "\uE74D";
    private const string AddGlyph = "\uE710";
    private readonly AnnotationCanvas _canvas;
    private readonly AnnotationPresetStore _presetStore;
    private readonly Dictionary<AnnotationTool, ToggleButton> _toolButtons = new();
    private readonly StackPanel _styleSection = new();
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private readonly ComboBox _presetBox = new();
    private List<AnnotationPreset> _presets = new();
    private bool _updatingUi;

    public event Action<AnnotationTool>? ToolPicked;

    public EditorSidebar(AnnotationCanvas canvas, AnnotationPresetStore presetStore)
    {
        _canvas = canvas;
        _presetStore = presetStore;
        Width = 276;

        // The owning inspector supplies the scroll viewer and the panel chrome,
        // so this contributes content only - a nested ScrollViewer here would
        // fight the outer one and clip the sections.
        var stack = new StackPanel();
        stack.Children.Add(Section("Preset", BuildPresetRow()));
        stack.Children.Add(Section("Tools", BuildToolsSection()));
        stack.Children.Add(_styleSection);
        Content = stack;
        _canvas.ToolChanged += RefreshAllSelections;
        _canvas.Model.Changed += RefreshStyleSection;
        Unloaded += (_, _) =>
        {
            _canvas.ToolChanged -= RefreshAllSelections;
            _canvas.Model.Changed -= RefreshStyleSection;
        };
        LoadPresets();
        RefreshAllSelections();
    }

    public void RefreshAllSelections()
    {
        foreach (var (tool, button) in _toolButtons) button.IsChecked = tool == _canvas.ActiveTool;
        RefreshStyleSection();
    }

    private UIElement BuildPresetRow()
    {
        var root = new StackPanel();
        var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _presetBox.Style = (Style)Application.Current.Resources["Sd.ComboBox"];
        _presetBox.SelectionChanged += OnPresetSelected;
        var remove = IconButton(DeleteGlyph, "Delete selected preset", (_, _) => DeleteSelectedPreset());
        var add = IconButton(AddGlyph, "Save current settings as a preset", (_, _) => AddPresetFromCurrent());
        remove.Margin = new Thickness(5, 0, 1, 0);
        add.Margin = new Thickness(1, 0, 0, 0);
        Grid.SetColumn(_presetBox, 0); Grid.SetColumn(remove, 1); Grid.SetColumn(add, 2);
        row.Children.Add(_presetBox); row.Children.Add(remove); row.Children.Add(add);
        root.Children.Add(row);
        return root;
    }

    private UIElement BuildToolsSection()
    {
        var section = new StackPanel();
        var surface = new Border
        {
            Background = Brush("Sd.Panel"), BorderBrush = Brush("Sd.BorderSoft"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9), Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 4),
        };
        var grid = new UniformGrid { Columns = 6 };
        foreach (var (tool, icon) in new[]
        {
            (AnnotationTool.Select, ToolIcon.SelectCursor()), (AnnotationTool.Rectangle, ToolIcon.Rectangle()),
            (AnnotationTool.FilledRectangle, ToolIcon.FilledRectangle()), (AnnotationTool.Ellipse, ToolIcon.Ellipse()),
            (AnnotationTool.Line, ToolIcon.Line()), (AnnotationTool.Arrow, ToolIcon.Arrow()),
            (AnnotationTool.Freehand, ToolIcon.Freehand()), (AnnotationTool.NumberedCircle, ToolIcon.StepMarker()),
            (AnnotationTool.Text, ToolIcon.Text()), (AnnotationTool.Highlight, ToolIcon.Highlight()),
            (AnnotationTool.Pixelate, ToolIcon.Pixelate()), (AnnotationTool.Blur, ToolIcon.Blur()),
        })
        {
            var button = new ToggleButton
            {
                Style = (Style)Application.Current.Resources["Sd.ToolToggle"], Content = icon, ToolTip = tool.Title(),
                Height = 31, Margin = new Thickness(1), Tag = tool,
            };
            button.Click += (_, _) => ToolPicked?.Invoke(tool);
            _toolButtons[tool] = button;
            grid.Children.Add(button);
        }
        surface.Child = grid;
        section.Children.Add(surface);
        return section;
    }

    private void RefreshStyleSection()
    {
        if (_updatingUi) return;
        _updatingUi = true;
        try
        {
            _styleSection.Children.Clear();
            Annotation? selected = _canvas.Model.Selected;
            AnnotationTool tool = selected?.Tool ?? _canvas.ActiveTool;
            if (tool.SupportsColor())
            {
                AddStyleSection("Style", content =>
                {
                    content.Children.Add(BuildColorPalette());
                    if (!tool.IsRedactionTool() && tool != AnnotationTool.Text)
                    {
                        double value = selected?.StrokeWidth > 0 ? selected.StrokeWidth : _canvas.ActiveStroke;
                        content.Children.Add(BuildPillSlider("Stroke", value, .002, .02, FormatStroke, (v, drag) =>
                        { _canvas.ActiveStroke = v; _canvas.SetSelectedStroke(v, drag); }, displayScale: 1000));
                    }
                });
            }
            if (tool.IsRedactionTool())
            {
                AddStyleSection("Smart Redaction", content =>
                {
                    var modes = new SegmentedControl(
                        ("Pixelate", AnnotationTool.Pixelate),
                        ("Blur", AnnotationTool.Blur))
                    {
                        Margin = new Thickness(0, 4, 0, 8),
                    };
                    modes.Select(tool == AnnotationTool.Pixelate ? AnnotationTool.Pixelate : AnnotationTool.Blur, raise: false);
                    modes.SelectionChanged += tag =>
                    {
                        if (tag is AnnotationTool picked)
                        {
                            ToolPicked?.Invoke(picked);
                        }
                    };
                    content.Children.Add(modes);
                    double value = selected?.Tool.IsRedactionTool() == true && selected.Density >= 0 ? selected.Density : _canvas.ActiveDensity;
                    content.Children.Add(BuildPillSlider("Strength", value, .02, 1, FormatPercent, (v, drag) =>
                    { _canvas.ActiveDensity = v; _canvas.SetSelectedDensity(v, drag); }, displayScale: 100));
                });
            }
            if (tool == AnnotationTool.Text)
            {
                AddStyleSection("Text", content =>
                {
                    double value = selected?.Tool == AnnotationTool.Text ? selected.FontSize : _canvas.ActiveFontSize;
                    content.Children.Add(BuildPillSlider("Size", value, .008, .08, FormatFontSize, (v, drag) =>
                    { _canvas.ActiveFontSize = v; _canvas.SetSelectedFontSize(v, drag); }, displayScale: 1000));
                });
            }
        }
        finally { _updatingUi = false; }
    }

    /// <summary>
    /// Wraps a titled group in a collapsible section, remembering which ones
    /// the user folded away so a live style refresh does not spring them open.
    /// </summary>
    private InspectorSection Section(string title, UIElement body)
    {
        var section = new InspectorSection(title, body)
        {
            IsExpanded = !_collapsed.Contains(title),
        };
        section.ExpandedChanged += (_, _) =>
        {
            if (section.IsExpanded)
            {
                _collapsed.Remove(title);
            }
            else
            {
                _collapsed.Add(title);
            }
        };
        return section;
    }

    private void AddStyleSection(string title, Action<StackPanel> build)
    {
        var content = new StackPanel();
        build(content);
        _styleSection.Children.Add(Section(title, content));
    }

    private UIElement BuildColorPalette()
    {
        var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 7) };
        foreach (var color in AnnotationColor.Palette)
        {
            bool active = color == _canvas.ActiveColor;
            // iOS tile parity: a hairline ring at rest, a 2px accent ring held
            // off the swatch by a 2.5px gap when selected.
            var swatch = new Border
            {
                Width = 23, Height = 23, CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(color.A255, color.R255, color.G255, color.B255)),
                BorderBrush = active ? Brush("Sd.Accent") : Brush("Sd.Border"),
                BorderThickness = new Thickness(active ? 2 : 1),
                Margin = new Thickness(active ? 1.5 : 2.5, active ? 1.5 : 2.5, 3.5, 3.5),
                Cursor = Cursors.Hand, ToolTip = "Set annotation color",
            };
            swatch.MouseLeftButtonDown += (_, _) => { _canvas.SetSelectedColor(color); RefreshStyleSection(); };
            row.Children.Add(swatch);
        }
        return row;
    }

    /// <summary>
    /// Builds a scrubber row and binds it to the canvas's live style edit, so
    /// dragging opens one undo batch and releasing closes it.
    /// </summary>
    private PillSliderRow BuildPillSlider(
        string label,
        double value,
        double min,
        double max,
        Func<double, string> format,
        Action<double, bool> apply,
        double displayScale = 1)
    {
        return new PillSliderRow(label, value, min, max, format, apply, displayScale)
        {
            BeginEdit = () => _canvas.BeginSelectedStyleEdit(),
            EndEdit = () => _canvas.EndSelectedStyleEdit(),
        };
    }

    private void LoadPresets()
    {
        _presets = _presetStore.Load();
        _updatingUi = true;
        _presetBox.Items.Clear();
        foreach (var preset in _presets) _presetBox.Items.Add(new ComboBoxItem { Content = preset.Name, Tag = preset });
        _presetBox.SelectedIndex = 0;
        _updatingUi = false;
    }

    private void OnPresetSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (!_updatingUi && _presetBox.SelectedItem is ComboBoxItem { Tag: AnnotationPreset preset }) ApplyPreset(preset);
    }

    private void ApplyPreset(AnnotationPreset preset)
    {
        if (preset.Name == AnnotationPresetStore.CurrentSettingsName) return;
        if (preset.ColorIndex >= 0 && preset.ColorIndex < AnnotationColor.Palette.Count) _canvas.SetSelectedColor(AnnotationColor.Palette[preset.ColorIndex]);
        if (preset.StrokeWidth > 0) _canvas.ActiveStroke = preset.StrokeWidth;
        if (preset.Density > 0) _canvas.ActiveDensity = preset.Density;
        if (preset.Tool != AnnotationTool.Select) ToolPicked?.Invoke(preset.Tool);
        RefreshAllSelections();
    }

    private void AddPresetFromCurrent()
    {
        string? name = Prompt("Save preset", "Preset name");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (_presets.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("A preset already has that name.", "Screenstop", MessageBoxButton.OK, MessageBoxImage.Information); return;
        }
        _presets.Insert(Math.Max(1, _presets.Count - 1), new AnnotationPreset
        {
            Name = name, Tool = _canvas.ActiveTool, ColorIndex = IndexOfColor(_canvas.ActiveColor), StrokeWidth = _canvas.ActiveStroke, Density = _canvas.ActiveDensity,
        });
        PersistSavedPresets(); LoadPresets(); SelectPresetByName(name);
    }

    private void DeleteSelectedPreset()
    {
        if (_presetBox.SelectedItem is not ComboBoxItem { Tag: AnnotationPreset preset } || preset.Name is AnnotationPresetStore.CurrentSettingsName or AnnotationPresetStore.DefaultName) return;
        _presets.Remove(preset); PersistSavedPresets(); LoadPresets();
    }

    private void PersistSavedPresets() => _presetStore.Save(_presets.Where(p => p.Name is not AnnotationPresetStore.CurrentSettingsName and not AnnotationPresetStore.DefaultName).ToList());

    private void SelectPresetByName(string name)
    {
        foreach (ComboBoxItem item in _presetBox.Items)
            if (item.Tag is AnnotationPreset preset && preset.Name == name) { _presetBox.SelectedItem = item; return; }
    }

    private static Button IconButton(string glyph, string tip, RoutedEventHandler click)
    {
        var button = new Button { Content = glyph, FontFamily = UiGlyph.Font, FontSize = 12, Style = (Style)Application.Current.Resources["Sd.GhostButton"], Padding = new Thickness(7, 5, 7, 5), ToolTip = tip };
        button.Click += click; return button;
    }
    private static int IndexOfColor(AnnotationColor color)
    {
        for (int index = 0; index < AnnotationColor.Palette.Count; index++)
            if (AnnotationColor.Palette[index] == color) return index;
        return -1;
    }
    private static string FormatPercent(double value) => $"{Math.Round(value * 100):0}%";
    private static string FormatStroke(double value) => $"{Math.Round(value * 1000):0}";
    private static string FormatFontSize(double value) => $"{Math.Round(value * 1000):0}";
    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    private static string? Prompt(string title, string label)
    {
        var window = new Window
        {
            Title = title,
            Width = 340,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = Brush("Sd.BgRaised"),
            Foreground = Brush("Sd.Text"),
        };
        var box = new TextBox { Text = "My preset", Margin = new Thickness(0, 8, 0, 14) };
        box.SelectAll();
        box.Focus();
        var save = new Button { Content = "Save", Style = (Style)Application.Current.Resources["Sd.AccentButton"], IsDefault = true, Padding = new Thickness(18, 6, 18, 6) };
        var cancel = new Button { Content = "Cancel", Style = (Style)Application.Current.Resources["Sd.Button"], IsCancel = true, Padding = new Thickness(18, 6, 18, 6), Margin = new Thickness(8, 0, 0, 0) };
        string? result = null;
        save.Click += (_, _) => { result = box.Text; window.DialogResult = true; };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        var content = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };
        content.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold });
        content.Children.Add(box);
        content.Children.Add(buttons);
        window.Content = content;
        return window.ShowDialog() == true ? result : null;
    }
}
