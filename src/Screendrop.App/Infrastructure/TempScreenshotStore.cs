using System.IO;
using SkiaSharp;

namespace Screendrop.App.Infrastructure;

internal static class TempScreenshotStore
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "Screendrop");

    public static string SavePng(SKBitmap bitmap)
    {
        Directory.CreateDirectory(Root);

        string fileName = $"Screendrop_{DateTimeOffset.Now:yyyyMMdd_HHmmss_fff}.png";
        string fullPath = Path.Combine(Root, fileName);

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(fullPath);
        encoded.SaveTo(stream);
        stream.Flush();

        return fullPath;
    }
}
