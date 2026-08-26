using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Screendrop.Core.Annotations;
using Screendrop.Rendering;
using SkiaSharp;

namespace Screendrop.App.Editor;

/// <summary>
/// The annotation editor window: themed toolbar + interactive canvas + inline
/// text editing. Opens on a captured image, lets the user annotate it, and
/// hands the composited result back to the caller (Save overwrites the staged
/// file). Visual chrome comes from the app-level resource dictionary.
/// </summary>
internal sealed class AnnotationEditorWindow : Window
{
    private readonly AnnotationCanvas _canvas = new();
    private readonly StackPanel _toolButtons = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _colorButtons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
    private readonly Button _undoButton;
    private readonly Button _redoButton;
    private readonly TextBlock _zoomLabel;
    private readonly TextBox _textBox;
    private readonly Border _textOverlay;
    private readonly TextBlock _hintLabel = new()
    {
        Foreground = FindAppBrush("Sd.TextMuted"),
        FontSize = 11,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Grid _canvasHost = new();
    private readonly Dictionary<AnnotationTool, ToggleButton> _toolToggles = new();
    private readonly string _sourcePath;
    private NormalizedPoint _textAnchor;
    private bool _textSessionActive;

    /// Raised after edits are saved to the sidecar, with the image path.
    public event Action<string>? Saved;

    public AnnotationEditorWindow(string imagePath)
    {
        _sourcePath = imagePath;
        Title = "Screendrop — Annotate";
        Width = 1100;
        Height = 760;
        MinWidth = 720;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = FindAppBrush("Sd.Bg");

        if (!File.Exists(imagePath) || !_canvas.LoadImage(imagePath))
        {
            MessageBox.Show(this, "The image could not be loaded.", "Screendrop", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            // Non-destructive: resume any previous edits stored in the sidecar.
            _canvas.Model.Load(AnnotationDocument.Load(imagePath));
        }

        _undoButton = MakeIconButton(UiGlyph.Undo, "Undo (Ctrl+Z)", (_, _) => { CommitTextSession(); _canvas.Model.Undo(); });
        _redoButton = MakeIconButton(UiGlyph.Redo, "Redo (Ctrl+Y)", (_, _) => { CommitTextSession(); _canvas.Model.Redo(); });
        _canvas.Model.Changed += UpdateUndoState;
        UpdateUndoState();

        _zoomLabel = new TextBlock
        {
            Text = ZoomText(_canvas.ZoomLevel),
            Foreground = FindAppBrush("Sd.Text"),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 46,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0),
            FontSize = 12,
        };
        _canvas.ZoomChanged += zoom => _zoomLabel.Text = ZoomText(zoom);

        var toolbar = BuildToolbar();

        _textBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            MinWidth = 120,
            MaxWidth = 420,
            Padding = new Thickness(4),
            Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
            BorderBrush = FindAppBrush("Sd.Accent"),
            BorderThickness = new Thickness(1.5),
            Visibility = Visibility.Collapsed,
        };
        _textBox.KeyDown += OnTextBoxKeyDown;
        _textBox.LostFocus += (_, _) => CommitTextSession();

        _textOverlay = new Border
        {
            Child = _textBox,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        // Floating hint chip in the lower-left corner of the image area.
        var hintChip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 17, 24, 39)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(12, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = _hintLabel,
            IsHitTestVisible = false,
        };

        _canvasHost.Children.Add(_canvas);
        _canvasHost.Children.Add(_textOverlay);
        _canvasHost.Children.Add(hintChip);

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(_canvasHost);
        Content = root;

        _canvas.TextSessionRequested += OpenTextSession;

        KeyDown += OnWindowKeyDown;
        Closed += (_, _) => _canvas.ReleaseResources();
    }

    /// Builds the top chrome: tool strip | colors | history | zoom | commit.
    private UIElement BuildToolbar()
    {
        var toolsPanel = new Border
        {
            Background = FindAppBrush("Sd.Panel"),
            BorderBrush = FindAppBrush("Sd.BorderSoft"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4, 3, 4, 3),
            Child = _toolButtons,
            VerticalAlignment = VerticalAlignment.Center,
        };

        BuildToolButtons();
        BuildColorButtons();

        var exportButton = MakeTextButton("Export…", "Export a flattened PNG copy", OnExport);
        var saveButton = MakeSaveButton();
        var closeButton = MakeIconButton(UiGlyph.Close, "Close", (_, _) => Close());

        var zoomOutButton = MakeTextButton("−", "Zoom out (Ctrl+− or mouse wheel)", (_, _) => _canvas.ZoomOut());
        var zoomInButton = MakeTextButton("+", "Zoom in (Ctrl++ or mouse wheel)", (_, _) => _canvas.ZoomIn());
        var fitButton = MakeTextButton("Fit", "Fit to window (Ctrl+0)", (_, _) => _canvas.ResetZoom());
        zoomOutButton.Margin = new Thickness(0, 0, 2, 0);
        zoomInButton.Margin = new Thickness(0, 0, 2, 0);

        // Right-aligned commit cluster stays pinned even when tools wrap.
        var rightCluster = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        foreach (var element in new UIElement[] { Sep(), exportButton, saveButton, Sep(), zoomOutButton, _zoomLabel, zoomInButton, fitButton, Sep(), _undoButton, _redoButton, closeButton })
        {
            rightCluster.Children.Add(element);
        }

        var leftCluster = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        leftCluster.Children.Add(toolsPanel);
        leftCluster.Children.Add(_colorButtons);

        var dock = new DockPanel
        {
            Margin = new Thickness(10, 8, 10, 8),
            LastChildFill = false,
        };
        DockPanel.SetDock(rightCluster, Dock.Right);
        dock.Children.Add(rightCluster);
        dock.Children.Add(leftCluster);
        return dock;
    }

    private void BuildToolButtons()
    {
        foreach (var (tool, icon, shortcut) in new[]
        {
            (AnnotationTool.Select, ToolIcon.SelectCursor(), "V"),
            (AnnotationTool.Rectangle, ToolIcon.Rectangle(), "R"),
            (AnnotationTool.Ellipse, ToolIcon.Ellipse(), "E"),
            (AnnotationTool.Arrow, ToolIcon.Arrow(), "A"),
            (AnnotationTool.Freehand, ToolIcon.Freehand(), "D"),
            (AnnotationTool.Text, ToolIcon.Text(), "T"),
            (AnnotationTool.NumberedCircle, ToolIcon.StepMarker(), "N"),
            (AnnotationTool.Pixelate, ToolIcon.Pixelate(), "P"),
            (AnnotationTool.Blur, ToolIcon.Blur(), "B"),
        })
        {
            var toggle = new ToggleButton
            {
                Style = (Style)FindAppResource("Sd.ToolToggle"),
                Content = icon,
                Margin = new Thickness(1, 0, 1, 0),
                ToolTip = $"{tool.Title()} ({shortcut})",
                Tag = tool,
            };
            toggle.Click += (_, _) => ActivateTool(tool);
            _toolToggles[tool] = toggle;
            _toolButtons.Children.Add(toggle);
        }

        ActivateTool(AnnotationTool.Rectangle);
    }

    private void BuildColorButtons()
    {
        foreach (var color in AnnotationColor.Palette)
        {
            var swatch = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(color.A255, color.R255, color.G255, color.B255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
            };
            swatch.MouseLeftButtonDown += (_, _) =>
            {
                _canvas.ActiveColor = color;
                if (_canvas.Model.Selected is { } selected && selected.Tool.SupportsColor())
                {
                    _canvas.Model.BeginBatch();
                    selected.Color = color;
                    _canvas.Model.EndBatch();
                }

                HighlightSelectedColor();
            };
            swatch.Tag = color;
            _colorButtons.Children.Add(swatch);
        }

        HighlightSelectedColor();
    }

    private void HighlightSelectedColor()
    {
        foreach (var child in _colorButtons.Children)
        {
            if (child is Border swatch && swatch.Tag is AnnotationColor color)
            {
                bool selected = color == _canvas.ActiveColor;
                swatch.BorderThickness = new Thickness(selected ? 2.5 : 1);
                swatch.BorderBrush = selected
                    ? FindAppBrush("Sd.Accent")
                    : new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
            }
        }
    }

    private void ActivateTool(AnnotationTool tool)
    {
        CommitTextSession();
        _canvas.SetTool(tool);
        foreach (var (key, toggle) in _toolToggles)
        {
            toggle.IsChecked = key == tool;
        }

        _hintLabel.Text = HintFor(tool);
    }

    private static string HintFor(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Select => "Click an annotation to select it · Del removes · Ctrl+Z undoes",
        AnnotationTool.Rectangle => "Drag to draw a rectangle",
        AnnotationTool.Ellipse => "Drag to draw an ellipse",
        AnnotationTool.Arrow => "Drag to draw an arrow",
        AnnotationTool.Freehand => "Draw freely with the mouse",
        AnnotationTool.Text => "Click where the text should start",
        AnnotationTool.NumberedCircle => "Click to place the next step marker",
        AnnotationTool.Pixelate => "Drag over an area to pixelate it",
        AnnotationTool.Blur => "Drag over an area to blur it",
        _ => string.Empty,
    };

    private static Border Sep() => new() { Style = (Style)FindAppResource("Sd.VSeparator"), VerticalAlignment = VerticalAlignment.Center };

    private Button MakeSaveButton()
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(new TextBlock
        {
            Text = UiGlyph.Save,
            FontFamily = UiGlyph.Font,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });
        stack.Children.Add(new TextBlock { Text = "Save", VerticalAlignment = VerticalAlignment.Center });

        var button = new Button
        {
            Style = (Style)FindAppResource("Sd.AccentButton"),
            Content = stack,
            ToolTip = "Save edits to the sidecar (Ctrl+S)",
            Padding = new Thickness(14, 5, 14, 5),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 0, 0, 0),
        };
        button.Click += OnSave;
        return button;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        CommitTextSession();

        if (_canvas.FullBitmap is null)
        {
            return;
        }

        try
        {
            _canvas.Model.ToDocument().Save(_sourcePath);
            Saved?.Invoke(_sourcePath);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save the edits: {ex.Message}", "Screendrop", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// Exports a flattened copy (image + annotations baked in) to a location
    /// the user chooses. The original capture and its sidecar stay intact.
    private void OnExport(object sender, RoutedEventArgs e)
    {
        CommitTextSession();

        if (_canvas.FullBitmap is null)
        {
            return;
        }

        try
        {
            byte[] bytes = EncodePng();

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export annotated image",
                Filter = "PNG image|*.png",
                FileName = $"screendrop-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            File.WriteAllBytes(dialog.FileName, bytes);

            // Keep the edits with the export too, so re-opening it resumes
            // where the user left off.
            _canvas.Model.ToDocument().Save(dialog.FileName);

            Saved?.Invoke(dialog.FileName);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not export the image: {ex.Message}", "Screendrop", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private byte[] EncodePng()
    {
        using var composited = AnnotationRenderer.Render(_canvas.FullBitmap!, _canvas.Model.ToDocument());
        using var data = composited.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        return stream.ToArray();
    }

    private static Button MakeIconButton(string glyph, string tooltip, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Style = (Style)FindAppResource("Sd.GhostButton"),
            Content = new TextBlock
            {
                Text = glyph,
                FontFamily = UiGlyph.Font,
                FontSize = 14,
            },
            ToolTip = tooltip,
            Padding = new Thickness(7, 4, 7, 4),
            Margin = new Thickness(0, 0, 2, 0),
        };
        button.Click += onClick;
        return button;
    }

    private static Button MakeTextButton(string label, string tooltip, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Style = (Style)FindAppResource("Sd.Button"),
            Content = label,
            ToolTip = tooltip,
        };
        button.Click += onClick;
        return button;
    }

    private static ResourceDictionary AppResources => Application.Current.Resources;

    private static object FindAppResource(string key) => AppResources[key];

    private static Brush FindAppBrush(string key) => (Brush)AppResources[key];

    private static string ZoomText(double zoom) => $"{Math.Round(zoom * 100):0}%";

    private void UpdateUndoState()
    {
        _undoButton.IsEnabled = _canvas.Model.CanUndo;
        _redoButton.IsEnabled = _canvas.Model.CanRedo;
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (_textSessionActive)
        {
            return; // the text box handles its own keys
        }

        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (ctrl && e.Key == Key.Z)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                _canvas.Model.Redo();
            }
            else
            {
                _canvas.Model.Undo();
            }

            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.Y)
        {
            _canvas.Model.Redo();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.S)
        {
            OnSave(sender, e);
            e.Handled = true;
        }
        else if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add))
        {
            _canvas.ZoomIn();
            e.Handled = true;
        }
        else if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract))
        {
            _canvas.ZoomOut();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.D0)
        {
            _canvas.ResetZoom();
            e.Handled = true;
        }
        else if (!ctrl && TryHandleToolShortcut(e.Key))
        {
            e.Handled = true;
        }
        else if (e.Key is Key.Delete or Key.Back && _canvas.Model.Selected is { } selected)
        {
            _canvas.Model.Remove(selected);
            if (selected.Tool == AnnotationTool.NumberedCircle)
            {
                _canvas.Model.RenumberMarkers();
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _canvas.Model.Select(null);
            e.Handled = true;
        }
    }

    /// Single-letter tool switching (V/R/E/A/D/T/N/P/B).
    private bool TryHandleToolShortcut(Key key)
    {
        AnnotationTool? tool = key switch
        {
            Key.V => AnnotationTool.Select,
            Key.R => AnnotationTool.Rectangle,
            Key.E => AnnotationTool.Ellipse,
            Key.A => AnnotationTool.Arrow,
            Key.D => AnnotationTool.Freehand,
            Key.T => AnnotationTool.Text,
            Key.N => AnnotationTool.NumberedCircle,
            Key.P => AnnotationTool.Pixelate,
            Key.B => AnnotationTool.Blur,
            _ => null,
        };

        if (tool is null)
        {
            return false;
        }

        ActivateTool(tool.Value);
        return true;
    }

    private void OpenTextSession(Point canvasDip, NormalizedPoint anchor)
    {
        _textAnchor = anchor;
        _textSessionActive = true;
        _textBox.Text = string.Empty;
        _textOverlay.Visibility = Visibility.Visible;
        _textBox.Visibility = Visibility.Visible;
        // The overlay lives in a Grid: position via margin (left/top aligned).
        _textOverlay.Margin = new Thickness(canvasDip.X, canvasDip.Y, 0, 0);
        _textBox.Focus();
    }

    private void CommitTextSession()
    {
        if (!_textSessionActive)
        {
            return;
        }

        _textSessionActive = false;
        string text = _textBox.Text;
        _textOverlay.Visibility = Visibility.Collapsed;
        _textBox.Visibility = Visibility.Collapsed;
        _textBox.Text = string.Empty;

        if (!string.IsNullOrWhiteSpace(text))
        {
            _canvas.AddText(_textAnchor, text);
        }
    }

    private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _textSessionActive = false;
            _textOverlay.Visibility = Visibility.Collapsed;
            _textBox.Visibility = Visibility.Collapsed;
            _textBox.Text = string.Empty;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            CommitTextSession();
            e.Handled = true;
        }
    }
}

/// Segoe MDL2/Fluent glyph constants shared by app windows.
internal static class UiGlyph
{
    public static readonly FontFamily Font = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public const string Undo = "\uE7A7";
    public const string Redo = "\uE7A6";
    public const string Save = "\uE74E";
    public const string Close = "\uE711";
}

/// Small vector icons for the annotation tools (stroke follows the host
/// control's Foreground so hover/checked tinting stays automatic).
internal static class ToolIcon
{
    private static Binding ForegroundOfHost => new("Foreground")
    {
        RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ToggleButton), 1),
    };

    private static System.Windows.Shapes.Path Stroked(string data, double thickness = 1.6)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        path.SetBinding(System.Windows.Shapes.Path.StrokeProperty, ForegroundOfHost);
        return path;
    }

    private static System.Windows.Shapes.Path Filled(string data)
    {
        var path = new System.Windows.Shapes.Path { Data = Geometry.Parse(data) };
        path.SetBinding(System.Windows.Shapes.Path.FillProperty, ForegroundOfHost);
        return path;
    }

    private static FrameworkElement Host(int width, int height, UIElement child) => new Canvas
    {
        Width = width,
        Height = height,
        Children = { child },
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    public static FrameworkElement SelectCursor() => Host(16, 16, Filled(
        "M3,1 L3,13.2 L6.3,10.3 L8.3,14.6 L10.6,13.6 L8.6,9.4 L12.8,9 Z"));

    public static FrameworkElement Rectangle() => Host(16, 16, Stroked("M2,3.5 L14,3.5 L14,12.5 L2,12.5 Z"));

    public static FrameworkElement Ellipse() => Host(16, 16, StrokedEllipse(12.5, 9.5));

    private static System.Windows.Shapes.Ellipse StrokedEllipse(double width, double height)
    {
        var ellipse = new System.Windows.Shapes.Ellipse
        {
            Width = width,
            Height = height,
            StrokeThickness = 1.6,
        };
        ellipse.SetBinding(System.Windows.Shapes.Ellipse.StrokeProperty, ForegroundOfHost);
        return ellipse;
    }

    public static FrameworkElement Arrow() => Host(16, 16, Stroked("M2.5,13.5 L12,4 M6.5,3.5 L12.5,3.5 L12.5,9.5"));

    public static FrameworkElement Freehand() => Host(16, 16, Stroked(
        "M1.5,11.5 C3.5,5 6,5 8,8.5 C9.5,11.2 11.5,10.8 14.5,4.5"));

    public static FrameworkElement Text() => Host(16, 16, Stroked("M3,3.5 L13,3.5 M8,3.5 L8,13"));

    public static FrameworkElement StepMarker()
    {
        var ring = new System.Windows.Shapes.Ellipse
        {
            Width = 12,
            Height = 12,
            StrokeThickness = 1.6,
        };
        ring.SetBinding(System.Windows.Shapes.Ellipse.StrokeProperty, ForegroundOfHost);

        var digit = new TextBlock
        {
            Text = "1",
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        return Host(16, 16, new Grid
        {
            Width = 14,
            Height = 14,
            Children = { ring, digit },
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
    }

    public static FrameworkElement Pixelate() => Host(16, 16, Filled(
        "M0,0 H4.5 V4.5 H0 Z M11.5,0 H16 V4.5 H11.5 Z M5.75,5.75 H10.25 V10.25 H5.75 Z M0,11.5 H4.5 V16 H0 Z M11.5,11.5 H16 V16 H11.5 Z"));

    public static FrameworkElement Blur() => Host(16, 16, new Canvas
    {
        Width = 16,
        Height = 16,
        Children =
        {
            Ring(14, 0.28),
            Ring(9.5, 0.55),
            Ring(5, 0.95),
        },
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    });

    private static System.Windows.Shapes.Ellipse Ring(double diameter, double opacity)
    {
        var ring = StrokedEllipse(diameter, diameter);
        ring.Opacity = opacity;
        return ring;
    }
}
