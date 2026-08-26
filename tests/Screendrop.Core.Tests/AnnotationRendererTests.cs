using Screendrop.Core.Annotations;
using Screendrop.Rendering;
using SkiaSharp;
using Xunit;

namespace Screendrop.Core.Tests;

public class AnnotationRendererTests
{
    private static SKBitmap SolidBitmap(int width, int height, SKColor color)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        return bitmap;
    }

    private static SKColor Pixel(SKBitmap bitmap, int x, int y) => bitmap.GetPixel(x, y);

    private static bool Differs(SKColor a, SKColor b) =>
        Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue) > 8;

    [Fact]
    public void Empty_document_returns_untouched_copy()
    {
        using var source = SolidBitmap(100, 80, SKColors.White);
        var document = new AnnotationDocument();

        using var result = AnnotationRenderer.Render(source, document);

        Assert.Equal(source.Width, result.Width);
        Assert.Equal(source.Height, result.Height);
        Assert.Equal(Pixel(source, 50, 40), Pixel(result, 50, 40));
    }

    [Fact]
    public void Rectangle_draws_stroke_at_normalized_position()
    {
        using var source = SolidBitmap(200, 200, SKColors.White);
        var document = new AnnotationDocument();
        document.Annotations.Add(new Annotation
        {
            Tool = AnnotationTool.Rectangle,
            Rect = new NormalizedRect(0.25, 0.25, 0.5, 0.5),
            Color = AnnotationColor.Red,
            StrokeWidth = 0.02,
        });

        using var result = AnnotationRenderer.Render(source, document);

        // Stroke crosses the rect edge at (50, 75) — center of the left edge.
        Assert.True(Differs(Pixel(result, 50, 100), SKColors.White), "left edge stroke");
        // Interior stays white.
        Assert.False(Differs(Pixel(result, 100, 100), SKColors.White), "interior untouched");
        // Outside stays white.
        Assert.False(Differs(Pixel(result, 10, 10), SKColors.White), "outside untouched");
    }

    [Fact]
    public void Ellipse_draws_inside_bounds_and_not_at_corners()
    {
        using var source = SolidBitmap(200, 200, SKColors.White);
        var document = new AnnotationDocument();
        document.Annotations.Add(new Annotation
        {
            Tool = AnnotationTool.Ellipse,
            Rect = new NormalizedRect(0.2, 0.2, 0.6, 0.6),
            Color = AnnotationColor.Blue,
            StrokeWidth = 0.02,
        });

        using var result = AnnotationRenderer.Render(source, document);

        // Top-center of the ellipse bounds: stroke present.
        Assert.True(Differs(Pixel(result, 100, 40), SKColors.White), "top edge stroke");
        // Corner of the bounding box: ellipse does not reach it.
        Assert.False(Differs(Pixel(result, 42, 42), SKColors.White), "bbox corner untouched");
    }

    [Fact]
    public void Arrow_draws_shaft_and_head()
    {
        using var source = SolidBitmap(200, 200, SKColors.White);
        var document = new AnnotationDocument();
        document.Annotations.Add(new Annotation
        {
            Tool = AnnotationTool.Arrow,
            Start = new NormalizedPoint(0.1, 0.5),
            End = new NormalizedPoint(0.9, 0.5),
            Color = AnnotationColor.Black,
            StrokeWidth = 0.02,
        });

        using var result = AnnotationRenderer.Render(source, document);

        // Mid-shaft.
        Assert.True(Differs(Pixel(result, 100, 100), SKColors.White), "shaft");
        // Near the tip (head region).
        Assert.True(Differs(Pixel(result, 172, 100), SKColors.White), "head");
        // Far from the line.
        Assert.False(Differs(Pixel(result, 100, 30), SKColors.White), "off-line untouched");
    }

    [Fact]
    public void Freehand_draws_along_points()
    {
        using var source = SolidBitmap(200, 200, SKColors.White);
        var document = new AnnotationDocument();
        document.Annotations.Add(new Annotation
        {
            Tool = AnnotationTool.Freehand,
            Points = new List<NormalizedPoint>
            {
                new(0.2, 0.2),
                new(0.5, 0.5),
                new(0.8, 0.2),
            },
            Color = AnnotationColor.Green,
            StrokeWidth = 0.02,
        });

        using var result = AnnotationRenderer.Render(source, document);

        Assert.True(Differs(Pixel(result, 100, 100), SKColors.White), "mid path");
        Assert.True(Differs(Pixel(result, 70, 70), SKColors.White), "first segment");
        Assert.False(Differs(Pixel(result, 100, 160), SKColors.White), "below path untouched");
    }

    [Fact]
    public void NumberedCircle_fills_disc()
    {
        using var source = SolidBitmap(200, 200, SKColors.White);
        var document = new AnnotationDocument();
        document.Annotations.Add(new Annotation
        {
            Tool = AnnotationTool.NumberedCircle,
            Rect = new NormalizedRect(0.4, 0.4, 0.2, 0.2),
            Color = AnnotationColor.Red,
            Number = 1,
        });

        using var result = AnnotationRenderer.Render(source, document);

        // Sample inside the disc but away from the centered white digit:
        // a point near the disc's right edge is solid red.
        var discEdge = Pixel(result, 114, 100);
        Assert.True(Differs(discEdge, SKColors.White), "disc filled red");
        // Outside the disc.
        Assert.False(Differs(Pixel(result, 30, 30), SKColors.White), "outside untouched");
    }

    [Fact]
    public void Text_draws_glyphs_at_position()
    {
        using var source = SolidBitmap(400, 200, SKColors.White);
        var document = new AnnotationDocument();
        document.Annotations.Add(new Annotation
        {
            Tool = AnnotationTool.Text,
            Text = "HELLO",
            Rect = new NormalizedRect(0.25, 0.4, 0.5, 0.1),
            Color = AnnotationColor.Black,
            FontSize = 0.08,
        });

        using var result = AnnotationRenderer.Render(source, document);

        // Scan the text band for any dark pixels.
        bool anyDark = false;
        for (int x = 100; x < 300 && !anyDark; x += 2)
        {
            for (int y = 80; y < 120 && !anyDark; y += 2)
            {
                if (Differs(Pixel(result, x, y), SKColors.White))
                {
                    anyDark = true;
                }
            }
        }

        Assert.True(anyDark, "text band contains glyphs");
    }

    [Fact]
    public void Pixelate_reduces_detail_in_region()
    {
        // Checkerboard source: 4px squares.
        using var source = new SKBitmap(new SKImageInfo(200, 200, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(source))
        {
            for (int y = 0; y < 200; y += 8)
            {
                for (int x = 0; x < 200; x += 8)
                {
                    bool even = ((x / 8) + (y / 8)) % 2 == 0;
                    using var paint = new SKPaint { Color = even ? SKColors.Black : SKColors.White };
                    canvas.DrawRect(x, y, 8, 8, paint);
                }
            }
        }

        var document = new AnnotationDocument();
        document.Annotations.Add(new Annotation
        {
            Tool = AnnotationTool.Pixelate,
            Rect = new NormalizedRect(0.25, 0.25, 0.5, 0.5),
        });

        using var result = AnnotationRenderer.Render(source, document);

        // Inside the region, adjacent pixels that differed in the source
        // should now match (mosaic blocks are >= 4px).
        int flattened = 0;
        for (int y = 60; y < 140; y += 4)
        {
            for (int x = 60; x < 140; x += 4)
            {
                if (Pixel(result, x, y) == Pixel(result, x + 3, y))
                {
                    flattened++;
                }
            }
        }

        Assert.True(flattened > 100, $"expected mosaic flattening, got {flattened}");
        // Outside the region the checkerboard survives.
        Assert.True(Differs(Pixel(result, 4, 4), Pixel(result, 12, 4)), "outside region untouched");
    }

    [Fact]
    public void Blur_softens_region_interior()
    {
        // Hard vertical edge: left half black, right half white.
        using var source = new SKBitmap(new SKImageInfo(200, 200, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(source))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(0, 0, 100, 200, paint);
        }

        var document = new AnnotationDocument();
        document.Annotations.Add(new Annotation
        {
            Tool = AnnotationTool.Blur,
            Rect = new NormalizedRect(0.25, 0.25, 0.5, 0.5),
        });

        using var result = AnnotationRenderer.Render(source, document);

        // Deep inside the blurred region near the edge, pixels should be
        // mid-gray (blended), not pure black/white.
        var blended = Pixel(result, 100, 100);
        Assert.True(blended.Red > 30 && blended.Red < 225, $"expected blended gray, got {blended.Red}");
        // Far outside the region the edge stays hard.
        Assert.Equal(0, Pixel(result, 10, 100).Red);
        Assert.Equal(255, Pixel(result, 190, 100).Red);
    }

    [Fact]
    public void Render_does_not_mutate_source()
    {
        using var source = SolidBitmap(100, 100, SKColors.White);
        var document = new AnnotationDocument();
        document.Annotations.Add(new Annotation
        {
            Tool = AnnotationTool.Rectangle,
            Rect = new NormalizedRect(0.1, 0.1, 0.8, 0.8),
            Color = AnnotationColor.Red,
            StrokeWidth = 0.05,
        });

        using var result = AnnotationRenderer.Render(source, document);

        Assert.False(Differs(Pixel(source, 10, 50), SKColors.White), "source untouched");
        Assert.True(Differs(Pixel(result, 10, 50), SKColors.White), "result has stroke");
    }

    [Fact]
    public void Annotations_outside_image_are_clipped_safely()
    {
        using var source = SolidBitmap(100, 100, SKColors.White);
        var document = new AnnotationDocument();
        document.Annotations.Add(new Annotation
        {
            Tool = AnnotationTool.Pixelate,
            Rect = new NormalizedRect(0.8, 0.8, 0.5, 0.5), // extends past the image
        });
        document.Annotations.Add(new Annotation
        {
            Tool = AnnotationTool.Blur,
            Rect = new NormalizedRect(-0.2, -0.2, 0.5, 0.5), // starts before the image
        });

        // Must not throw.
        using var result = AnnotationRenderer.Render(source, document);
        Assert.Equal(100, result.Width);
    }
}
