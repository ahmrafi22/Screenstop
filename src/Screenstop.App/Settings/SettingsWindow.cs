using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Screenstop.App.Infrastructure;
using Screenstop.Core.Settings;

namespace Screenstop.App.Settings;

/// <summary>
/// Preferences window (mac ScreenstopPreferences parity): General /
/// Screenshots / Hotkeys / About tabs. Edits a copy of the settings and
/// applies them atomically on Save through the onSave callback, so the
/// app layer decides what to reload (hotkeys, launch-at-login).
/// </summary>
internal sealed class SettingsWindow : Window
{
    private readonly ScreenstopSettings _settings;
    private readonly Action<ScreenstopSettings> _onSave;

    private readonly CheckBox _launchAtLogin;
    private readonly CheckBox _autoSave;
    private readonly CheckBox _autoCopy;
    private readonly CheckBox _autoCompress;
    private readonly Slider _quality;
    private readonly TextBlock _qualityLabel;
    private readonly TextBox _exportDirectory;
    private readonly TextBox _fileNamePattern;
    private readonly HotkeyRecorderBox _fullscreenHotkey;
    private readonly HotkeyRecorderBox _windowHotkey;
    private readonly HotkeyRecorderBox _areaHotkey;

    public SettingsWindow(ScreenstopSettings settings, Action<ScreenstopSettings> onSave)
    {
        _settings = settings;
        _onSave = onSave;

        Title = "Screenstop Settings";
        Width = 560;
        Height = 500;
        MinWidth = 480;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = true;
        Background = Brush("Sd.Bg");
        Foreground = Brush("Sd.Text");

        _launchAtLogin = new CheckBox
        {
            Content = "Launch Screenstop at login",
            IsChecked = LaunchAtLogin.IsEnabled(),
            Margin = new Thickness(0, 0, 0, 8),
        };

        _autoSave = new CheckBox
        {
            Content = "Automatically save screenshots",
            IsChecked = settings.AutoSave,
            Margin = new Thickness(0, 0, 0, 8),
        };

        _autoCopy = new CheckBox
        {
            Content = "Automatically copy to clipboard",
            IsChecked = settings.AutoCopy,
            Margin = new Thickness(0, 0, 0, 8),
        };

        _autoCompress = new CheckBox
        {
            Content = "Compress saved screenshots (JPEG)",
            IsChecked = settings.AutoCompress,
            Margin = new Thickness(0, 0, 0, 8),
        };

        _qualityLabel = new TextBlock
        {
            Text = QualityText(settings.CompressionQuality),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };

        _quality = new Slider
        {
            Minimum = ScreenstopSettings.MinQuality,
            Maximum = ScreenstopSettings.MaxQuality,
            Value = settings.CompressionQuality,
            TickFrequency = 0.05,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["Sd.Slider"],
        };
        _quality.ValueChanged += (_, _) => _qualityLabel.Text = QualityText(_quality.Value);

        _exportDirectory = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(settings.ExportDirectoryPath)
                ? SettingsStore.DefaultExportDirectoryPath
                : settings.ExportDirectoryPath,
            Padding = new Thickness(4, 2, 4, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        _fileNamePattern = new TextBox
        {
            Text = settings.FileNamePattern,
            Padding = new Thickness(4, 2, 4, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        _fullscreenHotkey = new HotkeyRecorderBox(settings.FullscreenHotkey);
        _windowHotkey = new HotkeyRecorderBox(settings.WindowHotkey);
        _areaHotkey = new HotkeyRecorderBox(settings.AreaHotkey);

        Content = BuildLayout();
    }

    private UIElement BuildLayout()
    {
        var tabs = new TabControl { Margin = new Thickness(12) };
        var tabStyle = (Style)Application.Current.Resources["Sd.TabItem"];

        tabs.Items.Add(new TabItem { Header = "General", Content = BuildGeneralTab(), Style = tabStyle });
        tabs.Items.Add(new TabItem { Header = "Screenshots", Content = BuildScreenshotsTab(), Style = tabStyle });
        tabs.Items.Add(new TabItem { Header = "Hotkeys", Content = BuildHotkeysTab(), Style = tabStyle });
        tabs.Items.Add(new TabItem { Header = "About", Content = BuildAboutTab(), Style = tabStyle });

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
            Margin = new Thickness(12, 0, 12, 12),
        };
        buttons.Children.Add(saveButton);
        buttons.Children.Add(cancelButton);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(tabs);
        return root;
    }

    private UIElement BuildGeneralTab()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(_launchAtLogin);
        stack.Children.Add(MutedText("Screenstop lives in the system tray. Capture with the hotkeys on the Hotkeys tab."));
        return stack;
    }

    private UIElement BuildScreenshotsTab()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(_autoSave);
        stack.Children.Add(_autoCopy);
        stack.Children.Add(_autoCompress);

        var qualityRow = new DockPanel { Margin = new Thickness(0, 4, 0, 12) };
        DockPanel.SetDock(_qualityLabel, Dock.Right);
        qualityRow.Children.Add(_qualityLabel);
        qualityRow.Children.Add(new TextBlock
        {
            Text = "JPEG quality",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        });
        qualityRow.Children.Add(_quality);
        stack.Children.Add(qualityRow);

        stack.Children.Add(Label("Save folder"));
        var folderRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var browse = new Button
        {
            Content = "Browse…",
            Style = (Style)Application.Current.Resources["Sd.Button"],
            Padding = new Thickness(12, 3, 12, 3),
            Margin = new Thickness(8, 0, 0, 0),
        };
        browse.Click += OnBrowseFolder;
        DockPanel.SetDock(browse, Dock.Right);
        folderRow.Children.Add(browse);
        folderRow.Children.Add(_exportDirectory);
        stack.Children.Add(folderRow);

        stack.Children.Add(Label("File name pattern"));
        stack.Children.Add(_fileNamePattern);
        stack.Children.Add(MutedText("Tokens: {timestamp}  {date}  {time}  {type}", topMargin: 4));

        return stack;
    }

    private UIElement BuildHotkeysTab()
    {
        var grid = new Grid { Margin = new Thickness(16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddHotkeyRow(grid, 0, "Capture fullscreen", _fullscreenHotkey);
        AddHotkeyRow(grid, 1, "Capture window", _windowHotkey);
        AddHotkeyRow(grid, 2, "Capture area", _areaHotkey);

        grid.Children.Add(MutedText(
            "Click a box and press the new combination. Backspace resets it to the default.",
            topMargin: 12));
        Grid.SetRow(grid.Children[^1], 3);
        Grid.SetColumnSpan(grid.Children[^1], 2);

        return grid;
    }

    private static void AddHotkeyRow(Grid grid, int row, string label, HotkeyRecorderBox box)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 8),
        };
        Grid.SetRow(text, row);
        grid.Children.Add(text);

        box.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);
    }

    private UIElement BuildAboutTab()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };

        stack.Children.Add(new TextBlock
        {
            Text = "Screenstop for Windows",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });

        stack.Children.Add(MutedText($"Version {typeof(SettingsWindow).Assembly.GetName().Version}", bottomMargin: 12));

        stack.Children.Add(LinkButton("Open trace log", TraceLogPath));
        stack.Children.Add(LinkButton("Open crash reports", CrashLog.DirectoryPath));

        stack.Children.Add(MutedText(
            "Screenshots stay on this machine — Screenstop has no sharing or upload features.",
            topMargin: 12));

        return stack;
    }

    private static string TraceLogPath =>
        Path.Combine(Path.GetTempPath(), "Screenstop", "trace.log");

    private static Button LinkButton(string text, string path)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)Application.Current.Resources["Sd.Button"],
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 0, 0, 6),
        };
        button.Click += (_, _) => OpenFolderOrFile(path);
        return button;
    }

    /// Muted helper text used across all tabs.
    private static TextBlock MutedText(string text, double topMargin = 0, double bottomMargin = 0) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 11.5,
        Foreground = (Brush)Application.Current.Resources["Sd.TextMuted"],
        Margin = new Thickness(0, topMargin, 0, bottomMargin),
    };

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

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

    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose the screenshot save folder",
            InitialDirectory = Directory.Exists(_exportDirectory.Text)
                ? _exportDirectory.Text
                : SettingsStore.DefaultExportDirectoryPath,
        };

        if (dialog.ShowDialog(this) == true)
        {
            _exportDirectory.Text = dialog.FolderName;
        }
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        _settings.LaunchAtLogin = _launchAtLogin.IsChecked == true;
        _settings.AutoSave = _autoSave.IsChecked == true;
        _settings.AutoCopy = _autoCopy.IsChecked == true;
        _settings.AutoCompress = _autoCompress.IsChecked == true;
        _settings.CompressionQuality = _quality.Value;
        _settings.ExportDirectoryPath = _exportDirectory.Text.Trim();
        _settings.FileNamePattern = _fileNamePattern.Text.Trim();
        _settings.FullscreenHotkey = _fullscreenHotkey.Text;
        _settings.WindowHotkey = _windowHotkey.Text;
        _settings.AreaHotkey = _areaHotkey.Text;

        try
        {
            _onSave(_settings);
        }
        catch (Exception ex)
        {
            TraceLog.Write($"settings save failed: {ex}");
            MessageBox.Show(this, $"Could not save settings:\n{ex.Message}", "Screenstop", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Close();
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 0, 0, 4),
    };

    private static string QualityText(double value) => $"{value:P0}";
}
