namespace Screendrop.Core.Background;

public enum BlurCoordinateOrigin
{
    TopLeft,
    BottomLeft,
}

/// Shared, pure geometry for the inspector, live preview, and renderer
/// (mac `AnnotationProgressiveBlurGeometry` parity). Keeping these values in
/// one place prevents preview/export drift.
public sealed class ProgressiveBlurGeometry
{
    public RectD Extent { get; }

    public PointD Focus { get; }

    public PointD[] Corners { get; }

    public (double Dx, double Dy) DirectionNormal { get; }

    public double[] DirectionalDistances { get; }

    public double ShortestEdge { get; }

    public double TransitionWidth { get; }

    public double RadialFocusRadius { get; }

    public double DirectionalFocusHalfWidth { get; }

    public double RenderRadius { get; }

    public ProgressiveBlurGeometry(
        RectD extent,
        ProgressiveBlurSettings settings,
        BlurCoordinateOrigin coordinateOrigin)
        : this(
            extent,
            settings.FocusX,
            settings.FocusY,
            settings.FocusSize,
            settings.Falloff,
            settings.DirectionDegrees,
            settings.Strength,
            coordinateOrigin)
    {
    }

    public ProgressiveBlurGeometry(
        RectD extent,
        double focusX,
        double focusY,
        double focusSize,
        double falloff,
        double directionDegrees,
        double strength,
        BlurCoordinateOrigin coordinateOrigin)
    {
        Extent = extent;

        double normalizedFocusX = Math.Clamp(focusX, 0, 1);
        double normalizedFocusY = Math.Clamp(focusY, 0, 1);
        Focus = new PointD(
            extent.MinX + normalizedFocusX * extent.Width,
            extent.MinY + (coordinateOrigin == BlurCoordinateOrigin.TopLeft
                ? normalizedFocusY
                : 1 - normalizedFocusY) * extent.Height);

        Corners =
        [
            new PointD(extent.MinX, extent.MinY),
            new PointD(extent.MaxX, extent.MinY),
            new PointD(extent.MaxX, extent.MaxY),
            new PointD(extent.MinX, extent.MaxY),
        ];

        double angle = directionDegrees * Math.PI / 180;
        DirectionNormal = coordinateOrigin == BlurCoordinateOrigin.TopLeft
            ? (-Math.Sin(angle), Math.Cos(angle))
            : (Math.Sin(angle), Math.Cos(angle));

        DirectionalDistances = Corners
            .Select(corner =>
                (corner.X - Focus.X) * DirectionNormal.Dx
                + (corner.Y - Focus.Y) * DirectionNormal.Dy)
            .ToArray();

        ShortestEdge = Math.Min(extent.Width, extent.Height);
        double normalizedFocusSize = Math.Clamp(focusSize, 0, 1);
        double normalizedFalloff = Math.Clamp(falloff, 0, 1);
        TransitionWidth = ShortestEdge * (0.10 + normalizedFalloff * 0.48);

        double minimumRadius = ShortestEdge * 0.04;
        double maximumRadius = Corners.Length > 0
            ? Corners.Max(corner => Distance(corner, Focus))
            : minimumRadius;
        RadialFocusRadius = minimumRadius + normalizedFocusSize * Math.Max(0, maximumRadius - minimumRadius);

        double minimumHalfWidth = ShortestEdge * 0.025;
        double maximumHalfWidth = DirectionalDistances.Length > 0
            ? DirectionalDistances.Max(Math.Abs)
            : minimumHalfWidth;
        DirectionalFocusHalfWidth = minimumHalfWidth + normalizedFocusSize * Math.Max(0, maximumHalfWidth - minimumHalfWidth);

        RenderRadius = Math.Max(0.5, strength * ShortestEdge / 1000);
    }

    private static double Distance(PointD a, PointD b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
