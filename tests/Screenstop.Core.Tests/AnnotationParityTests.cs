using Screenstop.Core.Annotations;
using Screenstop.Rendering;
using SkiaSharp;
using Xunit;

namespace Screenstop.Core.Tests;

/// Preview/export parity: the editor canvas composites annotations at
/// display resolution while the exporter composites at full resolution.
/// Both paths go through AnnotationRenderer.Draw with normalized
/// coordinates, so rendering the same document at two resolutions must
/// agree within visual tolerance after rescaling. This is the automated
/// form of the "exported image matches canvas" acceptance check.
public class AnnotationParityTests
{
    private const int Small = 240;
    private const int Large = 960;

    /// Deterministic multi-color cell pattern so pixelate/blur have real
    /// content to operate on (a flat fill would hide sampling errors).
    private static SKBitmap PatternBitmap(int size)
    {
        var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        const int cells = 16;
        float cell = size / (float)cells;
        for (int cy = 0; cy < cells; cy++)
        {
            for (int cx = 0; cx < cells; cx++)
            {
                var color = new SKColor(
                    (byte)((cx * 17) % 256),
                    (byte)((cy * 31) % 256),
                    (byte)(((cx + cy) * 53) % 256));
                using var paint = new SKPaint { Color = color };
                canvas.DrawRect(cx * cell, cy * cell, cell, cell, paint);
            }
        }

        return bitmap;
    }

    /// Fraction of sampled pixels whose channels differ by more than
    /// <paramref name="tolerance"/> between two same-sized bitmaps.
    private static double MismatchFraction(SKBitmap a, SKBitmap b, int tolerance)
    {
        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Height, b.Height);

        var pa = new byte[a.ByteCount];
        var pb = new byte[b.ByteCount];
        System.Runtime.InteropServices.Marshal.Copy(a.GetPixels(), pa, 0, pa.Length);
        System.Runtime.InteropServices.Marshal.Copy(b.GetPixels(), pb, 0, pb.Length);

        int strideA = a.RowBytes;
        int strideB = b.RowBytes;
        int mismatches = 0;
        int total = 0;

        for (int y = 0; y < a.Height; y += 2)
        {
            for (int x = 0; x < a.Width; x += 2)
            {
                total++;
                int oa = (y * strideA) + (x * 4);
                int ob = (y * strideB) + (x * 4);
                for (int c = 0; c < 3; c++)
                {
                    if (Math.Abs(pa[oa + c] - pb[ob + c]) > tolerance)
                    {
                        mismatches++;
                        break;
                    }
                }
            }
        }

        return total == 0 ? 0 : (double)mismatches / total;
    }

    /// Renders the document the two ways (direct at small res; large res
    /// then downscaled) and asserts they agree within tolerance.
    private static void AssertParity(AnnotationDocument document, double maxMismatch, int tolerance = 10)
    {
        using var smallSource = PatternBitmap(Small);
        using var largeSource = PatternBitmap(Large);

        using var direct = AnnotationRenderer.Render(smallSource, document);
        using var large = AnnotationRenderer.Render(largeSource, document);

        var downInfo = new SKImageInfo(Small, Small, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var viaLarge = large.Resize(downInfo, SKFilterQuality.High);
        Assert.NotNull(viaLarge);

        double mismatch = MismatchFraction(direct, viaLarge!, tolerance);
        Assert.True(
            mismatch <= maxMismatch,
            $"Preview/export parity broken: {mismatch:P1} of sampled pixels diverge (limit {maxMismatch:P0}).");
    }

    [Fact]
    public void Vector_tools_match_across_resolutions()
    {
        var document = new AnnotationDocument();
        document.Annotations.Add(new Annotation
        {
            Tool = AnnotationTool.Rectangle,
            Rect = new NormalizedRect(0.15, 0.15, 0.35, 0.3),
            Color = AnnotationColor.Red,
            StrokeWidth = 0.012,
        });
        document.Annotations.Add(new Annotation
        {
            Tool = AnnotationTool.Ellipse,
            Rect = new NormalizedRect(0.5, 0.45, 0.35, 0.4),
            Color = AnnotationColor.Blue,
            StrokeWidth = 0.012,
        });
        document.Annotations.Add(new Annotation
        {
            Tool = AnnotationTool.Arrow,
            Start = new NormalizedPoint(0.1, 0.85),
            End = new NormalizedPoint(0.6, 0.55),
            Color = AnnotationColor.Green,
            StrokeWidth = 0.012,
        });

        AssertParity(document, maxMismatch: 0.06);
    }

    [Fact]
    public void Freehand_matches_across_resolutions()
    {
        var document = new AnnotationDocument();
        var stroke = new Annotation
        {
            Tool = AnnotationTool.Freehand,
            Color = AnnotationColor.Black,
            StrokeWidth = 0.012,
        };
        for (int i = 0; i <= 40; i++)
        {
            double t = i / 40.0;
            stroke.Points.Add(new NormalizedPoint(0.1 + (t * 0.8), 0.5 + (Math.Sin(t * Math.PI * 3) * 0.2)));
        }

        document.Annotations.Add(stroke);

        AssertParity(document, maxMismatch: 0.06);
    }

    [Fact]
    public void Pixelate_matches_across_resolutions()
    {
        var document = new AnnotationDocument();
        document.Annotations.Add(new Annotation
        {
            Tool = AnnotationTool.Pixelate,
            Rect = new NormalizedRect(0.2, 0.2, 0.6, 0.6),
        });

        // Pixelate block size is quantized to whole pixels, so small
        // resolution differences are expected at block boundaries.
        AssertParity(document, maxMismatch: 0.10, tolerance: 16);
    }

    [Fact]
    public void Blur_matches_across_resolutions()
    {
        var document = new AnnotationDocument();
        document.Annotations.Add(new Annotation
        {
            Tool = AnnotationTool.Blur,
            Rect = new NormalizedRect(0.25, 0.25, 0.5, 0.5),
        });

        AssertParity(document, maxMismatch: 0.08, tolerance: 14);
    }

    [Fact]
    public void Numbered_circle_matches_across_resolutions()
    {
        var document = new AnnotationDocument();
        document.Annotations.Add(new Annotation
        {
            Tool = AnnotationTool.NumberedCircle,
            Rect = new NormalizedRect(0.4, 0.4, 0.2, 0.2),
            Color = AnnotationColor.Red,
            Number = 3,
        });

        AssertParity(document, maxMismatch: 0.08);
    }

    [Fact]
    public void Text_matches_across_resolutions()
    {
        var document = new AnnotationDocument();
        document.Annotations.Add(new Annotation
        {
            Tool = AnnotationTool.Text,
            Text = "Parity",
            Rect = new NormalizedRect(0.3, 0.4, 0.4, 0.1),
            Color = AnnotationColor.Black,
            FontSize = 0.05,
        });

        // Glyph hinting differs between sizes; allow more slack, but the
        // text must land in the same place at both resolutions.
        AssertParity(document, maxMismatch: 0.10, tolerance: 16);
    }
}
