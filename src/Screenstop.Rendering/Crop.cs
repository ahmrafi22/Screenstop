using Screenstop.Core.Annotations;
using SkiaSharp;

namespace Screenstop.Rendering;

/// <summary>
/// Pixel-level cropping: extracts a normalized region of a bitmap at full
/// resolution. Geometry remapping lives in Core's CropTransform; this type
/// owns the only Skia dependency.
/// </summary>
public static class Crop
{
    public static SKBitmap Apply(SKBitmap source, NormalizedRect rect)
    {
        int x = (int)Math.Round(rect.X * source.Width);
        int y = (int)Math.Round(rect.Y * source.Height);
        int width = Math.Max(1, (int)Math.Round(rect.Width * source.Width));
        int height = Math.Max(1, (int)Math.Round(rect.Height * source.Height));

        x = Math.Clamp(x, 0, Math.Max(0, source.Width - 1));
        y = Math.Clamp(y, 0, Math.Max(0, source.Height - 1));
        width = Math.Min(width, source.Width - x);
        height = Math.Min(height, source.Height - y);

        var info = new SKImageInfo(width, height, source.Info.ColorType, source.Info.AlphaType);
        var result = new SKBitmap(info);
        using var surface = new SKCanvas(result);
        surface.Clear(SKColors.Transparent);
        surface.DrawBitmap(source, -x, -y);
        surface.Flush();
        return result;
    }

    /// <summary>
    /// Remaps a document into a cropped image's space, preserving exact pixel
    /// appearance: geometry comes from Core's CropTransform, while stroke and
    /// font sizes (normalized against the image's largest dimension) are
    /// rescaled by the change in image size so lines and text keep their
    /// thickness on screen.
    /// </summary>
    public static AnnotationDocument TransformDocument(AnnotationDocument document, NormalizedRect rect, int sourceWidth, int sourceHeight)
    {
        var transformed = CropTransform.TransformDocument(document, rect);

        double sourceMax = Math.Max(sourceWidth, sourceHeight);
        int cropWidth = Math.Max(1, (int)Math.Round(rect.Width * sourceWidth));
        int cropHeight = Math.Max(1, (int)Math.Round(rect.Height * sourceHeight));
        double cropMax = Math.Max(cropWidth, cropHeight);
        double scale = sourceMax / cropMax;

        foreach (var annotation in transformed.Annotations)
        {
            annotation.StrokeWidth *= scale;
            annotation.FontSize *= scale;
        }

        return transformed;
    }
}
