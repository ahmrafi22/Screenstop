using System.IO;
using Screenstop.Capture;
using Screenstop.Core.History;
using Screenstop.Core.Settings;
using Screenstop.Rendering;
using SkiaSharp;

namespace Screenstop.App.Infrastructure;

internal static class AfterCapturePipeline
{
    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "Screenstop");

    public static AfterCaptureResult Run(SKBitmap bitmap, string captureType, int originX, int originY, DateTimeOffset? timestamp = null)
    {
        var time = timestamp ?? DateTimeOffset.Now;
        var settings = SettingsStore.Load();

        byte[] pngBytes = JpegCompressor.EncodePng(bitmap);

        string stagingName = FileNaming.BuildFileName(ScreenstopSettings.DefaultFileNamePattern, time, captureType, "png");
        Directory.CreateDirectory(TempRoot);
        string stagingPath = FileNaming.ResolveUnique(TempRoot, stagingName);
        File.WriteAllBytes(stagingPath, pngBytes);
        TraceLog.Write($"pipeline: staged {stagingPath} ({pngBytes.Length} bytes)");

        string? savedPath = null;
        string? compressedSummary = null;

        if (settings.AutoSave)
        {
            string directory = ResolveExportDirectory(settings);
            Directory.CreateDirectory(directory);

            string extension = settings.AutoCompress ? "jpg" : "png";
            string fileName = FileNaming.BuildFileName(settings.FileNamePattern, time, captureType, extension);
            savedPath = FileNaming.ResolveUnique(directory, fileName);

            if (settings.AutoCompress)
            {
                byte[] jpeg = JpegCompressor.Encode(bitmap, settings.CompressionQuality);
                File.WriteAllBytes(savedPath, jpeg);
                int savings = SavingsPercent(pngBytes.Length, jpeg.Length);
                compressedSummary = savings > 0 ? $"↓{savings}%" : null;
                TraceLog.Write($"pipeline: saved compressed {savedPath} ({jpeg.Length} bytes)");
            }
            else
            {
                File.WriteAllBytes(savedPath, pngBytes);
                TraceLog.Write($"pipeline: saved {savedPath}");
            }
        }

        bool copied = false;
        if (settings.AutoCopy)
        {
            try
            {
                ClipboardService.SetImage(bitmap);
                copied = true;
                TraceLog.Write("pipeline: copied to clipboard");
            }
            catch (Exception ex)
            {
                TraceLog.Write($"pipeline: clipboard copy failed: {ex.Message}");
            }
        }

        return new AfterCaptureResult(stagingPath, savedPath, copied, compressedSummary, originX, originY);
    }

    private static string ResolveExportDirectory(ScreenstopSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.ExportDirectoryPath)
            ? SettingsStore.DefaultExportDirectoryPath
            : settings.ExportDirectoryPath;
    }

    private static int SavingsPercent(long original, long compressed)
    {
        if (original <= 0)
        {
            return 0;
        }

        int raw = (int)Math.Round((1 - (double)compressed / original) * 100);
        return Math.Max(0, raw);
    }
}

internal sealed record AfterCaptureResult(string StagingPath, string? SavedPath, bool Copied, string? CompressedSummary, int OriginX, int OriginY)
{
    public string ThumbnailPath => SavedPath ?? StagingPath;

    public string DisplaySummary
    {
        get
        {
            var parts = new List<string>();

            if (Copied)
            {
                parts.Add("Copied");
            }

            if (SavedPath is not null)
            {
                parts.Add($"Saved {FormatBytes(new FileInfo(SavedPath).Length)}");
            }
            else
            {
                parts.Add(FormatBytes(new FileInfo(StagingPath).Length));
            }

            if (CompressedSummary is not null)
            {
                parts.Add(CompressedSummary);
            }

            return string.Join(" · ", parts);
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }
}
