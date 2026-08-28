using Screendrop.Core.Background;
using SkiaSharp;

namespace Screendrop.Rendering;

/// Progressive (focus) blur. Skia has no masked variable blur filter, so this
/// cross-fades a fully blurred copy over the sharp source using a mask derived
/// from the shared ProgressiveBlurGeometry — visually equivalent to mac's
/// CIFilter.maskedVariableBlur for stills.
public static class ProgressiveBlurRenderer
{
    public static SKBitmap Apply(SKBitmap source, ProgressiveBlurSettings settings)
    {
        if (!settings.IsActive || source.Width <= 0 || source.Height <= 0)
        {
            return source.Copy();
        }

        var extent = new RectD(0, 0, source.Width, source.Height);
        var geometry = new ProgressiveBlurGeometry(extent, settings, BlurCoordinateOrigin.TopLeft);

        using var mask = BuildMask(source.Width, source.Height, geometry, settings.Mode);
        using var blurred = Blur(source, (float)(geometry.RenderRadius * 0.5));

        var result = new SKBitmap(source.Info);
        using (var canvas = new SKCanvas(result))
        {
            canvas.DrawBitmap(source, 0, 0);

            using var blended = new SKBitmap(source.Info);
            using (var blendCanvas = new SKCanvas(blended))
            {
                blendCanvas.DrawBitmap(blurred, 0, 0);
                using var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn };
                blendCanvas.DrawBitmap(mask, 0, 0, maskPaint);
            }

            canvas.DrawBitmap(blended, 0, 0);
        }

        return result;
    }

    private static SKBitmap Blur(SKBitmap source, float sigma)
    {
        if (sigma <= 0)
        {
            return source.Copy();
        }

        var result = new SKBitmap(source.Info);
        using var canvas = new SKCanvas(result);
        using var paint = new SKPaint
        {
            ImageFilter = SKImageFilter.CreateBlur(sigma, sigma),
        };
        canvas.DrawBitmap(source, 0, 0, paint);
        return result;
    }

    /// Alpha mask: 0 = keep sharp, 255 = fully blurred.
    private static SKBitmap BuildMask(int width, int height, ProgressiveBlurGeometry geometry, ProgressiveBlurMode mode)
    {
        var mask = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var pixels = new SKColor[width * height];

        double transition = Math.Max(geometry.TransitionWidth, 0.0001);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double distance = mode == ProgressiveBlurMode.Radial
                    ? Distance(x, y, geometry.Focus.X, geometry.Focus.Y)
                    : Math.Abs(
                        (x - geometry.Focus.X) * geometry.DirectionNormal.Dx
                        + (y - geometry.Focus.Y) * geometry.DirectionNormal.Dy);

                double innerRadius = mode == ProgressiveBlurMode.Radial
                    ? geometry.RadialFocusRadius
                    : geometry.DirectionalFocusHalfWidth;

                double t = (distance - innerRadius) / transition;
                t = Math.Clamp(t, 0, 1);
                t = t * t * (3 - 2 * t);

                byte alpha = (byte)(t * 255);
                pixels[y * width + x] = new SKColor(255, 255, 255, alpha);
            }
        }

        mask.Pixels = pixels;
        return mask;
    }

    private static double Distance(double x0, double y0, double x1, double y1)
    {
        double dx = x0 - x1;
        double dy = y0 - y1;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
