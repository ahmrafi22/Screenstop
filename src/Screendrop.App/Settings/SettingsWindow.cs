using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Screendrop.App.Infrastructure;
using Screendrop.Core.Background;
using Screendrop.Core.Settings;

namespace Screendrop.App.Settings;

/// Preferences window rebuilt to match the mac Settings layout (sidebar +
/// grouped inset panes) with every customizable setting ported from iOS.
/// Edits a copy of the settings and applies them atomically on Save through
/// the onSave callback, so the app layer decides what to reload.
internal sealed class SettingsWindow : Window
{
    private enum Pane { General, Screenshots, Overlay, About }

    private readonly ScreendropSettings _settings;
    private readonly Action<ScreendropSettings> _onSave;
    private readonly OverlayCardLayoutStore _cardLayoutStore = new();

    private readonly ContentControl _contentHost;
    private readonly Dictionary<Pane, UIElement> _panes = new();
    private Pane _currentPane = Pane.General;

    // General
    private readonly TextBlock _exportFolderDisplay;
    private string _exportDirectoryPath;
    private readonly SwitchToggle _saveButtonUsesFolder;
    private readonly SwitchToggle _launchAtLogin;
    private readonly SwitchToggle _playSounds;
    private readonly SwitchToggle _showTrayIcon;
    private readonly SwitchToggle _includeAppWindows;

    // Screenshots
    private readonly HotkeyRecorderBox _fullscreenHotkey;
    private readonly HotkeyRecorderBox _windowHotkey;
    private readonly HotkeyRecorderBox _areaHotkey;
    private readonly ComboBox _selfTimer;
    private readonly SwitchToggle _lowResPreview;
    private readonly SwitchToggle _afterShowOverlay;
    private readonly SwitchToggle _afterCopy;
    private readonly SwitchToggle _afterSave;
    private readonly SwitchToggle _afterAnnotate;
    private readonly ComboBox _format;
    private readonly Slider _quality;
    private readonly TextBlock _qualityLabel;
    private readonly UIElement _qualityRow;
    private readonly TextBox _fileNamePattern;

    // Overlay
    private readonly ComboBox _previewPosition;
    private readonly ComboBox _autoClose;
    private readonly SwitchToggle _closeAfterDragging;
    private readonly Dictionary<string, ComboBox> _cardSlots = new();

    public SettingsWindow(ScreendropSettings settings, Action<ScreendropSettings> onSave)
    {
        _settings = settings;
        _onSave = onSave;
        _exportDirectoryPath = settings.ExportDirectoryPath;

        Title = "Settings";
        Width = 760;
        Height = 560;
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = true;
        Background = Brush("Sd.Bg");
        Foreground = Brush("Sd.Text");

        _exportFolderDisplay = new TextBlock
        {
            Text = AbbreviatedPath(ResolveExportDirectory(settings)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _saveButtonUsesFolder = new SwitchToggle { IsChecked = settings.EffectiveSaveButtonUsesFolder };
        _launchAtLogin = new SwitchToggle { IsChecked = LaunchAtLogin.IsEnabled() };
        _playSounds = new SwitchToggle { IsChecked = settings.PlaySounds };
        _showTrayIcon = new SwitchToggle { IsChecked = settings.ShowTrayIcon };
        _includeAppWindows = new SwitchToggle { IsChecked = settings.IncludeAppWindowsInCaptures };

        _fullscreenHotkey = new HotkeyRecorderBox(settings.FullscreenHotkey);
        _windowHotkey = new HotkeyRecorderBox(settings.WindowHotkey);
        _areaHotkey = new HotkeyRecorderBox(settings.AreaHotkey);
        _selfTimer = BuildCombo(new[] { "Off", "3 seconds", "5 seconds", "10 seconds" }, DelayIndex(settings.CaptureDelaySeconds));
        _lowResPreview = new SwitchToggle { IsChecked = settings.LowResolutionEditorPreview };
        _afterShowOverlay = new SwitchToggle { IsChecked = settings.AfterCaptureShowOverlay };
        _afterCopy = new SwitchToggle { IsChecked = settings.AutoCopy };
        _afterSave = new SwitchToggle { IsChecked = settings.AutoSave };
        _afterAnnotate = new SwitchToggle { IsChecked = settings.AfterCaptureAnnotate };
        _format = BuildCombo(new[] { "PNG", "JPEG" }, settings.ExportFormat == ExportFormat.Jpeg ? 1 : 0);

        _qualityLabel = new TextBlock
        {
            Text = QualityText(settings.CompressionQuality),
            VerticalAlignment = VerticalAlignment.Center,
            Width = 40,
            TextAlignment = TextAlignment.Right,
        };
        _quality = new Slider
        {
            Minimum = ScreendropSettings.MinQuality,
            Maximum = ScreendropSettings.MaxQuality,
            Value = settings.CompressionQuality,
            TickFrequency = 0.05,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["Sd.Slider"],
        };
        _quality.ValueChanged += (_, _) => _qualityLabel.Text = QualityText(_quality.Value);
        _qualityRow = SliderRow("Compression quality", _quality, _qualityLabel,
            "Lower values produce smaller files with reduced image quality.");

        _fileNamePattern = new TextBox
        {
            Text = settings.FileNamePattern,
            Padding = new Thickness(6, 3, 6, 3),
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        _previewPosition = BuildCombo(new[] { "Bottom left", "Bottom right" }, settings.PreviewPosition == PreviewPosition.Left ? 0 : 1);
        _autoClose = BuildCombo(new[] { "Never", "5 seconds", "10 seconds", "30 seconds", "60 seconds" }, AutoCloseIndex(settings.PreviewAutoCloseSeconds));
        _closeAfterDragging = new SwitchToggle { IsChecked = settings.PreviewCloseAfterDragging };

        _contentHost = new ContentControl();
        BuildPanes();
        Content = BuildLayout();
        ShowPane(Pane.General);

        _format.SelectionChanged += (_, _) =>
            _qualityRow.Visibility = _format.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        _qualityRow.Visibility = _format.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- layout

    private UIElement BuildLayout()
    {
        var sidebar = new ListBox
        {
            Width = 190,
            Background = Brush("Sd.BgRaised"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            BorderBrush = Brush("Sd.BorderSoft"),
            Padding = new Thickness(8, 12, 8, 12),
        };
        AddSidebarItem(sidebar, Pane.General, "\uE713", "General");
        AddSidebarItem(sidebar, Pane.Screenshots, "\uEB9F", "Screenshots");
        AddSidebarItem(sidebar, Pane.Overlay, "\uE737", "Overlay");
        AddSidebarItem(sidebar, Pane.About, "\uE946", "About");
        sidebar.SelectionChanged += (_, _) =>
        {
            if (sidebar.SelectedItem is ListBoxItem { Tag: Pane pane })
            {
                ShowPane(pane);
            }
        };

        var saveButton = new Button
        {
            Content = "Save",
            Style = (Style)Application.Current.Resources["Sd.AccentButton"],
            Padding = new Thickness(22, 6, 22, 6),
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
        };
        saveButton.Click += OnSaveClicked;

        var cancelButton = new Button
        {
            Content = "Cancel",
            Style = (Style)Application.Current.Resources["Sd.Button"],
            Padding = new Thickness(22, 6, 22, 6),
            IsCancel = true,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 0, 16, 14),
        };
        buttons.Children.Add(saveButton);
        buttons.Children.Add(cancelButton);

        var right = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        right.Children.Add(buttons);
        right.Children.Add(_contentHost);

        var root = new DockPanel();
        DockPanel.SetDock(sidebar, Dock.Left);
        root.Children.Add(sidebar);
        root.Children.Add(right);
        return root;
    }

    private void AddSidebarItem(ListBox sidebar, Pane pane, string glyph, string title)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });
        label.Children.Add(new TextBlock
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var item = new ListBoxItem
        {
            Content = label,
            Tag = pane,
            Padding = new Thickness(10, 7, 10, 7),
            IsSelected = pane == _currentPane,
        };
        sidebar.Items.Add(item);
    }

    private void ShowPane(Pane pane)
    {
        _currentPane = pane;
        _contentHost.Content = new ScrollViewer
        {
            Content = _panes[pane],
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 12, 0),
        };
    }

    private void BuildPanes()
    {
        _panes[Pane.General] = BuildGeneralPane();
        _panes[Pane.Screenshots] = BuildScreenshotsPane();
        _panes[Pane.Overlay] = BuildOverlayPane();
        _panes[Pane.About] = BuildAboutPane();
    }

    // ---------------------------------------------------------------- panes

    private UIElement BuildGeneralPane()
    {
        var stack = PaneStack();

        var chooseFolder = new Button
        {
            Content = "Choose Folder…",
            Style = (Style)Application.Current.Resources["Sd.Button"],
            Padding = new Thickness(12, 3, 12, 3),
        };
        chooseFolder.Click += OnBrowseFolder;
        var useDefault = new Button
        {
            Content = "Use Default",
            Style = (Style)Application.Current.Resources["Sd.Button"],
            Padding = new Thickness(12, 3, 12, 3),
            Margin = new Thickness(8, 0, 0, 0),
        };
        useDefault.Click += (_, _) =>
        {
            _exportDirectoryPath = string.Empty;
            _exportFolderDisplay.Text = AbbreviatedPath(SettingsStore.DefaultExportDirectoryPath);
        };

        var folderButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 2) };
        folderButtons.Children.Add(chooseFolder);
        folderButtons.Children.Add(useDefault);

        stack.Children.Add(Section("Save Location", Card(
            LabeledRow("Export folder", _exportFolderDisplay),
            folderButtons,
            ToggleRow("Save without choosing a location",
                "When you click Save, write straight to the export folder instead of asking where to put it.",
                _saveButtonUsesFolder))));

        stack.Children.Add(Section("System", Card(
            ToggleRow("Launch at Login", "Start Screendrop automatically when you sign in.", _launchAtLogin),
            ToggleRow("Play sounds", "Play the camera shutter sound when a screenshot is taken.", _playSounds),
            ToggleRow("Show tray icon", "When hidden, reopen Screendrop to get back to Settings.", _showTrayIcon))));

        stack.Children.Add(Section("Capture Visibility", Card(
            ToggleRow("Include Screendrop windows in captures",
                "Show preview cards, Settings, and other Screendrop windows in screenshots.",
                _includeAppWindows))));

        return stack;
    }

    private UIElement BuildScreenshotsPane()
    {
        var stack = PaneStack();

        stack.Children.Add(Section("Keyboard Shortcuts", Card(
            HotkeyRow("Fullscreen", _fullscreenHotkey),
            HotkeyRow("Window", _windowHotkey),
            HotkeyRow("Area", _areaHotkey))));

        stack.Children.Add(Section("Capture", Card(
            PickerRow("Self-timer", "Show a countdown before the capture is taken.", _selfTimer))));

        stack.Children.Add(Section("Annotation Editor", Card(
            ToggleRow("Use low-resolution preview to save memory",
                "Shows a downscaled image while editing. Saved and exported screenshots are always full resolution.",
                _lowResPreview))));

        stack.Children.Add(Section("After Capture", Card(
            ToggleRow("Show preview overlay", "Show the floating preview card after capturing.", _afterShowOverlay),
            ToggleRow("Copy to clipboard", "Copy the capture to the clipboard.", _afterCopy),
            ToggleRow("Save to folder", "Automatically save the capture to the export folder.", _afterSave),
            ToggleRow("Open annotation editor", "Jump straight into the annotation editor.", _afterAnnotate))));

        stack.Children.Add(Section("File Format", Card(
            PickerRow("Format", null, _format),
            _qualityRow,
            LabeledRow("File name pattern", _fileNamePattern),
            MutedText("Tokens: {timestamp}  {date}  {time}  {type}", topMargin: 6))));

        return stack;
    }

    private UIElement BuildOverlayPane()
    {
        var stack = PaneStack();

        stack.Children.Add(Section("Preview Overlay", Card(
            PickerRow("Position on screen", "Where the floating preview cards appear after a capture.", _previewPosition),
            PickerRow("Auto-close", "Automatically dismiss a preview after this delay, unless you're using it.", _autoClose),
            ToggleRow("Close after dragging", "Dismiss the preview once you drag it out to another app.", _closeAfterDragging))));

        stack.Children.Add(Section("Card Actions", BuildCardActionEditor()));

        return stack;
    }

    private UIElement BuildCardActionEditor()
    {
        var layout = _cardLayoutStore.Layout;
        var rows = new StackPanel();

        rows.Children.Add(MutedText(
            "Choose the action for each corner and the center pills. Actions left out are hidden.",
            bottomMargin: 8));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddSlotRow(grid, 0, "Top left", "TL", layout.TopLeading);
        AddSlotRow(grid, 1, "Top right", "TR", layout.TopTrailing);
        AddSlotRow(grid, 2, "Bottom left", "BL", layout.BottomLeading);
        AddSlotRow(grid, 3, "Bottom right", "BR", layout.BottomTrailing);
        for (int i = 0; i < OverlayCardLayout.MaxCenterActions; i++)
        {
            AddSlotRow(grid, 4 + i, $"Center pill {i + 1}", $"C{i}", i < layout.Center.Count ? layout.Center[i] : null);
        }

        rows.Children.Add(grid);

        var reset = new Button
        {
            Content = "Reset to default",
            Style = (Style)Application.Current.Resources["Sd.Button"],
            Padding = new Thickness(12, 3, 12, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 0),
        };
        reset.Click += (_, _) =>
        {
            var def = OverlayCardLayout.Default;
            _cardSlots["TL"].SelectedIndex = SlotIndex(def.TopLeading);
            _cardSlots["TR"].SelectedIndex = SlotIndex(def.TopTrailing);
            _cardSlots["BL"].SelectedIndex = SlotIndex(def.BottomLeading);
            _cardSlots["BR"].SelectedIndex = SlotIndex(def.BottomTrailing);
            for (int i = 0; i < OverlayCardLayout.MaxCenterActions; i++)
            {
                _cardSlots[$"C{i}"].SelectedIndex = SlotIndex(i < def.Center.Count ? def.Center[i] : null);
            }
        };
        rows.Children.Add(reset);

        return CardShell(rows);
    }

    private void AddSlotRow(Grid grid, int row, string label, string key, CardAction? current)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 8),
        };
        Grid.SetRow(text, row);
        grid.Children.Add(text);

        var combo = BuildCombo(SlotOptions(), SlotIndex(current));
        combo.Margin = new Thickness(0, 0, 0, 8);
        _cardSlots[key] = combo;
        Grid.SetRow(combo, row);
        Grid.SetColumn(combo, 1);
        grid.Children.Add(combo);
    }

    private static string[] SlotOptions()
    {
        var options = new List<string> { "(none)" };
        foreach (var action in Enum.GetValues<CardAction>())
        {
            options.Add(action.Title());
        }

        return options.ToArray();
    }

    private static int SlotIndex(CardAction? action) => action is null ? 0 : (int)action + 1;

    private static CardAction? SlotAction(int selectedIndex) =>
        selectedIndex <= 0 ? null : (CardAction?)(selectedIndex - 1);

    private UIElement BuildAboutPane()
    {
        var stack = PaneStack();

        var header = new StackPanel { Margin = new Thickness(2, 4, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "Screendrop",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
        });
        header.Children.Add(MutedText($"Version {typeof(SettingsWindow).Assembly.GetName().Version}", topMargin: 2));
        header.Children.Add(MutedText("A native screenshot and annotation tool for Windows.", topMargin: 6));
        stack.Children.Add(header);

        stack.Children.Add(Section("Project", Card(
            LinkRow("Open trace log", TraceLogPath),
            LinkRow("Open crash reports", CrashLog.DirectoryPath))));

        stack.Children.Add(MutedText(
            "Screenshots stay on this machine — Screendrop has no sharing or upload features.",
            topMargin: 8));

        return stack;
    }

    // ---------------------------------------------------------------- rows

    private static StackPanel PaneStack() => new() { Margin = new Thickness(20, 18, 8, 8) };

    private static UIElement Section(string title, UIElement content)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("Sd.TextMuted"),
            Margin = new Thickness(2, 0, 0, 6),
        });
        stack.Children.Add(content);
        return stack;
    }

    private static Border Card(params UIElement[] rows)
    {
        var stack = new StackPanel();
        foreach (var row in rows)
        {
            stack.Children.Add(row);
        }

        return CardShell(stack);
    }

    private static Border CardShell(StackPanel stack)
    {
        return new Border
        {
            Background = Brush("Sd.BgRaised"),
            BorderBrush = Brush("Sd.BorderSoft"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 4, 14, 4),
            Child = stack,
        };
    }

    private static UIElement ToggleRow(string title, string? subtitle, SwitchToggle toggle)
    {
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(new TextBlock { Text = title });
        if (!string.IsNullOrEmpty(subtitle))
        {
            label.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 11,
                Foreground = Brush("Sd.TextMuted"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 0),
            });
        }

        var grid = new Grid { Margin = new Thickness(0, 9, 0, 9) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(label, 0);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(label);
        grid.Children.Add(toggle);
        return grid;
    }

    private static UIElement PickerRow(string title, string? subtitle, ComboBox combo)
    {
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(new TextBlock { Text = title });
        if (!string.IsNullOrEmpty(subtitle))
        {
            label.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 11,
                Foreground = Brush("Sd.TextMuted"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 0),
            });
        }

        var grid = new Grid { Margin = new Thickness(0, 9, 0, 9) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        Grid.SetColumn(label, 0);
        Grid.SetColumn(combo, 1);
        grid.Children.Add(label);
        grid.Children.Add(combo);
        return grid;
    }

    private static UIElement LabeledRow(string title, UIElement right)
    {
        var grid = new Grid { Margin = new Thickness(0, 9, 0, 9) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private static UIElement HotkeyRow(string title, HotkeyRecorderBox box)
    {
        box.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { Margin = new Thickness(0, 9, 0, 9) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);
        return grid;
    }

    private static UIElement SliderRow(string title, Slider slider, TextBlock valueLabel, string? subtitle)
    {
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(new TextBlock { Text = title });
        if (!string.IsNullOrEmpty(subtitle))
        {
            label.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 11,
                Foreground = Brush("Sd.TextMuted"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 0),
            });
        }

        var right = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(valueLabel, Dock.Right);
        right.Children.Add(valueLabel);
        right.Children.Add(slider);

        var grid = new Grid { Margin = new Thickness(0, 9, 0, 9) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        Grid.SetColumn(label, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(label);
        grid.Children.Add(right);
        return grid;
    }

    private static UIElement LinkRow(string text, string path)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)Application.Current.Resources["Sd.Button"],
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 6, 0, 6),
        };
        button.Click += (_, _) => OpenFolderOrFile(path);
        return button;
    }

    private static ComboBox BuildCombo(string[] items, int selectedIndex)
    {
        var combo = new ComboBox
        {
            Style = (Style)Application.Current.Resources["Sd.ComboBox"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var item in items)
        {
            combo.Items.Add(item);
        }

        combo.SelectedIndex = Math.Clamp(selectedIndex, 0, items.Length - 1);
        return combo;
    }

    // ---------------------------------------------------------------- save

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        _settings.LaunchAtLogin = _launchAtLogin.IsChecked == true;
        _settings.PlaySounds = _playSounds.IsChecked == true;
        _settings.ShowTrayIcon = _showTrayIcon.IsChecked == true;
        _settings.IncludeAppWindowsInCaptures = _includeAppWindows.IsChecked == true;
        _settings.SaveButtonUsesFolder = _saveButtonUsesFolder.IsChecked == true;
        _settings.ExportDirectoryPath = _exportDirectoryPath;

        _settings.FullscreenHotkey = _fullscreenHotkey.Text;
        _settings.WindowHotkey = _windowHotkey.Text;
        _settings.AreaHotkey = _areaHotkey.Text;
        _settings.CaptureDelaySeconds = DelayFromIndex(_selfTimer.SelectedIndex);
        _settings.LowResolutionEditorPreview = _lowResPreview.IsChecked == true;
        _settings.AfterCaptureShowOverlay = _afterShowOverlay.IsChecked == true;
        _settings.AutoCopy = _afterCopy.IsChecked == true;
        _settings.AutoSave = _afterSave.IsChecked == true;
        _settings.AfterCaptureAnnotate = _afterAnnotate.IsChecked == true;
        _settings.ExportFormat = _format.SelectedIndex == 1 ? ExportFormat.Jpeg : ExportFormat.Png;
        _settings.AutoCompress = _settings.ExportFormat == ExportFormat.Jpeg;
        _settings.CompressionQuality = _quality.Value;
        _settings.FileNamePattern = _fileNamePattern.Text.Trim();

        _settings.PreviewPosition = _previewPosition.SelectedIndex == 0 ? PreviewPosition.Left : PreviewPosition.Right;
        _settings.PreviewAutoCloseSeconds = AutoCloseFromIndex(_autoClose.SelectedIndex);
        _settings.PreviewCloseAfterDragging = _closeAfterDragging.IsChecked == true;

        SaveCardLayout();

        try
        {
            _onSave(_settings);
        }
        catch (Exception ex)
        {
            TraceLog.Write($"settings save failed: {ex}");
            MessageBox.Show(this, $"Could not save settings:\n{ex.Message}", "Screendrop", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Close();
    }

    private void SaveCardLayout()
    {
        var layout = new OverlayCardLayout
        {
            TopLeading = SlotAction(_cardSlots["TL"].SelectedIndex),
            TopTrailing = SlotAction(_cardSlots["TR"].SelectedIndex),
            BottomLeading = SlotAction(_cardSlots["BL"].SelectedIndex),
            BottomTrailing = SlotAction(_cardSlots["BR"].SelectedIndex),
            Center = new List<CardAction>(),
            Hidden = new List<CardAction>(),
        };
        for (int i = 0; i < OverlayCardLayout.MaxCenterActions; i++)
        {
            var action = SlotAction(_cardSlots[$"C{i}"].SelectedIndex);
            if (action is not null)
            {
                layout.Center.Add(action.Value);
            }
        }

        _cardLayoutStore.Save(layout);
    }

    // ---------------------------------------------------------------- helpers

    private static int DelayIndex(int seconds) => seconds switch
    {
        3 => 1,
        5 => 2,
        10 => 3,
        _ => 0,
    };

    private static int DelayFromIndex(int index) => index switch
    {
        1 => 3,
        2 => 5,
        3 => 10,
        _ => 0,
    };

    private static int AutoCloseIndex(int seconds) => seconds switch
    {
        5 => 1,
        10 => 2,
        30 => 3,
        60 => 4,
        _ => 0,
    };

    private static int AutoCloseFromIndex(int index) => index switch
    {
        1 => 5,
        2 => 10,
        3 => 30,
        4 => 60,
        _ => 0,
    };

    private static string ResolveExportDirectory(ScreendropSettings settings) =>
        string.IsNullOrWhiteSpace(settings.ExportDirectoryPath)
            ? SettingsStore.DefaultExportDirectoryPath
            : settings.ExportDirectoryPath;

    private static string AbbreviatedPath(string path)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
        {
            return "~" + path[home.Length..];
        }

        return path;
    }

    private static string QualityText(double value) => $"{value:P0}";

    private static string TraceLogPath => Path.Combine(Path.GetTempPath(), "Screendrop", "trace.log");

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    private static TextBlock MutedText(string text, double topMargin = 0, double bottomMargin = 0) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 11.5,
        Foreground = Brush("Sd.TextMuted"),
        Margin = new Thickness(0, topMargin, 0, bottomMargin),
    };

    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose Save Location",
            InitialDirectory = Directory.Exists(ResolveExportDirectory(_settings))
                ? ResolveExportDirectory(_settings)
                : SettingsStore.DefaultExportDirectoryPath,
        };

        if (dialog.ShowDialog(this) == true)
        {
            _exportDirectoryPath = dialog.FolderName;
            _exportFolderDisplay.Text = AbbreviatedPath(dialog.FolderName);
        }
    }

    private static void OpenFolderOrFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else
            {
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            TraceLog.Write($"could not open '{path}': {ex.Message}");
        }
    }
}
