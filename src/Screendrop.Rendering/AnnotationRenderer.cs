using Screendrop.Core.Annotations;
using SkiaSharp;

namespace Screendrop.Rendering;

/// Composites annotations onto a source image at full pixel resolution.
/// This is the ONLY place normalized [0,1] coordinates become pixels
/// (mac invariant — the canvas reuses Draw with the same math).
public static class AnnotationRenderer
{
    private const double MinStrokePx = 1.0;
    private const double ArrowHeadLengthFactor = 5.0;
    private const double MinArrowHeadPx = 12.0;
    private const double TextLineHeightFactor = 1.25;
    private const double PixelateBlockFactor = 0.015;
    private const double BlurSigmaFactor = 0.012;
    private const double BlurFeatherFactor = 0.12;
    private const byte HighlightAlpha = 110;

    /// <summary>
    /// Maps the inspector's redaction strength (0..1, default 0.23) to a
    /// multiplier on the pixel block size / blur sigma. Unset (-1, legacy
    /// sidecars) keeps the pre-density default exactly.
    /// </summary>
    private static double DensityScale(double density)
    {
        if (density < 0)
        {
            return 1.0;
        }

        double clamped = Math.Clamp(density, 0.02, 1);
        return 0.25 + (clamped * 3.75); // 23% ≈ 1.11×, 100% = 4×
    }

    /// Renders the source image with all annotations at full resolution, then
    /// composites the result onto the mockup background stage when one is set.
    public static SKBitmap Render(SKBitmap source, AnnotationDocument document)
    {
        SKBitmap annotated;
        if (document.Annotations.Count == 0)
        {
            annotated = source.Copy();
        }
        else
        {
            annotated = source.Copy();
            using (var canvas = new SKCanvas(annotated))
            using (var image = SKImage.FromBitmap(source))
            {
                Draw(canvas, image, document.Annotations, source.Width, source.Height);
            }
        }

        return ApplyBackground(annotated, document.Background);
    }

    /// Composites an already-rendered bitmap onto the background stage.
    /// Returns the input unchanged (not a copy) when there is nothing to add.
    public static SKBitmap ApplyBackground(SKBitmap rendered, Core.Background.BackgroundSettings? background)
    {
        if (background is null || !background.HasRenderableContent)
        {
            return rendered;
        }

        using (rendered)
        {
            return BackgroundRenderer.Compose(rendered, background);
        }
    }

    /// Draws annotations onto a canvas. Coordinates are derived from the
    /// base image dimensions (imageWidth/imageHeight), so the same call
    /// serves both full-res export and the zoomed live canvas.
    public static void Draw(
        SKCanvas canvas,
        SKImage source,
        IReadOnlyList<Annotation> annotations,
        int imageWidth,
        int imageHeight)
    {
        double maxDim = Math.Max(imageWidth, imageHeight);

        foreach (var annotation in annotations)
        {
            switch (annotation.Tool)
            {
                case AnnotationTool.Rectangle:
                    DrawRectangle(canvas, annotation, imageWidth, imageHeight, maxDim);
                    break;
                case AnnotationTool.Ellipse:
                    DrawEllipse(canvas, annotation, imageWidth, imageHeight, maxDim);
                    break;
                case AnnotationTool.Arrow:
                    DrawArrow(canvas, annotation, imageWidth, imageHeight, maxDim);
                    break;
                case AnnotationTool.Freehand:
                    DrawFreehand(canvas, annotation, imageWidth, imageHeight, maxDim);
                    break;
                case AnnotationTool.Text:
                    DrawText(canvas, annotation, imageWidth, imageHeight, maxDim);
                    break;
                case AnnotationTool.NumberedCircle:
                    DrawNumberedCircle(canvas, annotation, imageWidth, imageHeight, maxDim);
                    break;
                case AnnotationTool.Pixelate:
                    DrawPixelate(canvas, source, annotation, imageWidth, imageHeight, maxDim);
                    break;
                case AnnotationTool.Blur:
                    DrawBlur(canvas, source, annotation, imageWidth, imageHeight, maxDim);
                    break;
                case AnnotationTool.Line:
                    DrawLine(canvas, annotation, imageWidth, imageHeight, maxDim);
                    break;
                case AnnotationTool.Highlight:
                    DrawHighlight(canvas, annotation, imageWidth, imageHeight, maxDim);
                    break;
                case AnnotationTool.FilledRectangle:
                    DrawFilledRectangle(canvas, annotation, imageWidth, imageHeight, maxDim);
                    break;
            }
        }
    }

    private static SKColor ToColor(AnnotationColor c) => new(c.R255, c.G255, c.B255, c.A255);

    private static SKRect ToPixelRect(NormalizedRect r, int w, int h) =>
        new((float)(r.X * w), (float)(r.Y * h), (float)(r.Right * w), (float)(r.Bottom * h));

    private static float StrokePx(Annotation a, double maxDim) =>
        (float)Math.Max(MinStrokePx, a.StrokeWidth * maxDim);

    private static void DrawRectangle(SKCanvas canvas, Annotation a, int w, int h, double maxDim)
    {
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = StrokePx(a, maxDim),
            Color = ToColor(a.Color),
            IsAntialias = true,
        };
        canvas.DrawRect(ToPixelRect(a.Rect, w, h), paint);
    }

    private static void DrawEllipse(SKCanvas canvas, Annotation a, int w, int h, double maxDim)
    {
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = StrokePx(a, maxDim),
            Color = ToColor(a.Color),
            IsAntialias = true,
        };
        canvas.DrawOval(ToPixelRect(a.Rect, w, h), paint);
    }

    /// Semi-transparent marker stroke (mac highlight parity). Alpha is
    /// baked into the paint rather than the color so the palette swatches
    /// stay fully opaque in the toolbar.
    private static void DrawHighlight(SKCanvas canvas, Annotation a, int w, int h, double maxDim)
    {
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.StrokeAndFill,
            StrokeWidth = StrokePx(a, maxDim),
            Color = ToColor(a.Color).WithAlpha(HighlightAlpha),
            IsAntialias = true,
        };
        canvas.DrawRect(ToPixelRect(a.Rect, w, h), paint);
    }

    private static void DrawFilledRectangle(SKCanvas canvas, Annotation a, int w, int h, double maxDim)
    {
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ToColor(a.Color),
            IsAntialias = true,
        };
        canvas.DrawRect(ToPixelRect(a.Rect, w, h), paint);
    }

    private static void DrawLine(SKCanvas canvas, Annotation a, int w, int h, double maxDim)
    {
        float x1 = (float)(a.Start.X * w);
        float y1 = (float)(a.Start.Y * h);
        float x2 = (float)(a.End.X * w);
        float y2 = (float)(a.End.Y * h);

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = StrokePx(a, maxDim),
            StrokeCap = SKStrokeCap.Round,
            Color = ToColor(a.Color),
            IsAntialias = true,
        };
        canvas.DrawLine(x1, y1, x2, y2, paint);
    }

    private static void DrawArrow(SKCanvas canvas, Annotation a, int w, int h, double maxDim)
    {
        float x1 = (float)(a.Start.X * w);
        float y1 = (float)(a.Start.Y * h);
        float x2 = (float)(a.End.X * w);
        float y2 = (float)(a.End.Y * h);

        double dx = x2 - x1;
        double dy = y2 - y1;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 0.5)
        {
            return;
        }

        float stroke = StrokePx(a, maxDim);
        var color = ToColor(a.Color);

        using var shaftPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = stroke,
            StrokeCap = SKStrokeCap.Round,
            Color = color,
            IsAntialias = true,
        };

        double headLength = Math.Max(MinArrowHeadPx, stroke * ArrowHeadLengthFactor);
        headLength = Math.Min(headLength, length * 0.5);
        double ux = dx / length;
        double uy = dy / length;

        // Shaft stops where the head begins so the cap doesn't poke through.
        float shaftEndX = (float)(x2 - (ux * headLength * 0.75));
        float shaftEndY = (float)(y2 - (uy * headLength * 0.75));
        canvas.DrawLine(x1, y1, shaftEndX, shaftEndY, shaftPaint);

        // Filled triangular head.
        double headHalfWidth = headLength * 0.5;
        double baseX = x2 - (ux * headLength);
        double baseY = y2 - (uy * headLength);
        double px = -uy;
        double py = ux;

        using var headPath = new SKPath();
        headPath.MoveTo(x2, y2);
        headPath.LineTo((float)(baseX + (px * headHalfWidth)), (float)(baseY + (py * headHalfWidth)));
        headPath.LineTo((float)(baseX - (px * headHalfWidth)), (float)(baseY - (py * headHalfWidth)));
        headPath.Close();

        using var headPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = color,
            IsAntialias = true,
        };
        canvas.DrawPath(headPath, headPaint);
    }

    private static void DrawFreehand(SKCanvas canvas, Annotation a, int w, int h, double maxDim)
    {
        if (a.Points.Count == 0)
        {
            return;
        }

        using var path = new SKPath();
        path.MoveTo((float)(a.Points[0].X * w), (float)(a.Points[0].Y * h));
        for (int i = 1; i < a.Points.Count; i++)
        {
            path.LineTo((float)(a.Points[i].X * w), (float)(a.Points[i].Y * h));
        }

        if (a.Points.Count == 1)
        {
            // A single tap renders as a dot.
            path.LineTo((float)(a.Points[0].X * w) + 0.1f, (float)(a.Points[0].Y * h) + 0.1f);
        }

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = StrokePx(a, maxDim),
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = ToColor(a.Color),
            IsAntialias = true,
        };
        canvas.DrawPath(path, paint);
    }

    private static void DrawText(SKCanvas canvas, Annotation a, int w, int h, double maxDim)
    {
        if (string.IsNullOrEmpty(a.Text))
        {
            return;
        }

        float fontSize = (float)Math.Max(8, a.FontSize * maxDim);
        using var paint = new SKPaint
        {
            Color = ToColor(a.Color),
            IsAntialias = true,
            TextSize = fontSize,
            Typeface = SKTypeface.Default,
        };

        float x = (float)(a.Rect.X * w);
        float y = (float)(a.Rect.Y * h);
        float lineHeight = (float)(fontSize * TextLineHeightFactor);

        string[] lines = a.Text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            canvas.DrawText(lines[i], x, y + (i * lineHeight) + fontSize, paint);
        }
    }

    private static void DrawNumberedCircle(SKCanvas canvas, Annotation a, int w, int h, double maxDim)
    {
        var rect = ToPixelRect(a.Rect, w, h);
        float radius = Math.Min(rect.Width, rect.Height) / 2f;
        if (radius < 1)
        {
            return;
        }

        float cx = rect.MidX;
        float cy = rect.MidY;

        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ToColor(a.Color),
            IsAntialias = true,
        };
        canvas.DrawCircle(cx, cy, radius, fillPaint);

        string label = a.Number > 0 ? a.Number.ToString() : "?";
        float fontSize = radius * 1.1f;
        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            TextSize = fontSize,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
        };

        textPaint.GetFontMetrics(out var metrics);
        float baselineY = cy - ((metrics.Ascent + metrics.Descent) / 2f);
        float textWidth = textPaint.MeasureText(label);
        canvas.DrawText(label, cx - (textWidth / 2f), baselineY, textPaint);
    }

    private static void DrawPixelate(SKCanvas canvas, SKImage source, Annotation a, int w, int h, double maxDim)
    {
        var region = SKRect.Intersect(ToPixelRect(a.Rect, w, h), SKRect.Create(w, h));
        if (region.Width < 2 || region.Height < 2)
        {
            return;
        }

        int block = (int)Math.Clamp(Math.Round(PixelateBlockFactor * maxDim * DensityScale(a.Density)), 2, 96);
        int rx = (int)Math.Floor(region.Left);
        int ry = (int)Math.Floor(region.Top);
        int rw = (int)Math.Ceiling(region.Width);
        int rh = (int)Math.Ceiling(region.Height);

        using var subset = source.Subset(SKRectI.Create(rx, ry, rw, rh));
        if (subset is null)
        {
            return;
        }

        // Downscale with nearest sampling, then back up: classic mosaic.
        int smallW = Math.Max(1, rw / block);
        int smallH = Math.Max(1, rh / block);

        using var nearestPaint = new SKPaint { FilterQuality = SKFilterQuality.None };
        using var smallSurface = SKSurface.Create(new SKImageInfo(smallW, smallH, SKColorType.Bgra8888, SKAlphaType.Opaque));
        smallSurface.Canvas.DrawImage(subset, SKRect.Create(0, 0, smallW, smallH), nearestPaint);

        using var smallImage = smallSurface.Snapshot();
        canvas.DrawImage(smallImage, SKRect.Create(rx, ry, rw, rh), nearestPaint);
    }

    private static void DrawBlur(SKCanvas canvas, SKImage source, Annotation a, int w, int h, double maxDim)
    {
        var region = SKRect.Intersect(ToPixelRect(a.Rect, w, h), SKRect.Create(w, h));
        if (region.Width < 2 || region.Height < 2)
        {
            return;
        }

        float sigma = (float)Math.Max(1.5, BlurSigmaFactor * maxDim * DensityScale(a.Density));
        float feather = (float)Math.Max(4, Math.Min(region.Width, region.Height) * BlurFeatherFactor);

        // Pad the region so the blur has real pixels to sample at its edges,
        // then feather the result back into the image to avoid a hard seam.
        int pad = (int)Math.Ceiling(sigma * 3);
        int rx = Math.Max(0, (int)Math.Floor(region.Left) - pad);
        int ry = Math.Max(0, (int)Math.Floor(region.Top) - pad);
        int rx2 = Math.Min(w, (int)Math.Ceiling(region.Right) + pad);
        int ry2 = Math.Min(h, (int)Math.Floor(region.Bottom) + pad);
        int rw = rx2 - rx;
        int rh = ry2 - ry;
        if (rw < 2 || rh < 2)
        {
            return;
        }

        var info = new SKImageInfo(rw, rh, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface is null)
        {
            return;
        }

        using (var blurFilter = SKImageFilter.CreateBlur(sigma, sigma))
        using (var blurPaint = new SKPaint { ImageFilter = blurFilter })
        {
            surface.Canvas.DrawImage(source, new SKRect(-rx, -ry, w - rx, h - ry), blurPaint);
        }

        // Feather the alpha with an inset gradient so edges blend smoothly.
        ApplyFeatherMask(surface, rw, rh, feather);

        canvas.DrawSurface(surface, rx, ry);
    }

    /// Multiplies the surface's alpha by a rectangular feather gradient
    /// (opaque inside, fading to transparent across `feather` px at the rim).
    private static void ApplyFeatherMask(SKSurface surface, int width, int height, float feather)
    {
        if (feather <= 1)
        {
            return;
        }

        using var maskSurface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = maskSurface.Canvas;
        canvas.Clear(SKColors.White);

        // Punch transparent rims with four linear gradients.
        DrawGradientEdge(canvas, SKRect.Create(0, 0, width, feather), 0);          // top
        DrawGradientEdge(canvas, SKRect.Create(0, height - feather, width, feather), 1); // bottom
        DrawGradientEdge(canvas, SKRect.Create(0, 0, feather, height), 2);          // left
        DrawGradientEdge(canvas, SKRect.Create(width - feather, 0, feather, height), 3); // right

        // Composite: surface alpha *= mask alpha.
        using var snapshot = maskSurface.Snapshot();
        using var dstInPaint = new SKPaint { BlendMode = SKBlendMode.DstIn };
        surface.Canvas.DrawImage(snapshot, 0, 0, dstInPaint);
    }

    private static void DrawGradientEdge(SKCanvas canvas, SKRect rect, int edge)
    {
        SKPoint start, end;
        switch (edge)
        {
            case 0: start = new SKPoint(rect.MidX, rect.Top); end = new SKPoint(rect.MidX, rect.Bottom); break;
            case 1: start = new SKPoint(rect.MidX, rect.Bottom); end = new SKPoint(rect.MidX, rect.Top); break;
            case 2: start = new SKPoint(rect.Left, rect.MidY); end = new SKPoint(rect.Right, rect.MidY); break;
            default: start = new SKPoint(rect.Right, rect.MidY); end = new SKPoint(rect.Left, rect.MidY); break;
        }

        using var shader = SKShader.CreateLinearGradient(
            start,
            end,
            new[] { SKColors.Transparent, SKColors.White },
            new[] { 0f, 1f },
            SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(rect, paint);
    }
}
