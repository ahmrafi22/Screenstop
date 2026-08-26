namespace Screendrop.Core.Geometry;

/// <summary>
/// Pure coordinate transform for a zoomable, pannable image viewport.
/// Kept in Core (no UI deps) so the zoom/pan math is unit-tested.
///
/// Mapping: canvasPx = Offset + normalized * DisplaySize, where normalized
/// coordinates run [0,1] across the image. Zoom 1.0 means "fit to canvas";
/// larger values magnify. Panning shifts the image within the canvas, clamped
/// so the image can never be pushed entirely off-screen.
/// </summary>
public sealed class ZoomPanTransform
{
    public const double MinZoom = 1.0;
    public const double MaxZoom = 8.0;

    private const double EdgeMarginPx = 80;
    private const double EdgeMarginFraction = 0.15;

    public double CanvasWidth { get; set; }
    public double CanvasHeight { get; set; }
    public double ImageWidth { get; set; }
    public double ImageHeight { get; set; }

    public double Zoom { get; private set; } = MinZoom;
    public double PanX { get; private set; }
    public double PanY { get; private set; }

    public bool HasImage => ImageWidth > 0 && ImageHeight > 0;
    public bool HasCanvas => CanvasWidth >= 4 && CanvasHeight >= 4;

    /// <summary>Scale that fits the image in the canvas (ignoring zoom).</summary>
    public double FitScale => HasImage
        ? Math.Min(CanvasWidth / ImageWidth, CanvasHeight / ImageHeight)
        : 1;

    public double DisplayWidth => ImageWidth * FitScale * Zoom;
    public double DisplayHeight => ImageHeight * FitScale * Zoom;
    public double OffsetX { get; private set; }
    public double OffsetY { get; private set; }

    /// <summary>Recomputes display size, clamps the pan, and derives offsets.</summary>
    public void Recompute()
    {
        if (!HasImage || !HasCanvas)
        {
            return;
        }

        ClampPan();
        OffsetX = ((CanvasWidth - DisplayWidth) / 2) + PanX;
        OffsetY = ((CanvasHeight - DisplayHeight) / 2) + PanY;
    }

    /// <summary>Sets zoom (clamped) about the canvas center, keeping it in view.</summary>
    public void SetZoom(double zoom)
    {
        ZoomAt(CanvasWidth / 2, CanvasHeight / 2, zoom);
    }

    /// <summary>
    /// Zooms so the image point currently under (canvasX, canvasY) stays under
    /// that same pixel — the standard "zoom at cursor" behavior.
    /// </summary>
    public void ZoomAt(double canvasX, double canvasY, double newZoom)
    {
        if (!HasImage || !HasCanvas)
        {
            return;
        }

        double clamped = Math.Clamp(newZoom, MinZoom, MaxZoom);

        // Normalized point under the cursor in the CURRENT mapping (unclamped,
        // so zooming from the letterbox still anchors sensibly).
        double rawNx = DisplayWidth > 0 ? (canvasX - OffsetX) / DisplayWidth : 0.5;
        double rawNy = DisplayHeight > 0 ? (canvasY - OffsetY) / DisplayHeight : 0.5;

        Zoom = clamped;

        double newDispW = DisplayWidth;
        double newDispH = DisplayHeight;
        double centeredX = (CanvasWidth - newDispW) / 2;
        double centeredY = (CanvasHeight - newDispH) / 2;

        PanX = (canvasX - (rawNx * newDispW)) - centeredX;
        PanY = (canvasY - (rawNy * newDispH)) - centeredY;

        Recompute();
    }

    /// <summary>Pans by a canvas-pixel delta (clamped to keep the image in view).</summary>
    public void PanBy(double dxPx, double dyPx)
    {
        if (!HasImage || !HasCanvas)
        {
            return;
        }

        PanX += dxPx;
        PanY += dyPx;
        Recompute();
    }

    /// <summary>Returns to fit-to-canvas, centered.</summary>
    public void Reset()
    {
        Zoom = MinZoom;
        PanX = 0;
        PanY = 0;
        Recompute();
    }

    /// <summary>Canvas pixel for a normalized image point.</summary>
    public (double X, double Y) ToCanvas(double nx, double ny) =>
        (OffsetX + (nx * DisplayWidth), OffsetY + (ny * DisplayHeight));

    /// <summary>Normalized image point for a canvas pixel, clamped to [0,1].</summary>
    public (double X, double Y) ToNormalized(double canvasX, double canvasY)
    {
        if (DisplayWidth <= 0 || DisplayHeight <= 0)
        {
            return (0, 0);
        }

        return (
            Math.Clamp((canvasX - OffsetX) / DisplayWidth, 0, 1),
            Math.Clamp((canvasY - OffsetY) / DisplayHeight, 0, 1));
    }

    private void ClampPan()
    {
        double centeredX = (CanvasWidth - DisplayWidth) / 2;
        double centeredY = (CanvasHeight - DisplayHeight) / 2;

        // An axis that fully fits doesn't pan: the image is already entirely
        // visible there, so shifting it would only expose empty canvas.
        if (DisplayWidth <= CanvasWidth)
        {
            PanX = 0;
        }
        else
        {
            double marginX = Math.Min(EdgeMarginPx, DisplayWidth * EdgeMarginFraction);
            double minPanX = (marginX - DisplayWidth) - centeredX;
            double maxPanX = (CanvasWidth - marginX) - centeredX;
            PanX = Math.Clamp(PanX, minPanX, maxPanX);
        }

        if (DisplayHeight <= CanvasHeight)
        {
            PanY = 0;
        }
        else
        {
            double marginY = Math.Min(EdgeMarginPx, DisplayHeight * EdgeMarginFraction);
            double minPanY = (marginY - DisplayHeight) - centeredY;
            double maxPanY = (CanvasHeight - marginY) - centeredY;
            PanY = Math.Clamp(PanY, minPanY, maxPanY);
        }
    }
}
