using Screenstop.Core.Background;
using Screenstop.Rendering;
using SkiaSharp;
using Xunit;

namespace Screenstop.Core.Tests;

/// <summary>
/// Guards the focus-blur compositor. The mask is a white image used purely as
/// an alpha stencil, so any leak of the mask's own colour into the output is a
/// visible white blob over the sharp focal area - the exact symptom that
/// prompted these tests.
/// </summary>
public class ProgressiveBlurRendererTests
{
    private static ProgressiveBlurSettings Settings(
        double strength = 18,
        double falloff = 0.36,
        double focusSize = 0.15,
        ProgressiveBlurMode mode = ProgressiveBlurMode.Radial) => new()
    {
        IsEnabled = true,
        EdgeMode = ProgressiveBlurEdgeMode.Bleed,
        Mode = mode,
        Strength = strength,
        Falloff = falloff,
        FocusSize = focusSize,
        FocusX = 0.5,
        FocusY = 0.5,
    };

    /// <summary>A dark scene with faint banding: bright output can only be a bug.</summary>
    private static SKBitmap DarkScene(int width, int height)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var card = new SKPaint { Color = new SKColor(35, 38, 45, 255) };
        canvas.DrawRect(20, 20, width - 40, height - 40, card);
        return bitmap;
    }

    private static SKColor Pixel(SKBitmap bitmap, int x, int y) => bitmap.GetPixel(x, y);

    [Fact]
    public void Blur_never_introduces_the_masks_white()
    {
        using var source = DarkScene(400, 300);

        using var result = ProgressiveBlurRenderer.Apply(source, Settings());

        for (int y = 0; y < result.Height; y += 3)
        {
            for (int x = 0; x < result.Width; x += 3)
            {
                var p = Pixel(result, x, y);
                Assert.True(
                    p.Red < 120 && p.Green < 120 && p.Blue < 140,
                    $"mask white leaked at ({x},{y}): {p}");
            }
        }
    }

    [Fact]
    public void Focal_point_stays_sharp_and_surroundings_blur()
    {
        // Vertical stripes blur into a flat wash; a sharp centre keeps them.
        using var source = new SKBitmap(new SKImageInfo(400, 300, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(source))
        {
            canvas.Clear(SKColors.Black);
            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = false };
            for (int x = 0; x < 400; x += 12)
            {
                canvas.DrawRect(x, 0, 6, 300, paint);
            }
        }

        using var result = ProgressiveBlurRenderer.Apply(source, Settings());

        double centreContrast = Contrast(result, 200, 150, 40);
        double edgeContrast = Contrast(result, 200, 12, 40);

        Assert.True(
            centreContrast > edgeContrast,
            $"expected the focal area to stay sharper than the edge (centre={centreContrast:F1}, edge={edgeContrast:F1})");
    }

    [Fact]
    public void Disabled_blur_returns_the_image_unchanged()
    {
        using var source = DarkScene(200, 150);
        var settings = Settings();
        settings.IsEnabled = false;

        using var result = ProgressiveBlurRenderer.Apply(source, settings);

        Assert.Equal(Pixel(source, 100, 75), Pixel(result, 100, 75));
    }

    [Fact]
    public void Directional_mode_also_preserves_content_colour()
    {
        using var source = DarkScene(300, 300);

        using var result = ProgressiveBlurRenderer.Apply(
            source, Settings(mode: ProgressiveBlurMode.Directional));

        var p = Pixel(result, 150, 150);
        Assert.True(p.Red < 120 && p.Green < 120 && p.Blue < 140, $"mask white leaked: {p}");
    }

    /// Mean absolute luminance step across a square sample - a cheap proxy for
    /// "how much stripe detail survives here".
    private static double Contrast(SKBitmap bitmap, int cx, int cy, int radius)
    {
        double sum = 0;
        int count = 0;
        for (int y = cy - radius; y < cy + radius; y++)
        {
            for (int x = cx - radius; x < cx + radius; x++)
            {
                var p = Pixel(bitmap, x, y);
                sum += (p.Red + p.Green + p.Blue) / 3.0;
                count++;
            }
        }

        double mean = sum / count;
        double variance = 0;
        for (int y = cy - radius; y < cy + radius; y++)
        {
            for (int x = cx - radius; x < cx + radius; x++)
            {
                var p = Pixel(bitmap, x, y);
                double luma = (p.Red + p.Green + p.Blue) / 3.0;
                variance += (luma - mean) * (luma - mean);
            }
        }

        return Math.Sqrt(variance / count);
    }
}
