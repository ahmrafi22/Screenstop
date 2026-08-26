using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Screendrop.Core.Annotations;

namespace Screendrop.App.Editor;

/// <summary>
/// The editor's right sidebar (mac AnnotationEditorInspector parity): a
/// 6-column tool grid, preset row with save/delete, and per-tool style
/// sections — Smart Redaction (Pixelate/Blur + Strength), Text size, Stroke
/// width. Advanced scene effects from the mac (Camera perspective,
/// Progressive Blur, Background fills) are out of scope for the Windows
/// port; this covers every tool the port implements.
/// </summary>
internal sealed class EditorSidebar : UserControl
{
    private const string DeleteGlyph = "\uE74D";
    private const string AddGlyph = "\uE710";

    private readonly AnnotationCanvas _canvas;
    private readonly AnnotationPresetStore _presetStore;
    private List<AnnotationPreset> _presets = new();

    private readonly Dictionary<AnnotationTool, ToggleButton> _toolButtons = new();
    private readonly StackPanel _styleSection = new() { Margin = new Thickness(0, 12, 0, 0) };
    private readonly ComboBox _presetBox = new();
    private ToggleButton? _pixelateChip;
    private ToggleButton? _blurChip;
    private bool _updatingUi;

    /// Raised when the user picks a tool in the grid (the window syncs its toolbar).
    public event Action<AnnotationTool>? ToolPicked;

    public EditorSidebar(AnnotationCanvas canvas, AnnotationPresetStore presetStore)
    {
        _canvas = canvas;
        _presetStore = presetStore;

        Width = 236;
        Background = (Brush)Application.Current.Resources["Sd.BgRaised"];

        var root = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(12),
        };
        var stack = new StackPanel();

        // Preset row: combo + delete + add (mac inspector header).
        var presetRow = new Grid();
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var deleteButton = new Button
        {
            Content = DeleteGlyph,
            FontFamily = UiGlyph.Font,
            FontSize = 12,
            Style = (Style)Application.Current.Resources["Sd.GhostButton"],
            Margin = new Thickness(4, 0, 2, 0),
            Padding = new Thickness(6, 4, 6, 4),
            ToolTip = "Delete selected preset",
        };
        deleteButton.Click += (_, _) => DeleteSelectedPreset();

        var addButton = new Button
        {
            Content = AddGlyph,
            FontFamily = UiGlyph.Font,
            FontSize = 12,
            Style = (Style)Application.Current.Resources["Sd.GhostButton"],
            Margin = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(6, 4, 6, 4),
            ToolTip = "Save current settings as a preset",
        };
        addButton.Click += (_, _) => AddPresetFromCurrent();

        _presetBox.SelectionChanged += OnPresetSelected;

        Grid.SetColumn(_presetBox, 0);
        Grid.SetColumn(deleteButton, 1);
        Grid.SetColumn(addButton, 2);
        presetRow.Children.Add(_presetBox);
        presetRow.Children.Add(deleteButton);
        presetRow.Children.Add(addButton);
        stack.Children.Add(presetRow);

        // Tool grid: 6 columns like the mac inspector.
        var toolGrid = new UniformGrid { Columns = 6, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var (tool, icon) in new[]
        {
            (AnnotationTool.Select, ToolIcon.SelectCursor()),
            (AnnotationTool.Rectangle, ToolIcon.Rectangle()),
            (AnnotationTool.FilledRectangle, ToolIcon.FilledRectangle()),
            (AnnotationTool.Ellipse, ToolIcon.Ellipse()),
            (AnnotationTool.Line, ToolIcon.Line()),
            (AnnotationTool.Arrow, ToolIcon.Arrow()),
            (AnnotationTool.Freehand, ToolIcon.Freehand()),
            (AnnotationTool.NumberedCircle, ToolIcon.StepMarker()),
            (AnnotationTool.Text, ToolIcon.Text()),
            (AnnotationTool.Highlight, ToolIcon.Highlight()),
            (AnnotationTool.Pixelate, ToolIcon.Pixelate()),
            (AnnotationTool.Blur, ToolIcon.Blur()),
        })
        {
            var toggle = new ToggleButton
            {
                Style = (Style)Application.Current.Resources["Sd.ToolToggle"],
                Content = icon,
                ToolTip = $"{tool.Title()}",
                Height = 30,
                Margin = new Thickness(1),
                Tag = tool,
            };
            toggle.Click += (_, _) =>
            {
                ToolPicked?.Invoke(tool);
                RefreshStyleSection();
            };
            _toolButtons[tool] = toggle;
            toolGrid.Children.Add(toggle);
        }

        stack.Children.Add(toolGrid);
        stack.Children.Add(_styleSection);
        root.Content = stack;
        Content = root;

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

    /// Syncs grid highlight with the canvas' active tool, then rebuilds sections.
    public void RefreshAllSelections()
    {
        foreach (var (tool, button) in _toolButtons)
        {
            button.IsChecked = tool == _canvas.ActiveTool;
        }

        RefreshStyleSection();
    }

    // ===== Presets =====

    private void LoadPresets()
    {
        _presets = _presetStore.Load();
        _updatingUi = true;
        _presetBox.Items.Clear();
        foreach (var preset in _presets)
        {
            _presetBox.Items.Add(new ComboBoxItem { Content = preset.Name, Tag = preset });
        }

        _presetBox.SelectedIndex = _presets.Count > 0 ? 0 : -1;
        _updatingUi = false;
    }

    private void OnPresetSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi)
        {
            return;
        }

        if (_presetBox.SelectedItem is ComboBoxItem item && item.Tag is AnnotationPreset preset)
        {
            ApplyPreset(preset);
        }
    }

    private void ApplyPreset(AnnotationPreset preset)
    {
        // "Current Settings" is a live marker — nothing stored to apply.
        if (preset.Name == AnnotationPresetStore.CurrentSettingsName)
        {
            return;
        }

        if (preset.ColorIndex >= 0 && preset.ColorIndex < AnnotationColor.Palette.Count)
        {
            _canvas.ActiveColor = AnnotationColor.Palette[preset.ColorIndex];
        }

        if (preset.StrokeWidth > 0)
        {
            _canvas.ActiveStroke = preset.StrokeWidth;
        }

        if (preset.Density > 0)
        {
            _canvas.ActiveDensity = preset.Density;
        }

        if (preset.Tool != AnnotationTool.Select)
        {
            ToolPicked?.Invoke(preset.Tool);
        }

        RefreshAllSelections();
    }

    private void AddPresetFromCurrent()
    {
        string? name = Prompt("Save preset", "Preset name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var preset = new AnnotationPreset
        {
            Name = name.Trim(),
            Tool = _canvas.ActiveTool,
            ColorIndex = IndexOfColor(_canvas.ActiveColor),
            StrokeWidth = _canvas.ActiveStroke,
            Density = _canvas.ActiveDensity,
        };

        _presets.Insert(Math.Max(1, _presets.Count - 1), preset); // before the Default entry
        PersistSavedPresets();
        LoadPresets();
        SelectPresetByName(name.Trim());
    }

    private void DeleteSelectedPreset()
    {
        if (_presetBox.SelectedItem is not ComboBoxItem item || item.Tag is not AnnotationPreset preset)
        {
            return;
        }

        if (preset.Name is AnnotationPresetStore.CurrentSettingsName or AnnotationPresetStore.DefaultName)
        {
            return; // built-ins stay
        }

        _presets.Remove(preset);
        PersistSavedPresets();
        LoadPresets();
    }

    private void PersistSavedPresets() =>
        _presetStore.Save(_presets.Where(p =>
            p.Name != AnnotationPresetStore.CurrentSettingsName &&
            p.Name != AnnotationPresetStore.DefaultName).ToList());

    private void SelectPresetByName(string name)
    {
        foreach (var item in _presetBox.Items.Cast<ComboBoxItem>())
        {
            if (item.Tag is AnnotationPreset p && p.Name == name)
            {
                bool previous = _updatingUi;
                _updatingUi = true;
                _presetBox.SelectedItem = item;
                _updatingUi = previous;
                return;
            }
        }
    }

    // ===== Style sections =====

    private void RefreshStyleSection()
    {
        if (_updatingUi)
        {
            return;
        }

        _updatingUi = true;
        try
        {
            _styleSection.Children.Clear();
            _pixelateChip = null;
            _blurChip = null;

            var selected = _canvas.Model.Selected;

            // Smart Redaction (mac parity): Pixelate/Blur segmented control +
            // Strength slider whenever a redaction tool is relevant.
            if (_canvas.ActiveTool.IsRedactionTool() || (selected?.Tool.IsRedactionTool() ?? false))
            {
                _styleSection.Children.Add(Header("Smart Redaction"));

                var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
                _pixelateChip = ModeChip("Pixelate", AnnotationTool.Pixelate);
                _blurChip = ModeChip("Blur", AnnotationTool.Blur);
                modeRow.Children.Add(_pixelateChip);
                modeRow.Children.Add(_blurChip);

                var activeRedaction = selected?.Tool.IsRedactionTool() == true ? selected.Tool : _canvas.ActiveTool;
                _pixelateChip.IsChecked = activeRedaction == AnnotationTool.Pixelate;
                _blurChip.IsChecked = activeRedaction == AnnotationTool.Blur;
                _styleSection.Children.Add(modeRow);

                double density = ResolveDensity(selected);
                var valueLabel = ValueLabel($"{Math.Round(density * 100)}%");
                var slider = MakeSlider(density, 0.02, 1, v =>
                {
                    double percent = Math.Round(v * 100) / 100;
                    _canvas.ActiveDensity = percent;
                    _canvas.SetSelectedDensity(percent);
                    valueLabel.Text = $"{Math.Round(percent * 100)}%";
                });
                _styleSection.Children.Add(Row("Strength", slider, valueLabel));
            }

            // Text section.
            if (_canvas.ActiveTool == AnnotationTool.Text || selected?.Tool == AnnotationTool.Text)
            {
                _styleSection.Children.Add(Header("Text"));
                double size = selected?.Tool == AnnotationTool.Text ? selected.FontSize : _canvas.ActiveFontSize;
                var sizeLabel = ValueLabel(SizeText(size));
                var slider = MakeSlider(size, 0.008, 0.08, v =>
                {
                    double snapped = Math.Round(v * 1000) / 1000;
                    _canvas.ActiveFontSize = snapped;
                    _canvas.SetSelectedFontSize(snapped);
                    sizeLabel.Text = SizeText(snapped);
                });
                _styleSection.Children.Add(Row("Size", slider, sizeLabel));
            }

            // Stroke section for stroke-drawn shapes.
            bool strokeRelevant = _canvas.ActiveTool.SupportsColor()
                && !_canvas.ActiveTool.IsRedactionTool()
                && _canvas.ActiveTool != AnnotationTool.Text;
            if (strokeRelevant)
            {
                _styleSection.Children.Add(Header("Stroke"));
                double stroke = _canvas.ActiveStroke;
                var strokeLabel = ValueLabel(SizeText(stroke));
                var slider = MakeSlider(stroke, 0.002, 0.02, v =>
                {
                    double snapped = Math.Round(v * 2000) / 2000;
                    _canvas.ActiveStroke = snapped;
                    strokeLabel.Text = SizeText(snapped);
                });
                _styleSection.Children.Add(Row("Width", slider, strokeLabel));
            }
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private double ResolveDensity(Annotation? selected)
    {
        if (selected is { } sel && sel.Tool.IsRedactionTool() && sel.Density >= 0)
        {
            return sel.Density;
        }

        return _canvas.ActiveDensity;
    }

    private ToggleButton ModeChip(string label, AnnotationTool tool)
    {
        var chip = new ToggleButton
        {
            Content = label,
            Style = (Style)Application.Current.Resources["Sd.ToolToggle"],
            Margin = new Thickness(0, 0, 4, 0),
            Padding = new Thickness(10, 3, 10, 3),
            FontSize = 11,
        };
        chip.Click += (_, _) => ToolPicked?.Invoke(tool);
        return chip;
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = (Brush)Application.Current.Resources["Sd.TextMuted"],
        Margin = new Thickness(0, 6, 0, 2),
    };

    private static DockPanel Row(string label, FrameworkElement control, FrameworkElement value)
    {
        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        DockPanel.SetDock(value, Dock.Right);
        row.Children.Add(value);
        var labelText = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            FontSize = 12,
            MinWidth = 52,
        };
        DockPanel.SetDock(labelText, Dock.Left);
        row.Children.Add(labelText);
        row.Children.Add(control);
        return row;
    }

    private static Slider MakeSlider(double value, double min, double max, Action<double> onChanged)
    {
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            IsSnapToTickEnabled = false,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["Sd.Slider"],
        };
        slider.ValueChanged += (_, e) => onChanged(e.NewValue);
        return slider;
    }

    private static string SizeText(double normalized) => $"{Math.Round(normalized * 1000)}";

    private static TextBlock ValueLabel(string text) => new()
    {
        Text = text,
        Foreground = (Brush)Application.Current.Resources["Sd.TextMuted"],
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
        MinWidth = 40,
        TextAlignment = TextAlignment.Right,
    };

    private static int IndexOfColor(AnnotationColor color)
    {
        for (int i = 0; i < AnnotationColor.Palette.Count; i++)
        {
            if (AnnotationColor.Palette[i] == color)
            {
                return i;
            }
        }

        return -1;
    }

    /// Small themed name prompt for saving presets.
    private static string? Prompt(string title, string label)
    {
        var window = new Window
        {
            Title = title,
            Width = 320,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = (Brush)Application.Current.Resources["Sd.Bg"],
            Foreground = (Brush)Application.Current.Resources["Sd.Text"],
        };

        var box = new TextBox { Text = "My preset", Margin = new Thickness(0, 8, 0, 12) };
        box.SelectAll();

        var ok = new Button
        {
            Content = "Save",
            Style = (Style)Application.Current.Resources["Sd.AccentButton"],
            IsDefault = true,
            Padding = new Thickness(18, 5, 18, 5),
        };
        var cancel = new Button
        {
            Content = "Cancel",
            Style = (Style)Application.Current.Resources["Sd.Button"],
            IsCancel = true,
            Padding = new Thickness(18, 5, 18, 5),
        };

        string? result = null;
        ok.Click += (_, _) =>
        {
            result = box.Text;
            window.Close();
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var stack = new StackPanel { Margin = new Thickness(14) };
        stack.Children.Add(new TextBlock { Text = label, FontSize = 12 });
        stack.Children.Add(box);
        stack.Children.Add(buttons);
        window.Content = stack;

        return window.ShowDialog() == true ? result : null;
    }
}
