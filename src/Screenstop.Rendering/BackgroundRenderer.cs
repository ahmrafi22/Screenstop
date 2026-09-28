using Screenstop.Core.Background;
using SkiaSharp;

namespace Screenstop.Rendering;

/// Composites the annotated screenshot onto its mockup stage: fill, padding,
/// rounded corners, shadow, border, camera projection, progressive blur, and
/// watermark (mac `AnnotationBackgroundRenderer` parity).
public static class BackgroundRenderer
{
    public static SKBitmap Compose(SKBitmap content, BackgroundSettings settings)
    {
        if (!settings.HasRenderableContent)
        {
            return content.Copy();
        }

        var contentSize = new SizeD(content.Width, content.Height);
        var layout = BackgroundLayout.Make(contentSize, settings);
        int width = Math.Max(1, (int)Math.Ceiling(layout.CanvasSize.Width));
        int height = Math.Max(1, (int)Math.Ceiling(layout.CanvasSize.Height));

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var result = new SKBitmap(info);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);

        var canvasRect = new RectD(0, 0, width, height);
        DrawBackground(canvas, settings.Style, canvasRect);

        double borderWidth = settings.Border.PixelThickness(content.Width, content.Height);
        var imageRect = PixelAlignedImageRect(layout.ImageRect, contentSize, layout.CanvasSize);
        var frameGeometry = new FrameGeometry(
            imageRect,
            imageRect.Inset(-borderWidth, -borderWidth),
            settings);

        bool usesSceneBlur = settings.ProgressiveBlur.IsActive
            && settings.ProgressiveBlur.EdgeMode == ProgressiveBlurEdgeMode.Bleed;

        SKBitmap displayedContent;
        bool ownsDisplayed = false;
        if (settings.ProgressiveBlur.IsActive
            && settings.ProgressiveBlur.EdgeMode == ProgressiveBlurEdgeMode.Clipped)
        {
            displayedContent = ProgressiveBlurRenderer.Apply(content, settings.ProgressiveBlur);
            ownsDisplayed = true;
        }
        else
        {
            displayedContent = content;
        }

        try
        {
            if (settings.Camera.HasEffect)
            {
                DrawProjectedCard(canvas, displayedContent, settings, frameGeometry, imageRect, canvasRect);
            }
            else
            {
                DrawFrameBacking(canvas, settings, frameGeometry, castsShadow: settings.IsEnabled);
                DrawImage(canvas, displayedContent, imageRect, frameGeometry);
            }

            if (usesSceneBlur)
            {
                ApplySceneBlur(result, settings.ProgressiveBlur);
            }

            if (settings.Watermark.IsVisible)
            {
                DrawWatermark(canvas, settings.Watermark, canvasRect);
            }
        }
        finally
        {
            if (ownsDisplayed)
            {
                displayedContent.Dispose();
            }
        }

        return result;
    }

    private static SizeD Size(this RectD rect) => new(rect.Width, rect.Height);

    /// Flat live preview of the stage for the editor canvas: fills the
    /// background across the whole canvas, then paints the shadow and border
    /// card around where the (unprojected) screenshot sits. Camera projection,
    /// scene blur, and watermark are applied at export time so annotations
    /// stay aligned while editing.
    public static void DrawLiveBackdrop(
        SKCanvas canvas,
        BackgroundSettings settings,
        RectD canvasRect,
        RectD imageRect,
        SizeD contentSize)
    {
        DrawBackground(canvas, settings.Style, canvasRect);

        double displayScale = contentSize.Width > 0 ? imageRect.Width / contentSize.Width : 1;
        double borderWidth =
            settings.Border.PixelThickness(contentSize.Width, contentSize.Height) * displayScale;
        var frameGeometry = new FrameGeometry(
            imageRect, imageRect.Inset(-borderWidth, -borderWidth), settings);
        DrawFrameBacking(canvas, settings, frameGeometry, castsShadow: settings.IsEnabled);
    }

    /// Clips the canvas to the rounded screenshot rect for the live preview so
    /// the image and its annotations match the exported corner clipping.
    public static void ClipLiveImage(SKCanvas canvas, BackgroundSettings settings, RectD imageRect)
    {
        var frameGeometry = new FrameGeometry(imageRect, imageRect, settings);
        canvas.ClipPath(
            SkiaGeometry.PerCornerPath(imageRect, frameGeometry.ImageCornerRadii),
            antialias: true);
    }

    /// Draws only the stage fill (solid/gradient/wallpaper) across a rect.
    public static void DrawStageFill(SKCanvas canvas, BackgroundStyle style, RectD rect) =>
        DrawBackground(canvas, style, rect);

    /// Draws the shadow + outer border card for the screenshot frame. Used by
    /// the live canvas inside the camera projection transform.
    public static void DrawCardBacking(
        SKCanvas canvas, BackgroundSettings settings, FrameGeometry geometry, bool castsShadow) =>
        DrawFrameBacking(canvas, settings, geometry, castsShadow);

    /// Draws the tiled watermark overlay across a rect (flat, not projected).
    public static void DrawWatermarkOverlay(SKCanvas canvas, WatermarkSettings settings, RectD rect) =>
        DrawWatermark(canvas, settings, rect);

    /// Builds the SkiaSharp matrix for a camera homography (row-major).
    public static SKMatrix ToSKMatrix(Homography h) => new(
        (float)h.A, (float)h.B, (float)h.C,
        (float)h.D, (float)h.E, (float)h.F,
        (float)h.G, (float)h.H, (float)h.I);

    private static void DrawBackground(SKCanvas canvas, BackgroundStyle style, RectD rect)
    {
        switch (style.Kind)
        {
            case BackgroundStyleKind.None:
                return;

            case BackgroundStyleKind.Solid:
                var color = style.ResolveColor() ?? BackgroundColor.Black;
                canvas.DrawRect(rect.ToSK(), new SKPaint
                {
                    Color = ToSK(color),
                    IsAntialias = true,
                });
                break;

            case BackgroundStyleKind.Gradient:
                var gradient = style.ResolveGradient();
                if (gradient is null)
                {
                    canvas.DrawRect(rect.ToSK(), new SKPaint { Color = SKColors.Black });
                    break;
                }

                DrawGradient(canvas, gradient, rect);
                break;

            case BackgroundStyleKind.Wallpaper:
                DrawWallpaper(canvas, style.WallpaperPath, rect);
                break;
        }
    }

    private static void DrawGradient(SKCanvas canvas, BackgroundGradient gradient, RectD rect)
    {
        var colors = gradient.Colors.Select(ToSK).ToArray();
        var start = new SKPoint(
            (float)(rect.MinX + gradient.StartPoint.X * rect.Width),
            (float)(rect.MinY + gradient.StartPoint.Y * rect.Height));
        var end = new SKPoint(
            (float)(rect.MinX + gradient.EndPoint.X * rect.Width),
            (float)(rect.MinY + gradient.EndPoint.Y * rect.Height));

        using var shader = SKShader.CreateLinearGradient(
            start, end, colors, null, SKShaderTileMode.Clamp);
        canvas.DrawRect(rect.ToSK(), new SKPaint
        {
            Shader = shader,
            IsAntialias = true,
        });
    }

    private static void DrawWallpaper(SKCanvas canvas, string? path, RectD rect)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            DrawMissingWallpaperFallback(canvas, rect);
            return;
        }

        try
        {
            var image = AcquireWallpaper(path);
            if (image is null)
            {
                DrawMissingWallpaperFallback(canvas, rect);
                return;
            }

            var fill = AspectFillRect(image.Width, image.Height, rect);
            canvas.DrawBitmap(image, fill.ToSK(), new SKPaint
            {
                FilterQuality = SKFilterQuality.High,
                IsAntialias = true,
            });
        }
        catch (Exception)
        {
            DrawMissingWallpaperFallback(canvas, rect);
        }
    }

    // Decoded wallpapers are cached (keyed by path + write time) so the live
    // editor preview doesn't re-decode on every redraw while dragging sliders.
    // The editor paints on the UI thread while an export can be composing on
    // another, and an unsynchronised Dictionary corrupts its own buckets under
    // concurrent writes - which surfaces as an AccessViolationException in
    // unrelated code rather than as an obvious failure here.
    private const int WallpaperCacheLimit = 6;
    private static readonly object WallpaperCacheGate = new();
    private static readonly Dictionary<string, (DateTime WriteTime, SKBitmap Bitmap)> WallpaperCache = new();

    private static SKBitmap? AcquireWallpaper(string path)
    {
        DateTime writeTime = File.GetLastWriteTimeUtc(path);
        string key = Path.GetFullPath(path);

        lock (WallpaperCacheGate)
        {
            if (WallpaperCache.TryGetValue(key, out var cached) && cached.WriteTime == writeTime)
            {
                return cached.Bitmap;
            }

            var bitmap = SKBitmap.Decode(path);
            if (bitmap is null)
            {
                return null;
            }

            WallpaperCache[key] = (writeTime, bitmap);
            while (WallpaperCache.Count > WallpaperCacheLimit)
            {
                // Evicted bitmaps are dropped, never disposed. A reader on
                // another thread may still be mid-draw on one, and freeing it
                // out from under them is a use-after-free. SKBitmap's finalizer
                // reclaims the native memory once nothing references it.
                WallpaperCache.Remove(WallpaperCache.Keys.First());
            }

            return bitmap;
        }
    }

    private static RectD AspectFillRect(double imageWidth, double imageHeight, RectD fillRect)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || fillRect.Width <= 0 || fillRect.Height <= 0)
        {
            return fillRect;
        }

        double scale = Math.Max(fillRect.Width / imageWidth, fillRect.Height / imageHeight);
        double width = imageWidth * scale;
        double height = imageHeight * scale;
        return new RectD(
            fillRect.MidX - width / 2,
            fillRect.MidY - height / 2,
            width,
            height);
    }

    private static void DrawMissingWallpaperFallback(SKCanvas canvas, RectD rect)
    {
        canvas.DrawRect(rect.ToSK(), new SKPaint { Color = SKColors.Black });
        var inset = rect.Inset(8, 8);
        canvas.DrawRect(inset.ToSK(), new SKPaint
        {
            Color = new SKColor(255, 255, 255, 41),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
        });
    }

    /// Stage rect a flat-composed card will occupy, so a caller can size a
    /// surface for it before composing.
    public static RectD CardBounds(
        BackgroundSettings settings, FrameGeometry frameGeometry, RectD canvasRect)
    {
        var caster = settings.Border.IsVisible && frameGeometry.BorderWidth > 0
            ? frameGeometry.CardRect
            : frameGeometry.ImageRect;
        return CardSourceBounds(caster, settings, frameGeometry, canvasRect);
    }

    /// Creates a surface able to hold a card spanning <paramref name="bounds"/>.
    public static SKSurface? CreateCardSurface(RectD bounds) =>
        SKSurface.Create(new SKImageInfo(
            Math.Max(1, (int)Math.Round(bounds.Width)),
            Math.Max(1, (int)Math.Round(bounds.Height)),
            SKColorType.Bgra8888,
            SKAlphaType.Premul));

    /// Composes the card (outer ring, screenshot, and its shadow spill) flat
    /// into <paramref name="surface"/>, which must have been sized by
    /// <see cref="CreateCardSurface"/> for the same <paramref name="bounds"/>.
    ///
    /// The card is composed flat and only then mapped through a camera, by
    /// callers that draw it with <see cref="DrawWarpedCard"/>. Composing under
    /// the perspective matrix instead is what makes a transformed card look
    /// rough: Skia degrades antialiased clipping and path rendering once a
    /// matrix is projective, and the screenshot gets resampled at the card's
    /// local foreshortening rate, smearing the 1px anti-aliased border. Doing
    /// the shadow here also keeps its blur in a space with no perspective
    /// matrix, where Skia's mask filter is exact.
    ///
    /// The surface is fully overwritten, so a caller can reuse one across frames
    /// rather than reallocating on every redraw.
    public static void ComposeCard(
        SKSurface surface,
        SKImage content,
        BackgroundSettings settings,
        FrameGeometry frameGeometry,
        RectD imageRect,
        RectD bounds)
    {
        // The canvas belongs to the surface - disposing it would free the same
        // native object twice. A reused surface still carries last frame's
        // translate, so the matrix is reset before it is applied again.
        var flat = surface.Canvas;
        flat.Save();
        flat.ResetMatrix();
        flat.Clear(SKColors.Transparent);
        flat.Translate((float)-bounds.MinX, (float)-bounds.MinY);
        DrawFrameBacking(flat, settings, frameGeometry, castsShadow: true);
        DrawImage(flat, content, imageRect, frameGeometry);
        flat.Restore();
    }

    /// Warps only the card (ring, screenshot, and its shadow spill), not the
    /// whole stage. Composing flat first keeps the screenshot's interior close
    /// to 1:1, and shrinks the intermediate surface from the whole stage to the
    /// card alone - a 4K stage no longer allocates a second 4K RGBA buffer.
    private static void DrawProjectedCard(
        SKCanvas canvas,
        SKBitmap content,
        BackgroundSettings settings,
        FrameGeometry frameGeometry,
        RectD imageRect,
        RectD canvasRect)
    {
        var bounds = CardBounds(settings, frameGeometry, canvasRect);
        using var surface = CreateCardSurface(bounds);
        if (surface is null)
        {
            DrawFrameBacking(canvas, settings, frameGeometry, castsShadow: true);
            DrawImage(canvas, content, imageRect, frameGeometry);
            return;
        }

        using var contentImage = SKImage.FromBitmap(content);
        ComposeCard(surface, contentImage, settings, frameGeometry, imageRect, bounds);

        using var image = surface.Snapshot();
        var projection = CameraGeometry.Projection(
            bounds, imageRect, canvasRect.Size(), settings.Camera);
        DrawWarpedCard(canvas, image, bounds, projection);
    }

    /// Draws a flat-composed card through a camera projection.
    public static void DrawWarpedCard(
        SKCanvas canvas, SKImage card, RectD sourceRect, CameraProjection projection)
    {
        if (IsIdentity(projection, sourceRect))
        {
            // No transform worth sampling: copy the card across 1:1 so the
            // screenshot and its border stay bit-exact.
            canvas.DrawImage(
                card,
                (float)sourceRect.MinX,
                (float)sourceRect.MinY,
                new SKPaint { FilterQuality = SKFilterQuality.None, IsAntialias = false });
            return;
        }

        var matrix = ToSKMatrix(projection.Forward);
        canvas.Save();
        canvas.Concat(ref matrix);
        canvas.DrawImage(card, sourceRect.ToSK(), new SKPaint
        {
            FilterQuality = SKFilterQuality.High,
            IsAntialias = true,
        });
        canvas.Restore();
    }

    /// The source rectangle a warped card needs: the caster grown by the
    /// shadow's spill, snapped to whole pixels so the flat 1:1 screenshot draw
    /// inside it stays exact. Magnification never widens this - the source is
    /// the input, the projected quad is the destination - so the card's own
    /// spill is all that has to be covered.
    private static RectD CardSourceBounds(
        RectD caster,
        BackgroundSettings settings,
        FrameGeometry frameGeometry,
        RectD canvasRect)
    {
        var radii = settings.Border.IsVisible && frameGeometry.BorderWidth > 0
            ? frameGeometry.CardCornerRadii
            : frameGeometry.ImageCornerRadii;
        double radius = Math.Max(
            Math.Max(radii.TopLeft, radii.TopRight),
            Math.Max(radii.BottomLeft, radii.BottomRight));

        var spill = settings.ShadowStyle.Layer(
            settings.Shadow, Math.Min(caster.Width, caster.Height)) is { } layer
            ? (layer.Radius * 2.5) + Math.Abs(layer.YOffset)
            : 0;

        // The blur is soft, so the outermost falloff can end at the surface
        // edge without a visible seam.
        var bounds = Intersect(caster.Inset(-(spill + radius + 2), -(spill + radius + 2)), canvasRect);

        double x = Math.Round(bounds.MinX);
        double y = Math.Round(bounds.MinY);
        double w = Math.Round(bounds.Width);
        double h = Math.Round(bounds.Height);
        if (w <= 0 || h <= 0)
        {
            return canvasRect;
        }

        w = Math.Min(w, canvasRect.MaxX - x);
        h = Math.Min(h, canvasRect.MaxY - y);
        if (w <= 0 || h <= 0)
        {
            return canvasRect;
        }

        return new RectD(x, y, w, h);
    }

    private static bool IsIdentity(CameraProjection projection, RectD sourceRect) =>
        CornersWithin(projection.Quad.TopLeft, sourceRect, 0.01)
        && CornersWithin(projection.Quad.TopRight, sourceRect, 0.01)
        && CornersWithin(projection.Quad.BottomRight, sourceRect, 0.01)
        && CornersWithin(projection.Quad.BottomLeft, sourceRect, 0.01);

    private static bool CornersWithin(PointD point, RectD rect, double tolerance) =>
        point.X >= rect.MinX - tolerance
        && point.X <= rect.MaxX + tolerance
        && point.Y >= rect.MinY - tolerance
        && point.Y <= rect.MaxY + tolerance;

    private static RectD Intersect(RectD a, RectD b)
    {
        double minX = Math.Max(a.MinX, b.MinX);
        double minY = Math.Max(a.MinY, b.MinY);
        double maxX = Math.Min(a.MaxX, b.MaxX);
        double maxY = Math.Min(a.MaxY, b.MaxY);
        if (maxX <= minX || maxY <= minY)
        {
            return new RectD(
                (a.MinX + a.MaxX) / 2,
                (a.MinY + a.MaxY) / 2,
                0,
                0);
        }

        return new RectD(minX, minY, maxX - minX, maxY - minY);
    }

    private static void DrawFrameBacking(
        SKCanvas canvas, BackgroundSettings settings, FrameGeometry geometry, bool castsShadow)
    {
        if (settings.Border.IsVisible && geometry.BorderWidth > 0)
        {
            double opacity = Math.Clamp(settings.Border.Opacity, 0, 1)
                * Math.Clamp(settings.Border.Color.Alpha, 0, 1);

            if (castsShadow)
            {
                DrawShadow(
                    canvas,
                    geometry.CardRect,
                    geometry.CardCornerRadii,
                    geometry.CardShadowKnockoutRect,
                    geometry.CardShadowKnockoutRadii,
                    settings.Shadow,
                    settings.ShadowStyle);
            }

            var color = settings.Border.Color;
            if (settings.Border.Style == BorderStyle.Glass)
            {
                DrawGlassBorder(canvas, settings.Border, geometry, opacity);
            }
            else
            {
                canvas.DrawPath(
                    SkiaGeometry.PerCornerPath(geometry.CardRect, geometry.CardCornerRadii),
                    new SKPaint
                    {
                        Color = new SKColor(
                            (byte)(color.Red * 255),
                            (byte)(color.Green * 255),
                            (byte)(color.Blue * 255),
                            (byte)(opacity * 255)),
                        IsAntialias = true,
                    });
            }
        }
        else if (castsShadow)
        {
            DrawShadow(
                canvas,
                geometry.ImageRect,
                geometry.ImageCornerRadii,
                geometry.ImageShadowKnockoutRect,
                geometry.ImageShadowKnockoutRadii,
                settings.Shadow,
                settings.ShadowStyle);
        }
    }

    /// Glassmorphism ring: a translucent fill graded bright→dim from top-left
    /// to bottom-right, plus an inner bevel (highlight on the light-facing inner
    /// edge, shadow on the opposite) to sell a thin pane of glass.
    private static void DrawGlassBorder(
        SKCanvas canvas, BorderSettings border, FrameGeometry geometry, double opacity)
    {
        var cardRect = geometry.CardRect;
        var color = border.Color;

        byte bright = (byte)(Math.Clamp(opacity * 1.2, 0, 1) * 255);
        byte dim = (byte)(Math.Clamp(opacity * 0.72, 0, 1) * 255);
        using (var ringPaint = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint((float)cardRect.MinX, (float)cardRect.MinY),
                new SKPoint((float)cardRect.MaxX, (float)cardRect.MaxY),
                new[] { ToSKColor(color, bright), ToSKColor(color, dim) },
                null,
                SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawPath(SkiaGeometry.PerCornerPath(cardRect, geometry.CardCornerRadii), ringPaint);
        }

        // Inner bevel sits inside the ring (outside the image rect) so the image
        // drawn afterwards never covers it.
        double bevelOffset = geometry.BorderWidth * 0.35;
        var bevelRect = geometry.ImageRect.Inset(-bevelOffset, -bevelOffset);
        float bevelStroke = (float)Math.Max(1, geometry.BorderWidth * 0.35);
        using var bevelPath = SkiaGeometry.PerCornerPath(bevelRect, geometry.ImageCornerRadii);

        using (var highlight = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = bevelStroke,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint((float)bevelRect.MinX, (float)bevelRect.MinY),
                new SKPoint((float)bevelRect.MaxX, (float)bevelRect.MaxY),
                new[] { ToSKColor(RgbaColor.White, (byte)(0.35 * 255)), ToSKColor(RgbaColor.White, 0) },
                null,
                SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawPath(bevelPath, highlight);
        }

        using (var shadow = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = bevelStroke,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint((float)bevelRect.MinX, (float)bevelRect.MinY),
                new SKPoint((float)bevelRect.MaxX, (float)bevelRect.MaxY),
                new[] { new SKColor(26, 32, 48, 0), new SKColor(26, 32, 48, (byte)(0.16 * 255)) },
                null,
                SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawPath(bevelPath, shadow);
        }
    }

    private static SKColor ToSKColor(RgbaColor color, byte alpha) => new(
        (byte)(color.Red * 255),
        (byte)(color.Green * 255),
        (byte)(color.Blue * 255),
        alpha);

    /// Paints the shadow without laying ink inside the card: the caster is
    /// clipped away so only the spill survives (mac drawShadow parity).
    private static void DrawShadow(
        SKCanvas canvas,
        RectD casterRect,
        PerCornerRadii casterRadii,
        RectD knockoutRect,
        PerCornerRadii knockoutRadii,
        double strength,
        ShadowStyle style)
    {
        var layer = style.Layer(strength, Math.Min(casterRect.Width, casterRect.Height));
        if (layer is null)
        {
            return;
        }

        using var casterPath = SkiaGeometry.PerCornerPath(casterRect, casterRadii);
        using var knockoutPath = SkiaGeometry.PerCornerPath(knockoutRect, knockoutRadii);

        canvas.Save();
        canvas.ClipPath(knockoutPath, SKClipOperation.Difference, antialias: true);
        canvas.Translate(0, (float)layer.Value.YOffset);
        canvas.DrawPath(casterPath, new SKPaint
        {
            Color = SKColors.Black.WithAlpha((byte)(layer.Value.Alpha * 255)),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)layer.Value.Radius),
            IsAntialias = true,
        });
        canvas.Restore();
    }

    private static void DrawImage(
        SKCanvas canvas, SKBitmap image, RectD imageRect, FrameGeometry geometry)
    {
        using var image2 = SKImage.FromBitmap(image);
        DrawImage(canvas, image2, imageRect, geometry);
    }

    private static void DrawImage(
        SKCanvas canvas, SKImage image, RectD imageRect, FrameGeometry geometry)
    {
        using var clipPath = SkiaGeometry.PerCornerPath(geometry.ImageRect, geometry.ImageCornerRadii);
        canvas.Save();
        canvas.ClipPath(clipPath, antialias: true);
        canvas.DrawImage(image, imageRect.ToSK(), new SKPaint
        {
            FilterQuality = SKFilterQuality.None,
        });
        canvas.Restore();
    }

    private static void ApplySceneBlur(SKBitmap bitmap, ProgressiveBlurSettings settings)
    {
        using var scene = ProgressiveBlurRenderer.Apply(bitmap, settings);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(scene, 0, 0);
    }

    private static void DrawWatermark(SKCanvas canvas, WatermarkSettings settings, RectD rect)
    {
        string text = settings.Text.Trim();
        if (text.Length == 0 || rect.Width <= 1 || rect.Height <= 1)
        {
            return;
        }

        int rows = Math.Max(2, (int)Math.Round(settings.Density));
        double spacingY = rect.Height / rows;
        double spacingX = Math.Max(1, spacingY * 2.2);
        double diagonal = Math.Sqrt(rect.Width * rect.Width + rect.Height * rect.Height);
        int columnCount = Math.Max(2, (int)Math.Ceiling(diagonal / spacingX));
        int rowCount = Math.Max(2, (int)Math.Ceiling(diagonal / spacingY));
        double originX = rect.MidX - columnCount * spacingX / 2;
        double originY = rect.MidY - rowCount * spacingY / 2;

        var resolvedTypeface = SKTypeface.FromFamilyName(
            "Segoe UI",
            SKFontStyleWeight.SemiBold,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright);
        using var ownedTypeface = resolvedTypeface;
        var typeface = resolvedTypeface ?? SKTypeface.Default;
        using var paint = new SKPaint
        {
            Typeface = typeface,
            TextSize = (float)Math.Max(1, settings.FontSize),
            IsAntialias = true,
            Color = ToSK(settings.Color).WithAlpha(
                (byte)(Math.Clamp(settings.Opacity, 0, 0.75) * settings.Color.Alpha * 255)),
        };

        float textWidth = paint.MeasureText(text);
        paint.GetFontMetrics(out var metrics);
        float textHeight = metrics.Descent - metrics.Ascent;

        canvas.Save();
        canvas.ClipRect(rect.ToSK());
        canvas.RotateDegrees((float)-settings.RotationDegrees, (float)rect.MidX, (float)rect.MidY);

        for (int row = 0; row <= rowCount; row++)
        {
            for (int column = 0; column <= columnCount; column++)
            {
                double centerX = originX + column * spacingX;
                double centerY = originY + row * spacingY;
                canvas.DrawText(
                    text,
                    (float)(centerX - textWidth / 2),
                    (float)(centerY - textHeight / 2 - metrics.Ascent),
                    paint);
            }
        }

        canvas.Restore();
    }

    private static RectD PixelAlignedImageRect(RectD rect, SizeD contentSize, SizeD canvasSize)
    {
        double width = Math.Min(contentSize.Width, canvasSize.Width);
        double height = Math.Min(contentSize.Height, canvasSize.Height);
        double x = Math.Round(rect.MinX);
        double y = Math.Round(rect.MinY);
        x = Math.Clamp(x, 0, Math.Max(0, canvasSize.Width - width));
        y = Math.Clamp(y, 0, Math.Max(0, canvasSize.Height - height));
        return new RectD(x, y, width, height);
    }

    private static SKColor ToSK(BackgroundColor color) => new(
        (byte)(color.R * 255),
        (byte)(color.G * 255),
        (byte)(color.B * 255),
        (byte)(color.A * 255));

    private static SKColor ToSK(RgbaColor color) => new(
        (byte)(color.Red * 255),
        (byte)(color.Green * 255),
        (byte)(color.Blue * 255),
        (byte)(color.Alpha * 255));
}
