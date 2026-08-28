using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Screenstop.Core.Background;

namespace Screenstop.App.Editor;

/// Inspector panel that edits the mockup stage (background, layout, camera,
/// focus blur, border, watermark) and previews it live on the canvas. Ports the
/// mac background inspector, including the built-in color/gradient/wallpaper
/// presets and camera looks.
internal sealed class BackgroundInspector : UserControl
{
    private readonly AnnotationCanvas _canvas;
    private readonly BackgroundPresetStore _presetStore = new();
    private readonly WallpaperStore _wallpaperStore = new();

    private BackgroundSettings _settings;
    private bool _updating;

    private readonly StackPanel _root = new();

    public BackgroundInspector(AnnotationCanvas canvas)
    {
        _canvas = canvas;
        _settings = canvas.Background?.Clone() ?? new BackgroundSettings();
        _wallpaperStore.Reload();

        Background = Brush("Sd.BgRaised");
        _root.Children.Add(BuildPresetSection());
        _root.Children.Add(BuildStyleSection());
        _root.Children.Add(BuildLayoutSection());
        _root.Children.Add(BuildShadowSection());
        _root.Children.Add(BuildCameraSection());
        _root.Children.Add(BuildBlurSection());
        _root.Children.Add(BuildBorderSection());
        _root.Children.Add(BuildWatermarkSection());
        Content = _root;
        Refresh();
    }

    /// Pushes the current settings to the canvas for a live preview.
    private void Apply()
    {
        if (_updating)
        {
            return;
        }

        _canvas.SetBackground(_settings.Clone());
    }

    // ---------------------------------------------------------------- presets

    private UIElement BuildPresetSection()
    {
        var section = Section("Presets");

        var box = new ComboBox { Style = (Style)Application.Current.Resources["Sd.ComboBox"] };
        box.Items.Add(new ComboBoxItem { Content = "None", Tag = null });
        foreach (var preset in _presetStore.Presets)
        {
            box.Items.Add(new ComboBoxItem { Content = preset.Name, Tag = preset });
        }

        box.SelectedIndex = 0;
        box.SelectionChanged += (_, _) =>
        {
            if (_updating || box.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            if (item.Tag is BackgroundPreset preset)
            {
                _settings = preset.Background.Clone();
                _settings.Camera.UpgradeProjectionIfNeeded();
            }
            else
            {
                _settings = new BackgroundSettings();
            }

            Apply();
            Refresh();
        };

        var save = new Button
        {
            Content = "Save Preset…",
            Style = (Style)Application.Current.Resources["Sd.Button"],
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        save.Click += (_, _) =>
        {
            string? name = Prompt("Save Background Preset", "Preset name");
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (_presetStore.SavePreset(name, _settings) is null)
            {
                MessageBox.Show("A preset already has that name.", "Screenstop",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Rebuild(section, BuildPresetSection());
        };

        section.Children.Add(box);
        section.Children.Add(save);
        return section;
    }

    // ---------------------------------------------------------------- style

    private UIElement BuildStyleSection()
    {
        var section = Section("Background");

        var kind = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        for (int i = 0; i < 4; i++)
        {
            kind.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        AddKindButton(kind, 0, "None", BackgroundStyleKind.None);
        AddKindButton(kind, 1, "Color", BackgroundStyleKind.Solid);
        AddKindButton(kind, 2, "Gradient", BackgroundStyleKind.Gradient);
        AddKindButton(kind, 3, "Image", BackgroundStyleKind.Wallpaper);
        section.Children.Add(kind);

        var detail = new StackPanel();
        section.Children.Add(detail);
        section.Tag = detail;
        return section;
    }

    private void AddKindButton(Grid host, int column, string label, BackgroundStyleKind kind)
    {
        var button = new ToggleButton
        {
            Content = label,
            Style = (Style)Application.Current.Resources["Sd.ToolToggle"],
            Height = 28,
            Margin = new Thickness(column == 0 ? 0 : 2, 0, column == 3 ? 0 : 2, 0),
            IsChecked = _settings.Style.Kind == kind,
            Tag = kind,
        };
        button.Click += (_, _) =>
        {
            _settings.Style = kind switch
            {
                BackgroundStyleKind.Solid => BackgroundStyle.Solid(
                    _settings.Style.ColorId ?? BackgroundColor.Black.ColorId),
                BackgroundStyleKind.Gradient => BackgroundStyle.Gradient(
                    _settings.Style.GradientId ?? BackgroundGradient.Presets[0].GradientId),
                BackgroundStyleKind.Wallpaper => BackgroundStyle.Wallpaper(
                    _settings.Style.WallpaperPath ?? string.Empty),
                _ => BackgroundStyle.None(),
            };
            Apply();
            Refresh();
        };
        Grid.SetColumn(button, column);
        host.Children.Add(button);
    }

    private UIElement BuildSolidPicker()
    {
        var grid = new WrapPanel();
        foreach (var color in BackgroundColor.PlainPresets)
        {
            bool active = _settings.Style.Kind == BackgroundStyleKind.Solid
                && _settings.Style.ColorId == color.ColorId;
            var swatch = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromRgb(
                    (byte)(color.R * 255), (byte)(color.G * 255), (byte)(color.B * 255))),
                BorderBrush = active ? Brush("Sd.Accent") : Brush("Sd.Border"),
                BorderThickness = new Thickness(active ? 2.5 : 1),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = color.Title,
            };
            swatch.MouseLeftButtonDown += (_, _) =>
            {
                _settings.Style = BackgroundStyle.Solid(color.ColorId);
                Apply();
                Refresh();
            };
            grid.Children.Add(swatch);
        }

        return grid;
    }

    private UIElement BuildGradientPicker()
    {
        var grid = new WrapPanel();
        foreach (var gradient in BackgroundGradient.Presets)
        {
            bool active = _settings.Style.Kind == BackgroundStyleKind.Gradient
                && _settings.Style.GradientId == gradient.GradientId;

            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(gradient.StartPoint.X, gradient.StartPoint.Y),
                EndPoint = new Point(gradient.EndPoint.X, gradient.EndPoint.Y),
            };
            for (int i = 0; i < gradient.Colors.Count; i++)
            {
                var c = gradient.Colors[i];
                brush.GradientStops.Add(new GradientStop
                {
                    Color = Color.FromRgb((byte)(c.R * 255), (byte)(c.G * 255), (byte)(c.B * 255)),
                    Offset = gradient.Colors.Count <= 1 ? 0 : (double)i / (gradient.Colors.Count - 1),
                });
            }

            var chip = new Border
            {
                Width = 40,
                Height = 26,
                CornerRadius = new CornerRadius(6),
                Background = brush,
                BorderBrush = active ? Brush("Sd.Accent") : Brush("Sd.Border"),
                BorderThickness = new Thickness(active ? 2.5 : 1),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = gradient.Title,
            };
            chip.MouseLeftButtonDown += (_, _) =>
            {
                _settings.Style = BackgroundStyle.Gradient(gradient.GradientId);
                Apply();
                Refresh();
            };
            grid.Children.Add(chip);
        }

        return grid;
    }

    private UIElement BuildWallpaperPicker()
    {
        var stack = new StackPanel();

        var box = new ComboBox { Style = (Style)Application.Current.Resources["Sd.ComboBox"] };
        box.Items.Add(new ComboBoxItem { Content = "(none)", Tag = null });
        foreach (var pack in WallpaperPack.BuiltIn)
        {
            foreach (var path in _wallpaperStore.WallpapersFor(pack))
            {
                box.Items.Add(new ComboBoxItem
                {
                    Content = $"{pack.Title} — {Path.GetFileNameWithoutExtension(path)}",
                    Tag = path,
                });
            }
        }

        foreach (var recent in _wallpaperStore.RecentWallpapers)
        {
            box.Items.Add(new ComboBoxItem
            {
                Content = $"Recent — {Path.GetFileNameWithoutExtension(recent)}",
                Tag = recent,
            });
        }

        SelectComboItemByTag(box, _settings.Style.WallpaperPath);
        box.SelectionChanged += (_, _) =>
        {
            if (_updating || box.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            if (item.Tag is string path)
            {
                _settings.Style = BackgroundStyle.Wallpaper(path);
                _wallpaperStore.AddRecent(path);
            }
            else
            {
                _settings.Style = BackgroundStyle.None();
            }

            Apply();
        };
        stack.Children.Add(box);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var choose = new Button
        {
            Content = "Choose…",
            Style = (Style)Application.Current.Resources["Sd.Button"],
            Padding = new Thickness(10, 3, 10, 3),
        };
        choose.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose a wallpaper",
                Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tif;*.tiff",
            };
            if (dialog.ShowDialog() == true)
            {
                _settings.Style = BackgroundStyle.Wallpaper(dialog.FileName);
                _wallpaperStore.AddRecent(dialog.FileName);
                Apply();
                Refresh();
            }
        };

        var get = new Button
        {
            Content = "Get Wallpapers",
            Style = (Style)Application.Current.Resources["Sd.Button"],
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "Download the built-in wallpaper packs (Frosted Lake, Serene Skies, and more)",
        };
        get.Click += async (_, _) =>
        {
            get.IsEnabled = false;
            get.Content = "Downloading…";
            try
            {
                foreach (var pack in WallpaperPack.BuiltIn)
                {
                    if (!_wallpaperStore.IsInstalled(pack))
                    {
                        await _wallpaperStore.InstallPackAsync(pack);
                    }
                }

                _wallpaperStore.Reload();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not download wallpapers:\n{ex.Message}", "Screenstop",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                get.IsEnabled = true;
                get.Content = "Get Wallpapers";
                Refresh();
            }
        };

        buttons.Children.Add(choose);
        buttons.Children.Add(get);
        stack.Children.Add(buttons);
        return stack;
    }

    // ---------------------------------------------------------------- layout

    private UIElement BuildLayoutSection()
    {
        var section = Section("Layout");
        section.Children.Add(Slider("Padding", () => _settings.Padding, v => _settings.Padding = v, 0, 0.4, FormatPercent));
        section.Children.Add(Slider("Corner radius", () => _settings.CornerRadius, v => _settings.CornerRadius = v, 0, 0.12, FormatPercent));

        section.Children.Add(Picker("Aspect ratio",
            Enum.GetValues<BackgroundAspectRatio>().Select(r => r.Title()).ToArray(),
            (int)_settings.AspectRatio,
            index =>
            {
                _settings.AspectRatio = (BackgroundAspectRatio)index;
                Apply();
            }));

        section.Children.Add(Picker("Position",
            Enum.GetValues<BackgroundAlignment>().Select(a => a.Title()).ToArray(),
            (int)_settings.Alignment,
            index =>
            {
                _settings.Alignment = (BackgroundAlignment)index;
                Apply();
            }));

        return section;
    }

    private UIElement BuildShadowSection()
    {
        var section = Section("Shadow");
        section.Children.Add(Slider("Shadow", () => _settings.Shadow, v => _settings.Shadow = v, 0, 1, FormatPercent));
        section.Children.Add(Picker("Style",
            Enum.GetValues<ShadowStyle>().Select(s => s.Title()).ToArray(),
            (int)_settings.ShadowStyle,
            index =>
            {
                _settings.ShadowStyle = (ShadowStyle)index;
                Apply();
            }));
        return section;
    }

    // ---------------------------------------------------------------- camera

    private static readonly (string Name, Action<CameraSettings> Apply)[] CameraPresets =
    [
        ("Straight on", c => { }),
        ("Tilted", c => { c.TiltXDegrees = 12; c.RotationYDegrees = -18; }),
        ("Perspective left", c => { c.RotationYDegrees = -28; c.TiltXDegrees = 6; }),
        ("Perspective right", c => { c.RotationYDegrees = 28; c.TiltXDegrees = 6; }),
        ("Overhead", c => { c.TiltXDegrees = 32; c.Zoom = 0.92; }),
    ];

    private UIElement BuildCameraSection()
    {
        var section = Section("Camera");

        var presetRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var (name, apply) in CameraPresets)
        {
            var button = new Button
            {
                Content = name,
                Style = (Style)Application.Current.Resources["Sd.Button"],
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 6, 5),
                FontSize = 11,
            };
            button.Click += (_, _) =>
            {
                _settings.Camera = new CameraSettings();
                apply(_settings.Camera);
                Apply();
                Refresh();
            };
            presetRow.Children.Add(button);
        }

        var reset = new Button
        {
            Content = "Reset camera",
            Style = (Style)Application.Current.Resources["Sd.GhostButton"],
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 0, 6),
            FontSize = 11,
        };
        reset.Click += (_, _) =>
        {
            _settings.Camera = new CameraSettings();
            Apply();
            Refresh();
        };

        section.Children.Add(presetRow);
        section.Children.Add(reset);

        // Mirrors the mac camera inspector: upgrade any legacy (v1) projection
        // before applying a change so the sliders always drive the current model.
        UIElement CamSlider(string label, Func<double> get, Action<double> set, double min, double max, Func<double, string> fmt)
            => Slider(label, get, v => { _settings.Camera.UpgradeProjectionIfNeeded(); set(v); }, min, max, fmt);

        section.Children.Add(GroupLabel("Camera angle"));
        section.Children.Add(CamSlider("Tilt X", () => _settings.Camera.TiltXDegrees, v => _settings.Camera.TiltXDegrees = v, -45, 45, FormatDegrees));
        section.Children.Add(CamSlider("Tilt Y", () => _settings.Camera.TiltYDegrees, v => _settings.Camera.TiltYDegrees = v, -45, 45, FormatDegrees));
        section.Children.Add(CamSlider("Roll", () => _settings.Camera.RollDegrees, v => _settings.Camera.RollDegrees = v, -45, 45, FormatDegrees));

        section.Children.Add(GroupLabel("Framing"));
        section.Children.Add(CamSlider("Field of view", () => _settings.Camera.FieldOfViewDegrees, v => _settings.Camera.FieldOfViewDegrees = v, 18, 80, FormatDegrees));
        section.Children.Add(CamSlider("Zoom", () => _settings.Camera.Zoom, v => _settings.Camera.Zoom = v, 0.4, 2.5, v => $"{v:0.00}×"));
        section.Children.Add(CamSlider("Pan X", () => _settings.Camera.PanX, v => _settings.Camera.PanX = v, -0.5, 0.5, v => $"{v * 100:+0;-0;0}%"));
        section.Children.Add(CamSlider("Pan Y", () => _settings.Camera.PanY, v => _settings.Camera.PanY = v, -0.5, 0.5, v => $"{v * 100:+0;-0;0}%"));

        section.Children.Add(GroupLabel("Card rotation"));
        section.Children.Add(CamSlider("Rotate X", () => _settings.Camera.RotationXDegrees, v => _settings.Camera.RotationXDegrees = v, -60, 60, FormatDegrees));
        section.Children.Add(CamSlider("Rotate Y", () => _settings.Camera.RotationYDegrees, v => _settings.Camera.RotationYDegrees = v, -60, 60, FormatDegrees));

        section.Children.Add(new TextBlock
        {
            Text = "Camera perspective previews live and is included when you save or export.",
            FontSize = 11,
            Foreground = Brush("Sd.TextMuted"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        });
        return section;
    }

    // ---------------------------------------------------------------- blur

    private UIElement BuildBlurSection()
    {
        var section = Section("Focus Blur");

        var enable = new CheckBox
        {
            Content = "Enable focus blur",
            IsChecked = _settings.ProgressiveBlur.IsEnabled,
            Margin = new Thickness(0, 0, 0, 6),
        };
        enable.Checked += (_, _) => { _settings.ProgressiveBlur.IsEnabled = true; Apply(); Refresh(); };
        enable.Unchecked += (_, _) => { _settings.ProgressiveBlur.IsEnabled = false; Apply(); Refresh(); };
        section.Children.Add(enable);

        section.Children.Add(Slider("Strength", () => _settings.ProgressiveBlur.Strength, v => _settings.ProgressiveBlur.Strength = v, 0, 48, v => $"{v:0}"));
        section.Children.Add(Slider("Focus size", () => _settings.ProgressiveBlur.FocusSize, v => _settings.ProgressiveBlur.FocusSize = v, 0.05, 1, FormatPercent));
        section.Children.Add(Slider("Falloff", () => _settings.ProgressiveBlur.Falloff, v => _settings.ProgressiveBlur.Falloff = v, 0.05, 1, FormatPercent));

        section.Children.Add(Picker("Mode",
            Enum.GetValues<ProgressiveBlurMode>().Select(m => m.Title()).ToArray(),
            (int)_settings.ProgressiveBlur.Mode,
            index =>
            {
                _settings.ProgressiveBlur.Mode = (ProgressiveBlurMode)index;
                Apply();
            }));

        section.Children.Add(Picker("Applies to",
            Enum.GetValues<ProgressiveBlurEdgeMode>().Select(m => m.Title()).ToArray(),
            (int)_settings.ProgressiveBlur.EdgeMode,
            index =>
            {
                _settings.ProgressiveBlur.EdgeMode = (ProgressiveBlurEdgeMode)index;
                Apply();
            }));

        section.Children.Add(new TextBlock
        {
            Text = "Focus blur is applied when you save or export.",
            FontSize = 11,
            Foreground = Brush("Sd.TextMuted"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        });
        return section;
    }

    // ---------------------------------------------------------------- border

    private UIElement BuildBorderSection()
    {
        var section = Section("Border");

        var enable = new CheckBox
        {
            Content = "Enable border",
            IsChecked = _settings.Border.IsEnabled,
            Margin = new Thickness(0, 0, 0, 6),
        };
        enable.Checked += (_, _) => { _settings.Border.IsEnabled = true; Apply(); Refresh(); };
        enable.Unchecked += (_, _) => { _settings.Border.IsEnabled = false; Apply(); Refresh(); };
        section.Children.Add(enable);

        section.Children.Add(Slider("Thickness", () => _settings.Border.Thickness, v => _settings.Border.Thickness = v, 0, 0.06, FormatPercent));
        section.Children.Add(Slider("Opacity", () => _settings.Border.Opacity, v => _settings.Border.Opacity = v, 0, 1, FormatPercent));

        var swatches = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var (id, title, color) in SwatchLibrary.Swatches)
        {
            bool active = ColorsClose(_settings.Border.Color, color);
            var chip = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.FromRgb(
                    (byte)(color.Red * 255), (byte)(color.Green * 255), (byte)(color.Blue * 255))),
                BorderBrush = active ? Brush("Sd.Accent") : Brush("Sd.Border"),
                BorderThickness = new Thickness(active ? 2.5 : 1),
                Margin = new Thickness(0, 0, 6, 5),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = title,
            };
            chip.MouseLeftButtonDown += (_, _) =>
            {
                _settings.Border.Color = color;
                Apply();
                Refresh();
            };
            swatches.Children.Add(chip);
        }

        section.Children.Add(swatches);
        return section;
    }

    // ---------------------------------------------------------------- watermark

    private UIElement BuildWatermarkSection()
    {
        var section = Section("Watermark");

        var enable = new CheckBox
        {
            Content = "Enable watermark",
            IsChecked = _settings.Watermark.IsEnabled,
            Margin = new Thickness(0, 0, 0, 6),
        };
        enable.Checked += (_, _) => { _settings.Watermark.IsEnabled = true; Apply(); Refresh(); };
        enable.Unchecked += (_, _) => { _settings.Watermark.IsEnabled = false; Apply(); Refresh(); };
        section.Children.Add(enable);

        var text = new TextBox
        {
            Text = _settings.Watermark.Text,
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 0, 0, 6),
        };
        text.LostFocus += (_, _) =>
        {
            _settings.Watermark.Text = text.Text;
            Apply();
        };
        section.Children.Add(text);

        section.Children.Add(Slider("Opacity", () => _settings.Watermark.Opacity, v => _settings.Watermark.Opacity = v, 0, 0.75, FormatPercent));
        section.Children.Add(Slider("Size", () => _settings.Watermark.FontSize, v => _settings.Watermark.FontSize = v, 12, 200, v => $"{v:0}"));
        section.Children.Add(Slider("Density", () => _settings.Watermark.Density, v => _settings.Watermark.Density = v, 2, 12, v => $"{v:0}"));
        section.Children.Add(Slider("Rotation", () => _settings.Watermark.RotationDegrees, v => _settings.Watermark.RotationDegrees = v, 0, 90, FormatDegrees));
        return section;
    }

    // ---------------------------------------------------------------- helpers

    private void Refresh()
    {
        _updating = true;
        try
        {
            _root.Children.Clear();
            _root.Children.Add(BuildPresetSection());
            _root.Children.Add(BuildStyleSection());
            _root.Children.Add(BuildLayoutSection());
            _root.Children.Add(BuildShadowSection());
            _root.Children.Add(BuildCameraSection());
            _root.Children.Add(BuildBlurSection());
            _root.Children.Add(BuildBorderSection());
            _root.Children.Add(BuildWatermarkSection());
            RefreshStyleDetail();
        }
        finally
        {
            _updating = false;
        }
    }

    private void Rebuild(StackPanel host, UIElement replacement)
    {
        _updating = true;
        try
        {
            int index = _root.Children.IndexOf(host);
            _root.Children.RemoveAt(index);
            _root.Children.Insert(index, replacement);
        }
        finally
        {
            _updating = false;
        }
    }

    /// Fills the Background section's detail area with the picker that matches
    /// the active style kind.
    private void RefreshStyleDetail()
    {
        foreach (var child in _root.Children)
        {
            if (child is StackPanel { Tag: StackPanel detail })
            {
                detail.Children.Clear();
                detail.Children.Add(_settings.Style.Kind switch
                {
                    BackgroundStyleKind.Solid => BuildSolidPicker(),
                    BackgroundStyleKind.Gradient => BuildGradientPicker(),
                    BackgroundStyleKind.Wallpaper => BuildWallpaperPicker(),
                    _ => new TextBlock
                    {
                        Text = "No background. Pick Color, Gradient, or Image.",
                        FontSize = 11,
                        Foreground = Brush("Sd.TextMuted"),
                        TextWrapping = TextWrapping.Wrap,
                    },
                });
            }
        }
    }

    private static StackPanel Section(string title)
    {
        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        section.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("Sd.Text"),
            Margin = new Thickness(0, 0, 0, 6),
        });
        return section;
    }

    /// Small sub-heading inside a section (mac InspectorGroupLabel parity).
    private static TextBlock GroupLabel(string text) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeights.Medium,
        Foreground = Brush("Sd.TextMuted"),
        Margin = new Thickness(0, 8, 0, 2),
    };

    private UIElement Slider(
        string label,
        Func<double> get,
        Action<double> set,
        double min,
        double max,
        Func<double, string> format)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });

        var name = new TextBlock
        {
            Text = label,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var slider = new Slider
        {
            Style = (Style)Application.Current.Resources["Sd.Slider"],
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(get(), min, max),
            SmallChange = (max - min) / 100,
            LargeChange = (max - min) / 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var value = new TextBlock
        {
            Text = format(slider.Value),
            FontSize = 11,
            Foreground = Brush("Sd.TextMuted"),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
        };

        slider.ValueChanged += (_, _) =>
        {
            if (_updating)
            {
                return;
            }

            value.Text = format(slider.Value);
            set(slider.Value);
            Apply();
        };

        Grid.SetColumn(name, 0);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(value, 2);
        row.Children.Add(name);
        row.Children.Add(slider);
        row.Children.Add(value);
        return row;
    }

    private UIElement Picker(string label, string[] items, int selectedIndex, Action<int> onChanged)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var name = new TextBlock
        {
            Text = label,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var box = new ComboBox { Style = (Style)Application.Current.Resources["Sd.ComboBox"] };
        foreach (var item in items)
        {
            box.Items.Add(item);
        }

        box.SelectedIndex = Math.Clamp(selectedIndex, 0, items.Length - 1);
        box.SelectionChanged += (_, _) =>
        {
            if (!_updating && box.SelectedIndex >= 0)
            {
                onChanged(box.SelectedIndex);
            }
        };

        Grid.SetColumn(name, 0);
        Grid.SetColumn(box, 1);
        row.Children.Add(name);
        row.Children.Add(box);
        return row;
    }

    private static void SelectComboItemByTag(ComboBox box, string? tag)
    {
        for (int i = 0; i < box.Items.Count; i++)
        {
            if (box.Items[i] is ComboBoxItem item
                && string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedIndex = i;
                return;
            }
        }

        box.SelectedIndex = 0;
    }

    private static bool ColorsClose(RgbaColor a, RgbaColor b) =>
        Math.Abs(a.Red - b.Red) < 0.01
        && Math.Abs(a.Green - b.Green) < 0.01
        && Math.Abs(a.Blue - b.Blue) < 0.01;

    private static string FormatPercent(double value) => $"{Math.Round(value * 100):0}%";

    private static string FormatDegrees(double value) => $"{Math.Round(value):0}°";

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    private static string? Prompt(string title, string label)
    {
        var window = new Window
        {
            Title = title,
            Width = 330,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = Brush("Sd.BgRaised"),
            Foreground = Brush("Sd.Text"),
        };
        var box = new TextBox { Text = "My Background", Margin = new Thickness(0, 7, 0, 12) };
        box.SelectAll();
        var save = new Button
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
            Margin = new Thickness(6, 0, 0, 0),
        };
        string? result = null;
        save.Click += (_, _) => { result = box.Text; window.DialogResult = true; };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(new TextBlock { Text = label, FontSize = 12 });
        content.Children.Add(box);
        content.Children.Add(buttons);
        window.Content = content;
        return window.ShowDialog() == true ? result : null;
    }
}
