using Screenstop.Core.Background;
using SkiaSharp;

namespace Screenstop.Rendering;

/// Progressive (focus) blur. Skia has no masked variable blur filter, so this
/// cross-fades a fully blurred copy over the sharp source using a mask derived
/// from the shared ProgressiveBlurGeometry — visually equivalent to mac's
/// CIFilter.maskedVariableBlur for stills.
///
/// The blend is done per pixel rather than with a canvas blend mode. Routing a
/// mask bitmap through <c>DstIn</c> depends on how Skia premultiplies the
/// stencil, and getting that wrong silently inverts the focus region - the
/// sharp centre comes out washed out while the edges stay crisp. A plain byte
/// ramp has no such ambiguity.
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

        byte[] ramp = BuildRamp(source.Width, source.Height, geometry, settings.Mode);

        using var blurred = Blur(source, (float)(geometry.RenderRadius * 0.5));
        return Blend(source, blurred, ramp);
    }

    /// <summary>Cross-fades <paramref name="blurred"/> over <paramref name="sharp"/>.</summary>
    private static SKBitmap Blend(SKBitmap sharp, SKBitmap blurred, byte[] ramp)
    {
        var result = new SKBitmap(sharp.Info);

        using var resultPixels = result.PeekPixels();
        using var sharpPixels = sharp.PeekPixels();
        using var blurredPixels = blurred.PeekPixels();

        Span<SKColor> destination = resultPixels.GetPixelSpan<SKColor>();
        Span<SKColor> from = sharpPixels.GetPixelSpan<SKColor>();
        Span<SKColor> to = blurredPixels.GetPixelSpan<SKColor>();

        int count = Math.Min(destination.Length, Math.Min(from.Length, to.Length));
        for (int i = 0; i < count; i++)
        {
            byte t = i < ramp.Length ? ramp[i] : (byte)255;
            SKColor a = from[i];

            if (t == 0)
            {
                destination[i] = a;
                continue;
            }

            SKColor b = to[i];
            if (t == 255)
            {
                destination[i] = new SKColor(b.Red, b.Green, b.Blue, a.Alpha);
                continue;
            }

            // Alpha always comes from the sharp image. A blur averages in the
            // transparent pixels just outside the source, so its own alpha
            // falls off around the border; interpolating that would make the
            // corners transparent and let the backdrop show through as a white
            // halo. Colour is what the blur is for; coverage is not.
            destination[i] = new SKColor(
                (byte)(a.Red + ((b.Red - a.Red) * t / 255f)),
                (byte)(a.Green + ((b.Green - a.Green) * t / 255f)),
                (byte)(a.Blue + ((b.Blue - a.Blue) * t / 255f)),
                a.Alpha);
        }

        // Any tail the spans did not cover (colour types without a 4-byte
        // SKColor mapping, for instance) keeps the sharp image.
        for (int i = count; i < destination.Length; i++)
        {
            destination[i] = from[Math.Min(i, from.Length - 1)];
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
        // The blur filter samples past the edges, so start from a known state
        // rather than whatever the allocator handed back.
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint
        {
            ImageFilter = SKImageFilter.CreateBlur(sigma, sigma),
        };
        canvas.DrawBitmap(source, 0, 0, paint);
        return result;
    }

    /// <summary>Per-pixel blend weight: 0 keeps the sharp image, 255 takes the blur.</summary>
    private static byte[] BuildRamp(int width, int height, ProgressiveBlurGeometry geometry, ProgressiveBlurMode mode)
    {
        var ramp = new byte[width * height];
        double transition = Math.Max(geometry.TransitionWidth, 0.0001);

        bool radial = mode == ProgressiveBlurMode.Radial;
        double innerRadius = radial ? geometry.RadialFocusRadius : geometry.DirectionalFocusHalfWidth;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double distance = radial
                    ? Distance(x, y, geometry.Focus.X, geometry.Focus.Y)
                    : Math.Abs(
                        (x - geometry.Focus.X) * geometry.DirectionNormal.Dx
                        + (y - geometry.Focus.Y) * geometry.DirectionNormal.Dy);

                double t = (distance - innerRadius) / transition;
                t = Math.Clamp(t, 0, 1);
                t = t * t * (3 - 2 * t);

                ramp[y * width + x] = (byte)(t * 255);
            }
        }

        return ramp;
    }

    private static double Distance(double x0, double y0, double x1, double y1)
    {
        double dx = x0 - x1;
        double dy = y0 - y1;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
