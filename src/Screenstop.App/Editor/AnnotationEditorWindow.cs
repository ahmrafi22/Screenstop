using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Screenstop.Core.Annotations;
using Screenstop.Rendering;
using SkiaSharp;

namespace Screenstop.App.Editor;

/// The annotation editor window: toolbar + interactive canvas + inline text
/// editing. Opens on a captured image, lets the user annotate it, and hands
/// the composited result back to the caller (Save overwrites the staged file).
internal sealed class AnnotationEditorWindow : Window
{
    private readonly AnnotationCanvas _canvas = new();
    private readonly StackPanel _toolButtons = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _colorButtons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
    private readonly Button _undoButton;
    private readonly Button _redoButton;
    private readonly TextBox _textBox;
    private readonly Border _textOverlay;
    private readonly Grid _canvasHost = new();
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
        MinWidth = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(17, 24, 39));

        if (!File.Exists(imagePath) || !_canvas.LoadImage(imagePath))
        {
            MessageBox.Show(this, "The image could not be loaded.", "Screenstop", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            // Non-destructive: resume any previous edits stored in the sidecar.
            _canvas.Model.Load(AnnotationDocument.Load(imagePath));
        }

        _undoButton = MakeButton("Undo", OnUndo);
        _redoButton = MakeButton("Redo", OnRedo);
        _canvas.Model.Changed += UpdateUndoState;
        UpdateUndoState();

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 8, 10, 8),
        };
        BuildToolButtons();
        BuildColorButtons();
        toolbar.Children.Add(_toolButtons);
        toolbar.Children.Add(_colorButtons);
        toolbar.Children.Add(_undoButton);
        toolbar.Children.Add(_redoButton);

        var saveButton = MakeButton("Save", OnSave);
        saveButton.FontWeight = FontWeights.Bold;
        saveButton.Margin = new Thickness(12, 0, 0, 0);
        toolbar.Children.Add(saveButton);

        var exportButton = MakeButton("Export…", OnExport);
        exportButton.Margin = new Thickness(6, 0, 0, 0);
        toolbar.Children.Add(exportButton);

        var closeButton = MakeButton("Close", (_, _) => Close());
        closeButton.Margin = new Thickness(6, 0, 0, 0);
        toolbar.Children.Add(closeButton);

        _textBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            MinWidth = 120,
            MaxWidth = 420,
            Padding = new Thickness(4),
            Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
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

        _canvasHost.Children.Add(_canvas);
        _canvasHost.Children.Add(_textOverlay);

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(_canvasHost);
        Content = root;

        _canvas.TextSessionRequested += OpenTextSession;
        _canvas.SetTool(AnnotationTool.Rectangle);

        KeyDown += OnWindowKeyDown;
        Closed += (_, _) => _canvas.ReleaseResources();
    }

    private void BuildToolButtons()
    {
        foreach (var tool in new[]
        {
            AnnotationTool.Select,
            AnnotationTool.Rectangle,
            AnnotationTool.Ellipse,
            AnnotationTool.Arrow,
            AnnotationTool.Freehand,
            AnnotationTool.Text,
            AnnotationTool.NumberedCircle,
            AnnotationTool.Pixelate,
            AnnotationTool.Blur,
        })
        {
            var button = MakeButton(tool.Title(), null!);
            button.Tag = tool;
            button.Margin = new Thickness(0, 0, 4, 0);
            button.Click += (_, _) => SelectTool(tool, button);
            _toolButtons.Children.Add(button);
        }
    }

    private void BuildColorButtons()
    {
        foreach (var color in AnnotationColor.Palette)
        {
            var swatch = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.FromArgb(color.A255, color.R255, color.G255, color.B255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 5, 0),
                Cursor = Cursors.Hand,
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
                    ? new SolidColorBrush(Color.FromRgb(59, 130, 246))
                    : new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
            }
        }
    }

    private void SelectTool(AnnotationTool tool, Button clicked)
    {
        CommitTextSession();
        _canvas.SetTool(tool);
        foreach (var child in _toolButtons.Children)
        {
            if (child is Button button)
            {
                bool active = ReferenceEquals(button, clicked);
                button.Background = active
                    ? new SolidColorBrush(Color.FromRgb(59, 130, 246))
                    : new SolidColorBrush(Color.FromRgb(55, 65, 81));
            }
        }
    }

    private static Button MakeButton(string label, RoutedEventHandler? onClick)
    {
        var button = new Button
        {
            Content = label,
            FontSize = 12,
            Padding = new Thickness(10, 5, 10, 5),
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(55, 65, 81)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(75, 85, 99)),
            BorderThickness = new Thickness(1),
        };
        if (onClick is not null)
        {
            button.Click += onClick;
        }

        return button;
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        CommitTextSession();
        _canvas.Model.Undo();
    }

    private void OnRedo(object sender, RoutedEventArgs e)
    {
        CommitTextSession();
        _canvas.Model.Redo();
    }

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

    /// Saves the edits non-destructively: the annotation document goes to the
    /// `.screenstop` sidecar; the captured image itself is never touched.
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
}
