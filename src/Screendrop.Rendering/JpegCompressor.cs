using SkiaSharp;

namespace Screendrop.Rendering;

public static class JpegCompressor
{
    public static byte[] Encode(SKBitmap bitmap, double quality)
    {
        int percent = (int)Math.Round(Math.Clamp(quality, 0.1, 1.0) * 100);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, percent);
        return data.ToArray();
    }

    public static byte[] EncodePng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
