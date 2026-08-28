namespace Screenstop.Core.Settings;

public enum ExportFormat
{
    Png,
    Jpeg,
}

public enum PreviewPosition
{
    Left,
    Right,
}

public sealed class ScreenstopSettings
{
    public const double DefaultQuality = 0.75;
    public const double MinQuality = 0.1;
    public const double MaxQuality = 1.0;
    public const string DefaultFileNamePattern = "Screenstop_{timestamp}";

    public static readonly int[] CaptureDelayOptions = [0, 3, 5, 10];
    public static readonly int[] PreviewAutoCloseOptions = [0, 5, 10, 30, 60];

    public int Version { get; set; } = 1;

    public bool AutoSave { get; set; }

    public bool AutoCopy { get; set; }

    public bool AutoCompress { get; set; }

    public double CompressionQuality { get; set; } = DefaultQuality;

    public string ExportDirectoryPath { get; set; } = string.Empty;

    public string FileNamePattern { get; set; } = DefaultFileNamePattern;

    public bool LaunchAtLogin { get; set; }

    public string FullscreenHotkey { get; set; } = HotkeyCombo.DefaultFullscreen;

    public string WindowHotkey { get; set; } = HotkeyCombo.DefaultWindow;

    public string AreaHotkey { get; set; } = HotkeyCombo.DefaultArea;

    /// Shutter sound after a screenshot (mac `playSounds`, defaults to on).
    public bool PlaySounds { get; set; } = true;

    /// Tray icon visibility (mac `showMenuBarIcon`, defaults to on). When
    /// hidden, hotkeys still work and relaunching opens Settings.
    public bool ShowTrayIcon { get; set; } = true;

    /// Whether Screenstop's own windows are visible in captures. Defaults to
    /// off for capture privacy (mac `includeAppWindowsInCaptures`).
    public bool IncludeAppWindowsInCaptures { get; set; }

    /// Countdown delay in seconds before a capture. 0 = off (mac self-timer).
    public int CaptureDelaySeconds { get; set; }

    /// Which bottom corner the preview overlay docks to (mac default right).
    public PreviewPosition PreviewPosition { get; set; } = PreviewPosition.Right;

    /// Seconds before the preview overlay auto-dismisses. 0 = never.
    public int PreviewAutoCloseSeconds { get; set; }

    /// Dismiss the preview once you drag it out (mac default on).
    public bool PreviewCloseAfterDragging { get; set; } = true;

    /// Downscaled editor preview to save memory; exports stay full-res.
    public bool LowResolutionEditorPreview { get; set; } = true;

    /// Select the exported file in Explorer after an editor export.
    public bool RevealExportInExplorer { get; set; } = true;

    public ExportFormat ExportFormat { get; set; } = ExportFormat.Png;

    /// Panel Save writes straight to the export folder instead of asking.
    /// Null = follow AutoSave (mac `saveButtonUsesConfiguredFolder`).
    public bool? SaveButtonUsesFolder { get; set; }

    /// After-capture: show the floating preview overlay (default on).
    public bool AfterCaptureShowOverlay { get; set; } = true;

    /// After-capture: jump straight into the annotation editor (default off).
    public bool AfterCaptureAnnotate { get; set; }

    public bool EffectiveSaveButtonUsesFolder => SaveButtonUsesFolder ?? AutoSave;

    public void Normalize()
    {
        Version = Math.Max(Version, 1);
        CompressionQuality = Math.Clamp(CompressionQuality, MinQuality, MaxQuality);
        if (string.IsNullOrWhiteSpace(FileNamePattern))
        {
            FileNamePattern = DefaultFileNamePattern;
        }

        CaptureDelaySeconds = SnapToOption(CaptureDelaySeconds, CaptureDelayOptions);
        PreviewAutoCloseSeconds = SnapToOption(PreviewAutoCloseSeconds, PreviewAutoCloseOptions);
        if (!Enum.IsDefined(PreviewPosition))
        {
            PreviewPosition = PreviewPosition.Right;
        }

        if (!Enum.IsDefined(ExportFormat))
        {
            ExportFormat = ExportFormat.Png;
        }

        FullscreenHotkey = NormalizeHotkey(FullscreenHotkey, HotkeyCombo.DefaultFullscreen);
        WindowHotkey = NormalizeHotkey(WindowHotkey, HotkeyCombo.DefaultWindow);
        AreaHotkey = NormalizeHotkey(AreaHotkey, HotkeyCombo.DefaultArea);

        // Two modes bound to the same combo can never both register; the
        // later one falls back to its default so every mode keeps a hotkey.
        if (WindowHotkey == FullscreenHotkey)
        {
            WindowHotkey = HotkeyCombo.DefaultWindow;
        }

        if (AreaHotkey == FullscreenHotkey || AreaHotkey == WindowHotkey)
        {
            AreaHotkey = HotkeyCombo.DefaultArea;
        }
    }

    private static string NormalizeHotkey(string? value, string fallback)
    {
        if (HotkeyCombo.TryParse(value, out var combo) && combo.HasModifier)
        {
            return combo.Format();
        }

        return fallback;
    }

    private static int SnapToOption(int value, int[] options)
    {
        int best = options[0];
        foreach (int option in options)
        {
            if (Math.Abs(option - value) < Math.Abs(best - value))
            {
                best = option;
            }
        }

        return best;
    }
}
