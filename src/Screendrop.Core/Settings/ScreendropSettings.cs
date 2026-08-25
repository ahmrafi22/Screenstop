namespace Screendrop.Core.Settings;

public sealed class ScreendropSettings
{
    public const double DefaultQuality = 0.75;
    public const double MinQuality = 0.1;
    public const double MaxQuality = 1.0;
    public const string DefaultFileNamePattern = "Screendrop_{timestamp}";

    public int Version { get; set; } = 1;

    public bool AutoSave { get; set; }

    public bool AutoCopy { get; set; }

    public bool AutoCompress { get; set; }

    public double CompressionQuality { get; set; } = DefaultQuality;

    public string ExportDirectoryPath { get; set; } = string.Empty;

    public string FileNamePattern { get; set; } = DefaultFileNamePattern;

    public void Normalize()
    {
        Version = Math.Max(Version, 1);
        CompressionQuality = Math.Clamp(CompressionQuality, MinQuality, MaxQuality);
        if (string.IsNullOrWhiteSpace(FileNamePattern))
        {
            FileNamePattern = DefaultFileNamePattern;
        }
    }
}
