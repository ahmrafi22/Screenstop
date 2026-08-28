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

        return new BackgroundLayout(canvasSize, cardRect, imageRect, padding);
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
