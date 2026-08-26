namespace Screenstop.Core.Settings;

public sealed class ScreenstopSettings
{
    public const double DefaultQuality = 0.75;
    public const double MinQuality = 0.1;
    public const double MaxQuality = 1.0;
    public const string DefaultFileNamePattern = "Screenstop_{timestamp}";

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

    public void Normalize()
    {
        Version = Math.Max(Version, 1);
        CompressionQuality = Math.Clamp(CompressionQuality, MinQuality, MaxQuality);
        if (string.IsNullOrWhiteSpace(FileNamePattern))
        {
            FileNamePattern = DefaultFileNamePattern;
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
}
