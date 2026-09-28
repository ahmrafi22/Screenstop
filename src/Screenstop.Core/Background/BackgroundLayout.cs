namespace Screenstop.Core.Background;

public readonly record struct SizeD(double Width, double Height)
{
    public static readonly SizeD Zero = new(0, 0);
}

public readonly record struct PointD(double X, double Y);

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double MinX => X;
    public double MinY => Y;
    public double MaxX => X + Width;
    public double MaxY => Y + Height;
    public double MidX => X + Width / 2;
    public double MidY => Y + Height / 2;

    public static RectD FromSize(SizeD size) => new(0, 0, size.Width, size.Height);

    public RectD Inset(double dx, double dy) => new(X + dx, Y + dy, Width - dx * 2, Height - dy * 2);
}

/// Canvas/card/image layout for the mockup stage (mac
/// `AnnotationBackgroundLayout` parity). All values are in export pixels
/// relative to the content image size.
public sealed record BackgroundLayout(SizeD CanvasSize, RectD CardRect, RectD ImageRect, double Padding)
{
    public static BackgroundLayout Make(SizeD contentSize, BackgroundSettings settings)
    {
        if (contentSize.Width <= 0 || contentSize.Height <= 0)
        {
            return new BackgroundLayout(SizeD.Zero, default, default, 0);
        }

        if (!settings.RequiresCanvasLayout)
        {
            var contentRect = RectD.FromSize(contentSize);
            return new BackgroundLayout(contentSize, contentRect, contentRect, 0);
        }

        var alignment = settings.EffectiveCanvasAlignment;
        double shortestEdge = Math.Min(contentSize.Width, contentSize.Height);
        double borderThickness = settings.Border.PixelThickness(contentSize.Width, contentSize.Height);
        var cardSize = new SizeD(
            contentSize.Width + borderThickness * 2,
            contentSize.Height + borderThickness * 2);

        double normalizedPadding;
        if (settings.IsEnabled)
        {
            normalizedPadding = settings.Padding;
        }
        else if (settings.UsesCanvasLayout)
        {
            normalizedPadding = Math.Max(settings.Padding, 0.18);
        }
        else
        {
            // A border-only export should grow by exactly the outer ring, not
            // inherit the transparent breathing room used by camera effects.
            normalizedPadding = 0;
        }

        double padding = Math.Max(0, shortestEdge * normalizedPadding);

        double paddingTop = alignment.SticksToTop() ? 0 : padding;
        double paddingBottom = alignment.SticksToBottom() ? 0 : padding;
        double paddingLeading = alignment.SticksToLeading() ? 0 : padding;
        double paddingTrailing = alignment.SticksToTrailing() ? 0 : padding;

        var minimumSize = new SizeD(
            cardSize.Width + paddingLeading + paddingTrailing,
            cardSize.Height + paddingTop + paddingBottom);
        var canvasSize = ExpandedSize(minimumSize, settings.UsesCanvasLayout ? settings.AspectRatio.Value() : null);

        var availableRect = new RectD(
            paddingLeading,
            paddingTop,
            canvasSize.Width - paddingLeading - paddingTrailing,
            canvasSize.Height - paddingTop - paddingBottom);

        double originX = availableRect.MinX + Math.Max(0, availableRect.Width - cardSize.Width) * alignment.XFactor();
        double originY = availableRect.MinY + Math.Max(0, availableRect.Height - cardSize.Height) * alignment.YFactor();

        var cardRect = new RectD(originX, originY, cardSize.Width, cardSize.Height);
        var imageRect = cardRect.Inset(borderThickness, borderThickness);

        return FitCamera(
            new BackgroundLayout(canvasSize, cardRect, imageRect, padding),
            padding,
            borderThickness,
            settings);
    }

    /// Grows the stage so a camera transform cannot push the screenshot's own
    /// frame off the export.
    ///
    /// Without this the stage is sized from the flat card alone, so zooming in
    /// magnifies the card straight through the canvas edge and the border ring,
    /// rounded corners, and drop shadow are cropped away - the frame is what
    /// makes the screenshot read as a placed object, and it silently disappears
    /// at exactly the camera angles the user is dialling in.
    ///
    /// Only the projected extent drives the growth. Pan is left alone on
    /// purpose: it is an explicit artistic offset measured against the stage,
    /// so a card the user has deliberately pushed to one side is allowed to run
    /// off the edge. Growing to chase it would chase its own tail, since the
    /// offset is a fraction of the very stage being widened.
    private static BackgroundLayout FitCamera(
        BackgroundLayout layout,
        double padding,
        double borderThickness,
        BackgroundSettings settings)
    {
        if (!settings.Camera.HasEffect || layout.CanvasSize.Width <= 0 || layout.CanvasSize.Height <= 0)
        {
            return layout;
        }

        double shadow = settings.ShadowStyle.Layer(
            settings.Shadow,
            Math.Min(layout.CardRect.Width, layout.CardRect.Height)) is { } layer
            ? layer.Radius + Math.Abs(layer.YOffset)
            : 0;

        // The flat stage already reserves `padding` around the card, so the
        // breathing room a projected card needs is whichever is larger.
        double breathing = Math.Max(padding, shadow);
        var cardRect = layout.CardRect;
        var imageRect = layout.ImageRect;
        var canvasSize = layout.CanvasSize;

        // The camera orbits the card's centre, so recentring the card on a
        // wider stage keeps the framing. Two passes settle the loop, because
        // moving the orbit centre slightly changes the projected span.
        for (int pass = 0; pass < 2; pass++)
        {
            var projected = CameraGeometry.Projection(cardRect, imageRect, canvasSize, settings.Camera);
            var span = SpanOf(projected.Quad);
            double requiredW = span.Width + (breathing * 2);
            double requiredH = span.Height + (breathing * 2);

            bool growW = requiredW > canvasSize.Width;
            bool growH = requiredH > canvasSize.Height;
            if (!growW && !growH)
            {
                break;
            }

            canvasSize = new SizeD(
                growW ? Math.Ceiling(requiredW) : canvasSize.Width,
                growH ? Math.Ceiling(requiredH) : canvasSize.Height);

            // Snapping to whole pixels keeps the export's 1:1 screenshot draw exact.
            cardRect = new RectD(
                Math.Round((canvasSize.Width - cardRect.Width) / 2),
                Math.Round((canvasSize.Height - cardRect.Height) / 2),
                cardRect.Width,
                cardRect.Height);
            imageRect = cardRect.Inset(borderThickness, borderThickness);
        }

        return new BackgroundLayout(canvasSize, cardRect, imageRect, padding);
    }

    private static (double Width, double Height) SpanOf(CameraQuad quad)
    {
        var points = quad.Points;
        double minX = points.Min(p => p.X);
        double minY = points.Min(p => p.Y);
        double maxX = points.Max(p => p.X);
        double maxY = points.Max(p => p.Y);
        return (maxX - minX, maxY - minY);
    }

    private static SizeD ExpandedSize(SizeD size, double? aspectRatio)
    {
        if (aspectRatio is not ( > 0 ) || size.Width <= 0 || size.Height <= 0)
        {
            return size;
        }

        double currentRatio = size.Width / size.Height;
        return currentRatio < aspectRatio.Value
            ? new SizeD(size.Height * aspectRatio.Value, size.Height)
            : new SizeD(size.Width, size.Width / aspectRatio.Value);
    }
}
