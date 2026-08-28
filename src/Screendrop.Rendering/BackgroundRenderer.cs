using Screendrop.Core.Background;
using SkiaSharp;

namespace Screendrop.Rendering;

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
                using var foreground = DrawForeground(
                    displayedContent, settings, frameGeometry, imageRect, width, height);
                var projection = CameraGeometry.Projection(
                    canvasRect, imageRect, canvasRect.Size(), settings.Camera);
                DrawProjectedForeground(canvas, foreground, projection);
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
    private const int WallpaperCacheLimit = 6;
    private static readonly Dictionary<string, (DateTime WriteTime, SKBitmap Bitmap)> WallpaperCache = new();

    private static SKBitmap? AcquireWallpaper(string path)
    {
        DateTime writeTime = File.GetLastWriteTimeUtc(path);
        string key = Path.GetFullPath(path);

        if (WallpaperCache.TryGetValue(key, out var cached))
        {
            if (cached.WriteTime == writeTime)
            {
                return cached.Bitmap;
            }

            cached.Bitmap.Dispose();
            WallpaperCache.Remove(key);
        }

        var bitmap = SKBitmap.Decode(path);
        if (bitmap is null)
        {
            return null;
        }

        WallpaperCache[key] = (writeTime, bitmap);
        while (WallpaperCache.Count > WallpaperCacheLimit)
        {
            var oldest = WallpaperCache.Keys.First();
            WallpaperCache[oldest].Bitmap.Dispose();
            WallpaperCache.Remove(oldest);
        }

        return bitmap;
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

    private static SKBitmap DrawForeground(
        SKBitmap content,
        BackgroundSettings settings,
        FrameGeometry frameGeometry,
        RectD imageRect,
        int width,
        int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var foreground = new SKBitmap(info);
        using var canvas = new SKCanvas(foreground);
        canvas.Clear(SKColors.Transparent);

        DrawFrameBacking(canvas, settings, frameGeometry, castsShadow: true);
        DrawImage(canvas, content, imageRect, frameGeometry);
        return foreground;
    }

    private static void DrawProjectedForeground(
        SKCanvas canvas, SKBitmap foreground, CameraProjection projection)
    {
        var h = projection.Forward;
        var matrix = new SKMatrix(
            (float)h.A, (float)h.B, (float)h.C,
            (float)h.D, (float)h.E, (float)h.F,
            (float)h.G, (float)h.H, (float)h.I);

        canvas.Save();
        canvas.Concat(ref matrix);
        canvas.DrawBitmap(foreground, 0, 0, new SKPaint { FilterQuality = SKFilterQuality.High });
        canvas.Restore();
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
        using var clipPath = SkiaGeometry.PerCornerPath(geometry.ImageRect, geometry.ImageCornerRadii);
        canvas.Save();
        canvas.ClipPath(clipPath, antialias: true);
        canvas.DrawBitmap(image, imageRect.ToSK(), new SKPaint
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
