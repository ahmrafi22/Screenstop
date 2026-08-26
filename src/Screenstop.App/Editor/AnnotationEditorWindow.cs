using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Screenstop.Core.Annotations;
using Screenstop.Rendering;
using SkiaSharp;

namespace Screenstop.App.Editor;

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
    private readonly TextBlock _cropLabel = new()
    {
        Foreground = FindAppBrush("Sd.Text"),
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Border _cropConfirmBar;
    private NormalizedRect? _pendingCrop;
    private readonly string _sourcePath;
    private NormalizedPoint _textAnchor;
    private bool _textSessionActive;

    /// Raised after edits are saved to the sidecar, with the image path.
    public event Action<string>? Saved;

    public AnnotationEditorWindow(string imagePath)
    {
        _sourcePath = imagePath;
        Title = "Screenstop — Annotate";
        Width = 1100;
        Height = 760;
        MinWidth = 720;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = FindAppBrush("Sd.Bg");
        Foreground = FindAppBrush("Sd.Text");

        if (!File.Exists(imagePath) || !_canvas.LoadImage(imagePath))
        {
            MessageBox.Show(this, "The image could not be loaded.", "Screenstop", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            Background = new SolidColorBrush(Color.FromArgb(200, 41, 41, 41)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(12, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = _hintLabel,
            IsHitTestVisible = false,
        };

        // Crop confirmation bar: appears after dragging a region.
        var applyCropButton = MakeTextButton("Apply", "Cut the image to this region", (_, _) => ApplyPendingCrop());
        var cancelCropButton = MakeTextButton("Cancel", "Discard this crop selection", (_, _) => DiscardPendingCrop());
        var cropButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        cropButtons.Children.Add(applyCropButton);
        cropButtons.Children.Add(cancelCropButton);

        var cropRow = new StackPanel { Orientation = Orientation.Horizontal };
        cropRow.Children.Add(_cropLabel);
        cropRow.Children.Add(cropButtons);

        _cropConfirmBar = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 41, 41, 41)),
            BorderBrush = FindAppBrush("Sd.Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 16),
            Child = cropRow,
            Visibility = Visibility.Collapsed,
        };

        _canvasHost.Children.Add(_canvas);
        _canvasHost.Children.Add(_textOverlay);
        _canvasHost.Children.Add(hintChip);
        _canvasHost.Children.Add(_cropConfirmBar);

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(_canvasHost);
        Content = root;

        _canvas.TextSessionRequested += OpenTextSession;
        _canvas.CropCommitted += OnCropCommitted;

        KeyDown += OnWindowKeyDown;
        Closed += (_, _) => _canvas.ReleaseResources();
    }

    private void OnStartCrop(object sender, RoutedEventArgs e)
    {
        CommitTextSession();
        _pendingCrop = null;
        UpdateCropBar();
        _canvas.EnterCropMode();
        _hintLabel.Text = "Drag over the area to keep · Esc cancels";
    }

    private void OnCropCommitted(NormalizedRect rect)
    {
        _pendingCrop = rect;
        double widthPx = Math.Round(rect.Width * (_canvas.FullBitmap?.Width ?? 0));
        double heightPx = Math.Round(rect.Height * (_canvas.FullBitmap?.Height ?? 0));
        _cropLabel.Text = $"Crop to {widthPx:0} × {heightPx:0} px?";
        UpdateCropBar();
    }

    private void ApplyPendingCrop()
    {
        if (_pendingCrop is not { } rect)
        {
            return;
        }

        _canvas.ApplyCrop(rect);
        _pendingCrop = null;
        UpdateCropBar();
        ActivateTool(_canvas.ActiveTool);
    }

    private void DiscardPendingCrop()
    {
        _pendingCrop = null;
        _canvas.CancelCrop();
        UpdateCropBar();
        ActivateTool(_canvas.ActiveTool);
    }

    private void UpdateCropBar()
    {
        bool visible = _pendingCrop is not null || _canvas.IsCropping;
        _cropConfirmBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (_pendingCrop is null && _canvas.IsCropping)
        {
            _cropLabel.Text = "Drag over the area to keep";
        }
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
        var cropButton = MakeTextButton("Crop", "Crop the image (drag a region, then Apply)", OnStartCrop);

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
        foreach (var element in new UIElement[] { Sep(), cropButton, exportButton, saveButton, Sep(), zoomOutButton, _zoomLabel, zoomInButton, fitButton, Sep(), _undoButton, _redoButton, closeButton })
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
            (AnnotationTool.Select, ToolIcon.SelectCursor(), "H"),
            (AnnotationTool.Rectangle, ToolIcon.Rectangle(), "R"),
            (AnnotationTool.FilledRectangle, ToolIcon.FilledRectangle(), "F"),
            (AnnotationTool.Ellipse, ToolIcon.Ellipse(), "O"),
            (AnnotationTool.Line, ToolIcon.Line(), "L"),
            (AnnotationTool.Arrow, ToolIcon.Arrow(), "A"),
            (AnnotationTool.Freehand, ToolIcon.Freehand(), "D"),
            (AnnotationTool.NumberedCircle, ToolIcon.StepMarker(), "1"),
            (AnnotationTool.Text, ToolIcon.Text(), "T"),
            (AnnotationTool.Highlight, ToolIcon.Highlight(), "G"),
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
        AnnotationTool.FilledRectangle => "Drag to draw a solid rectangle",
        AnnotationTool.Ellipse => "Drag to draw an ellipse",
        AnnotationTool.Line => "Drag to draw a straight line",
        AnnotationTool.Arrow => "Drag to draw an arrow",
        AnnotationTool.Freehand => "Draw freely with the mouse",
        AnnotationTool.Highlight => "Drag over text to highlight it",
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
            MessageBox.Show(this, $"Could not save the edits: {ex.Message}", "Screenstop", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                FileName = $"screenstop-{DateTime.Now:yyyyMMdd-HHmmss}.png",
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
            MessageBox.Show(this, $"Could not export the image: {ex.Message}", "Screenstop", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        else if (!ctrl && !_canvas.IsCropping && TryHandleToolShortcut(e.Key))
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
            if (_pendingCrop is not null || _canvas.IsCropping)
            {
                DiscardPendingCrop();
            }
            else
            {
                _canvas.Model.Select(null);
            }

            e.Handled = true;
        }
    }

    /// Single-letter tool switching (V/R/E/A/D/T/N/P/B).
    private bool TryHandleToolShortcut(Key key)
    {
        AnnotationTool? tool = key switch
        {
            Key.H => AnnotationTool.Select,
            Key.R => AnnotationTool.Rectangle,
            Key.F => AnnotationTool.FilledRectangle,
            Key.O => AnnotationTool.Ellipse,
            Key.L => AnnotationTool.Line,
            Key.A => AnnotationTool.Arrow,
            Key.D => AnnotationTool.Freehand,
            Key.T => AnnotationTool.Text,
            Key.D1 => AnnotationTool.NumberedCircle,
            Key.G => AnnotationTool.Highlight,
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

    public static FrameworkElement SelectCursor() => Host(16, 16, new TextBlock
    {
        Text = "\uE7A9", // Segoe touch-pointer: stand-in for mac's hand.point.up.left
        FontFamily = UiGlyph.Font,
        FontSize = 15,
    });

    public static FrameworkElement Rectangle() => Host(16, 16, Stroked("M2,3.5 L14,3.5 L14,12.5 L2,12.5 Z"));

    public static FrameworkElement FilledRectangle() => Host(16, 16, Filled("M2,3.5 L14,3.5 L14,12.5 L2,12.5 Z"));

    /// Mac highlight icon (square.dashed.inset.filled): dashed outline with a
    /// solid bar inset inside it.
    public static FrameworkElement Highlight()
    {
        var outer = Stroked("M2,3 L2,13 M14,3 L14,13 M2,3 L5.5,3 M10.5,3 L14,3 M2,13 L5.5,13 M10.5,13 L14,13");
        var inner = Filled("M4.5,6.5 L11.5,6.5 L11.5,9.5 L4.5,9.5 Z");
        return Host(16, 16, new Canvas
        {
            Width = 16,
            Height = 16,
            Children = { outer, inner },
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
    }

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

    public static FrameworkElement Line() => Host(16, 16, Stroked("M2.5,13.5 L13.5,2.5"));

    public static FrameworkElement Freehand() => Host(16, 16, Stroked(
        "M1.5,11.5 C3.5,5 6,5 8,8.5 C9.5,11.2 11.5,10.8 14.5,4.5"));

    /// Mac text icon (textformat): a letterform.
    public static FrameworkElement Text() => Host(16, 16, Filled(
        "M6.9,2.8 L9.1,2.8 L13.2,13.2 L11.1,13.2 L10.2,10.7 L5.8,10.7 L4.9,13.2 L2.8,13.2 Z M9.55,8.9 L8,4.7 L6.45,8.9 Z"));

    /// Mac numberedCircle icon is 1.circle.fill: a filled disc with the digit
    /// knocked out in the chrome ground color.
    public static FrameworkElement StepMarker()
    {
        var disc = new System.Windows.Shapes.Ellipse
        {
            Width = 13,
            Height = 13,
        };
        disc.SetBinding(System.Windows.Shapes.Ellipse.FillProperty, ForegroundOfHost);

        var digit = new TextBlock
        {
            Text = "1",
            FontSize = 8.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x20)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        return Host(16, 16, new Grid
        {
            Width = 14,
            Height = 14,
            Children = { disc, digit },
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
    }

    /// Mac pixelate icon (app.background.dotted): a 3x3 grid of dots.
    public static FrameworkElement Pixelate() => Host(16, 16, new Canvas
    {
        Width = 16,
        Height = 16,
        Children =
        {
            Dot(3, 3), Dot(8, 3), Dot(13, 3),
            Dot(3, 8), Dot(8, 8), Dot(13, 8),
            Dot(3, 13), Dot(8, 13), Dot(13, 13),
        },
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    });

    private static System.Windows.Shapes.Ellipse Dot(double cx, double cy)
    {
        var dot = new System.Windows.Shapes.Ellipse { Width = 2.4, Height = 2.4 };
        Canvas.SetLeft(dot, cx - 1.2);
        Canvas.SetTop(dot, cy - 1.2);
        return dot;
    }

    /// Mac blur icon (drop.fill): a water droplet.
    public static FrameworkElement Blur() => Host(16, 16, Filled(
        "M8,1.6 C8,1.6 12.8,7.2 12.8,10.4 C12.8,13.1 10.65,15 8,15 C5.35,15 3.2,13.1 3.2,10.4 C3.2,7.2 8,1.6 8,1.6 Z"));
}
