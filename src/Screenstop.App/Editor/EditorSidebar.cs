using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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
    private readonly StackPanel _styleSection = new() { Margin = new Thickness(0, 12, 0, 0) };
    private readonly ComboBox _presetBox = new();
    private List<AnnotationPreset> _presets = new();
    private bool _updatingUi;

    public event Action<AnnotationTool>? ToolPicked;

    public EditorSidebar(AnnotationCanvas canvas, AnnotationPresetStore presetStore)
    {
        _canvas = canvas;
        _presetStore = presetStore;
        Width = 278;
        Background = Brush("Sd.BgRaised");
        BorderBrush = Brush("Sd.BorderSoft");
        BorderThickness = new Thickness(1, 0, 0, 0);

        var stack = new StackPanel();
        stack.Children.Add(BuildPresetRow());
        stack.Children.Add(BuildToolsSection());
        stack.Children.Add(_styleSection);
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(12, 14, 12, 18),
            Content = stack,
        };
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
        root.Children.Add(Caption("Preset"));
        var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
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
        var section = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        section.Children.Add(Caption("Tools"));
        var surface = new Border
        {
            Background = Brush("Sd.Panel"), BorderBrush = Brush("Sd.BorderSoft"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9), Padding = new Thickness(4), Margin = new Thickness(0, 5, 0, 0),
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
                        content.Children.Add(PillSlider("Stroke", value, .002, .02, FormatStroke, (v, drag) =>
                        { _canvas.ActiveStroke = v; _canvas.SetSelectedStroke(v, drag); }, displayScale: 1000));
                    }
                });
            }
            if (tool.IsRedactionTool())
            {
                AddStyleSection("Smart Redaction", content =>
                {
                    var modes = new Grid { Margin = new Thickness(0, 4, 0, 8) };
                    modes.ColumnDefinitions.Add(new ColumnDefinition()); modes.ColumnDefinitions.Add(new ColumnDefinition());
                    modes.Children.Add(ModeButton("Pixelate", AnnotationTool.Pixelate, tool == AnnotationTool.Pixelate, 0));
                    modes.Children.Add(ModeButton("Blur", AnnotationTool.Blur, tool == AnnotationTool.Blur, 1));
                    content.Children.Add(modes);
                    double value = selected?.Tool.IsRedactionTool() == true && selected.Density >= 0 ? selected.Density : _canvas.ActiveDensity;
                    content.Children.Add(PillSlider("Strength", value, .02, 1, FormatPercent, (v, drag) =>
                    { _canvas.ActiveDensity = v; _canvas.SetSelectedDensity(v, drag); }, displayScale: 100));
                });
            }
            if (tool == AnnotationTool.Text)
            {
                AddStyleSection("Text", content =>
                {
                    double value = selected?.Tool == AnnotationTool.Text ? selected.FontSize : _canvas.ActiveFontSize;
                    content.Children.Add(PillSlider("Size", value, .008, .08, FormatFontSize, (v, drag) =>
                    { _canvas.ActiveFontSize = v; _canvas.SetSelectedFontSize(v, drag); }, displayScale: 1000));
                });
            }
        }
        finally { _updatingUi = false; }
    }

    private void AddStyleSection(string title, Action<StackPanel> build)
    {
        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        section.Children.Add(Caption(title));
        var content = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        build(content);
        section.Children.Add(content);
        _styleSection.Children.Add(section);
    }

    private UIElement BuildColorPalette()
    {
        var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 7) };
        foreach (var color in AnnotationColor.Palette)
        {
            bool active = color == _canvas.ActiveColor;
            var swatch = new Border
            {
                Width = 23, Height = 23, CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(color.A255, color.R255, color.G255, color.B255)),
                BorderBrush = active ? Brush("Sd.Accent") : Brush("Sd.BgRaised"), BorderThickness = new Thickness(active ? 3 : 1),
                Margin = new Thickness(0, 0, 6, 5), Cursor = Cursors.Hand, ToolTip = "Set annotation color",
            };
            swatch.MouseLeftButtonDown += (_, _) => { _canvas.SetSelectedColor(color); RefreshStyleSection(); };
            row.Children.Add(swatch);
        }
        return row;
    }

    private UIElement ModeButton(string label, AnnotationTool tool, bool selected, int column)
    {
        var button = new ToggleButton
        {
            Content = label, IsChecked = selected, Style = (Style)Application.Current.Resources["Sd.ToolToggle"], Height = 29,
            Margin = new Thickness(column == 0 ? 0 : 2, 0, column == 1 ? 0 : 2, 0),
        };
        button.Click += (_, _) => ToolPicked?.Invoke(tool);
        Grid.SetColumn(button, column);
        return button;
    }

    private UIElement PillSlider(string label, double value, double min, double max, Func<double, string> format, Action<double, bool> apply, double displayScale = 1)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        var surface = new Border
        {
            Background = Brush("Sd.Panel"), BorderBrush = Brush("Sd.BorderSoft"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 2, 7, 2), MinHeight = 32,
        };
        var inner = new Grid();
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inner.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush("Sd.Text"), VerticalAlignment = VerticalAlignment.Center });
        var slider = new Slider
        {
            Style = (Style)Application.Current.Resources["Sd.Slider"], Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max),
            SmallChange = (max - min) / 100, LargeChange = (max - min) / 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0),
        };
        Grid.SetColumn(slider, 1); inner.Children.Add(slider); surface.Child = inner;
        var valueBox = new TextBox
        {
            Text = format(slider.Value), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center, MinHeight = 32, Margin = new Thickness(7, 0, 0, 0),
            ToolTip = $"Enter a value between {format(min)} and {format(max)}",
        };
        bool dragging = false;
        slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => { dragging = true; _canvas.BeginSelectedStyleEdit(); }));
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => { if (!dragging) return; dragging = false; _canvas.EndSelectedStyleEdit(); }));
        slider.ValueChanged += (_, _) =>
        {
            if (_updatingUi) return;
            valueBox.Text = format(slider.Value);
            apply(slider.Value, dragging);
        };
        valueBox.LostKeyboardFocus += (_, _) => CommitTypedValue();
        valueBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { CommitTypedValue(); Keyboard.ClearFocus(); e.Handled = true; }
            if (e.Key == Key.Escape) { valueBox.Text = format(slider.Value); Keyboard.ClearFocus(); e.Handled = true; }
        };
        void CommitTypedValue()
        {
            if (TryParseValue(valueBox.Text, out double typed)) slider.Value = Math.Clamp(typed / displayScale, min, max);
            valueBox.Text = format(slider.Value);
        }
        Grid.SetColumn(surface, 0); Grid.SetColumn(valueBox, 1);
        row.Children.Add(surface); row.Children.Add(valueBox);
        return row;
    }

    private static bool TryParseValue(string text, out double value)
    {
        string normalized = text.Trim().TrimEnd('%').Trim();
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && !double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
        return double.IsFinite(value);
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

    private static TextBlock Caption(string text) => new() { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brush("Sd.Text") };
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
        var window = new Window { Title = title, Width = 330, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Background = Brush("Sd.BgRaised"), Foreground = Brush("Sd.Text") };
        var box = new TextBox { Text = "My preset", Margin = new Thickness(0, 7, 0, 12) }; box.SelectAll();
        var save = new Button { Content = "Save", Style = (Style)Application.Current.Resources["Sd.AccentButton"], IsDefault = true, Padding = new Thickness(18, 5, 18, 5) };
        var cancel = new Button { Content = "Cancel", Style = (Style)Application.Current.Resources["Sd.Button"], IsCancel = true, Padding = new Thickness(18, 5, 18, 5), Margin = new Thickness(6, 0, 0, 0) };
        string? result = null;
        save.Click += (_, _) => { result = box.Text; window.DialogResult = true; };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; buttons.Children.Add(save); buttons.Children.Add(cancel);
        var content = new StackPanel { Margin = new Thickness(16) }; content.Children.Add(new TextBlock { Text = label, FontSize = 12 }); content.Children.Add(box); content.Children.Add(buttons); window.Content = content;
        return window.ShowDialog() == true ? result : null;
    }
}
