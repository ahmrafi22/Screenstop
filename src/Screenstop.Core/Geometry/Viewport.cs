using Screenstop.Core.Annotations;

namespace Screenstop.Core.Geometry;

/// <summary>
/// Zoom/pan viewport math for the annotation canvas, in normalized image
/// space. Pure struct — no UI dependencies — so the zoom-at-point stability
/// and edge clamping are unit-testable.
///
/// Model: the image is displayed at (fitScale * Zoom) where fitScale maps
/// image pixels to canvas pixels at Zoom == 1. Center is the normalized
/// image point shown at the canvas center. At Zoom == 1 the image fits the
/// canvas exactly and Center is pinned to (0.5, 0.5).
/// </summary>
public readonly record struct Viewport(double Zoom, NormalizedPoint Center)
{
    public const double MinZoom = 1.0;
    public const double MaxZoom = 8.0;

    public static Viewport Fit { get; } = new(MinZoom, new NormalizedPoint(0.5, 0.5));

    /// <summary>Display size in canvas pixels at the current zoom.</summary>
    public (double Width, double Height) DisplaySize(double fitWidth, double fitHeight) =>
        (fitWidth * Zoom, fitHeight * Zoom);

    /// <summary>Top-left offset so that Center lands on the canvas center.</summary>
    public (double X, double Y) Offset(double canvasWidth, double canvasHeight, double fitWidth, double fitHeight)
    {
        var (w, h) = DisplaySize(fitWidth, fitHeight);
        return ((canvasWidth / 2) - (Center.X * w), (canvasHeight / 2) - (Center.Y * h));
    }

    /// <summary>
    /// Zooms toward/away from a focus point, keeping that point stable on
    /// screen (the standard editor zoom feel). Clamped to [MinZoom, MaxZoom].
    /// </summary>
    public Viewport ZoomAt(NormalizedPoint focus, double factor, double canvasWidth, double canvasHeight, double fitWidth, double fitHeight)
    {
        if (factor <= 0 || fitWidth <= 0 || fitHeight <= 0)
        {
            return this;
        }

        double newZoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - Zoom) < 1e-9)
        {
            return this;
        }

        var (oldW, oldH) = DisplaySize(fitWidth, fitHeight);
        var (offX, offY) = Offset(canvasWidth, canvasHeight, fitWidth, fitHeight);

        // Focus position in canvas pixels — must be identical after the zoom.
        double focusPxX = offX + (focus.X * oldW);
        double focusPxY = offY + (focus.Y * oldH);

        double newW = fitWidth * newZoom;
        double newH = fitHeight * newZoom;

        double centerX = (focus.X - ((focusPxX - (canvasWidth / 2)) / newW));
        double centerY = (focus.Y - ((focusPxY - (canvasHeight / 2)) / newH));

        return new Viewport(newZoom, new NormalizedPoint(centerX, centerY))
            .Clamped(canvasWidth, canvasHeight, fitWidth, fitHeight);
    }

    /// <summary>Pans by a canvas-pixel drag delta (content follows the cursor).</summary>
    public Viewport Pan(double deltaPxX, double deltaPxY, double canvasWidth, double canvasHeight, double fitWidth, double fitHeight)
    {
        var (w, h) = DisplaySize(fitWidth, fitHeight);
        if (w <= 0 || h <= 0)
        {
            return this;
        }

        var center = new NormalizedPoint(Center.X - (deltaPxX / w), Center.Y - (deltaPxY / h));
        return new Viewport(Zoom, center).Clamped(canvasWidth, canvasHeight, fitWidth, fitHeight);
    }

    /// <summary>
    /// Keeps the view sane: zoom within bounds, and at the current zoom the
    /// image always covers the canvas on each axis where it is larger than
    /// the canvas (no blank strips; overscroll is limited to half a screen).
    /// </summary>
    public Viewport Clamped(double canvasWidth, double canvasHeight, double fitWidth, double fitHeight)
    {
        double zoom = Math.Clamp(Zoom, MinZoom, MaxZoom);
        var (w, h) = (fitWidth * zoom, fitHeight * zoom);

        double minX = w <= canvasWidth ? 0.5 : canvasWidth / (2 * w);
        double maxX = w <= canvasWidth ? 0.5 : 1 - (canvasWidth / (2 * w));
        double minY = h <= canvasHeight ? 0.5 : canvasHeight / (2 * h);
        double maxY = h <= canvasHeight ? 0.5 : 1 - (canvasHeight / (2 * h));

        return new Viewport(
            zoom,
            new NormalizedPoint(Math.Clamp(Center.X, minX, maxX), Math.Clamp(Center.Y, minY, maxY)));
    }
}
