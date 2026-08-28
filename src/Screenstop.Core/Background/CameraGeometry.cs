namespace Screenstop.Core.Background;

public sealed record CameraQuad(PointD TopLeft, PointD TopRight, PointD BottomRight, PointD BottomLeft)
{
    public PointD[] Points => [TopLeft, TopRight, BottomRight, BottomLeft];
}

/// 3x3 projective transform (mac `AnnotationHomography` parity). Maps points
/// as x' = (a·x + b·y + c) / (g·x + h·y + i).
public readonly record struct Homography(
    double A, double B, double C,
    double D, double E, double F,
    double G, double H, double I)
{
    public static readonly Homography Identity = new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    public static Homography? Mapping(RectD sourceRect, CameraQuad quad)
    {
        if (sourceRect.Width <= 0 || sourceRect.Height <= 0)
        {
            return null;
        }

        var p0 = quad.TopLeft;
        var p1 = quad.TopRight;
        var p2 = quad.BottomRight;
        var p3 = quad.BottomLeft;

        double dx1 = p1.X - p2.X;
        double dx2 = p3.X - p2.X;
        double dx3 = p0.X - p1.X + p2.X - p3.X;
        double dy1 = p1.Y - p2.Y;
        double dy2 = p3.Y - p2.Y;
        double dy3 = p0.Y - p1.Y + p2.Y - p3.Y;

        Homography unit;
        double denominator = dx1 * dy2 - dx2 * dy1;
        if (Math.Abs(dx3) < 0.000001 && Math.Abs(dy3) < 0.000001)
        {
            unit = new Homography(
                p1.X - p0.X, p3.X - p0.X, p0.X,
                p1.Y - p0.Y, p3.Y - p0.Y, p0.Y,
                0, 0, 1);
        }
        else
        {
            if (Math.Abs(denominator) <= 0.000001)
            {
                return null;
            }

            double projectiveX = (dx3 * dy2 - dx2 * dy3) / denominator;
            double projectiveY = (dx1 * dy3 - dx3 * dy1) / denominator;
            unit = new Homography(
                p1.X - p0.X + projectiveX * p1.X,
                p3.X - p0.X + projectiveY * p3.X,
                p0.X,
                p1.Y - p0.Y + projectiveX * p1.Y,
                p3.Y - p0.Y + projectiveY * p3.Y,
                p0.Y,
                projectiveX,
                projectiveY,
                1);
        }

        double inverseWidth = 1 / sourceRect.Width;
        double inverseHeight = 1 / sourceRect.Height;
        return new Homography(
            unit.A * inverseWidth,
            unit.B * inverseHeight,
            unit.C - unit.A * sourceRect.MinX * inverseWidth - unit.B * sourceRect.MinY * inverseHeight,
            unit.D * inverseWidth,
            unit.E * inverseHeight,
            unit.F - unit.D * sourceRect.MinX * inverseWidth - unit.E * sourceRect.MinY * inverseHeight,
            unit.G * inverseWidth,
            unit.H * inverseHeight,
            unit.I - unit.G * sourceRect.MinX * inverseWidth - unit.H * sourceRect.MinY * inverseHeight);
    }

    public PointD Apply(PointD point)
    {
        double denominator = G * point.X + H * point.Y + I;
        if (Math.Abs(denominator) <= 0.000001)
        {
            return point;
        }

        return new PointD(
            (A * point.X + B * point.Y + C) / denominator,
            (D * point.X + E * point.Y + F) / denominator);
    }

    public Homography? Inverted()
    {
        double determinant = A * (E * I - F * H)
            - B * (D * I - F * G)
            + C * (D * H - E * G);
        if (Math.Abs(determinant) <= 0.000001)
        {
            return null;
        }

        double reciprocal = 1 / determinant;
        return new Homography(
            (E * I - F * H) * reciprocal,
            (C * H - B * I) * reciprocal,
            (B * F - C * E) * reciprocal,
            (F * G - D * I) * reciprocal,
            (A * I - C * G) * reciprocal,
            (C * D - A * F) * reciprocal,
            (D * H - E * G) * reciprocal,
            (B * G - A * H) * reciprocal,
            (A * E - B * D) * reciprocal);
    }
}

/// Camera projection for the screenshot card (mac `AnnotationCameraProjection`
/// + `AnnotationCameraGeometry` parity). Projects the flat canvas onto a
/// perspective quad; preview hit-testing and export share one geometry.
public sealed class CameraProjection
{
    public CameraQuad Quad { get; }

    private readonly Homography _forward;
    private readonly Homography _inverse;

    private CameraProjection(RectD sourceRect, CameraQuad quad, Homography forward, Homography inverse)
    {
        Quad = quad;
        _forward = forward;
        _inverse = inverse;
    }

    public static CameraProjection Create(RectD sourceRect, CameraQuad quad)
    {
        var transform = Homography.Mapping(sourceRect, quad);
        var inverted = transform?.Inverted();
        if (transform is not null && inverted is not null)
        {
            return new CameraProjection(sourceRect, quad, transform.Value, inverted.Value);
        }

        // Preview hit-testing and export must fail as one unit. Keeping a
        // transformed quad with identity homographies would make them render
        // and interact with different geometry.
        var identityQuad = new CameraQuad(
            new PointD(sourceRect.MinX, sourceRect.MinY),
            new PointD(sourceRect.MaxX, sourceRect.MinY),
            new PointD(sourceRect.MaxX, sourceRect.MaxY),
            new PointD(sourceRect.MinX, sourceRect.MaxY));
        return new CameraProjection(sourceRect, identityQuad, Homography.Identity, Homography.Identity);
    }

    public PointD Project(PointD point) => _forward.Apply(point);

    public PointD Unproject(PointD point) => _inverse.Apply(point);

    public Homography Forward => _forward;
}

public static class CameraGeometry
{
    public static CameraProjection Projection(
        RectD sourceRect,
        RectD imageRect,
        SizeD canvasSize,
        CameraSettings settings)
    {
        if (sourceRect.Width <= 0 || sourceRect.Height <= 0
            || imageRect.Width <= 0 || imageRect.Height <= 0)
        {
            return CameraProjection.Create(sourceRect, QuadFor(sourceRect));
        }

        var pivot = new PointD(imageRect.MidX, imageRect.MidY);
        double scale;
        if (settings.ProjectionVersion < 2)
        {
            var rawImageCorners = CornersOf(imageRect)
                .Select(p => LegacyProjectedPoint(p, pivot, imageRect, settings))
                .ToArray();
            var rawBounds = BoundingRect(rawImageCorners);
            double fitScale = Math.Min(
                1,
                Math.Min(
                    imageRect.Width / Math.Max(rawBounds.Width, 1),
                    imageRect.Height / Math.Max(rawBounds.Height, 1)));
            scale = fitScale * Math.Clamp(settings.Zoom, 0.4, 2.5);
        }
        else
        {
            // Zoom is the only framing scale in the current model. Do not
            // silently shrink the card as its angle changes.
            scale = Math.Clamp(settings.Zoom, 0.4, 2.5);
        }

        double panWidth = settings.PanX * canvasSize.Width;
        double panHeight = settings.PanY * canvasSize.Height;

        PointD Projected(PointD point)
        {
            var rawPoint = settings.ProjectionVersion < 2
                ? LegacyProjectedPoint(point, pivot, imageRect, settings)
                : CurrentProjectedPoint(point, pivot, imageRect, settings);
            return new PointD(
                pivot.X + (rawPoint.X - pivot.X) * scale + panWidth,
                pivot.Y + (rawPoint.Y - pivot.Y) * scale + panHeight);
        }

        var sourceCorners = CornersOf(sourceRect);
        var destinationQuad = new CameraQuad(
            Projected(sourceCorners[0]),
            Projected(sourceCorners[1]),
            Projected(sourceCorners[2]),
            Projected(sourceCorners[3]));
        return CameraProjection.Create(sourceRect, destinationQuad);
    }

    private static PointD CurrentProjectedPoint(
        PointD point, PointD pivot, RectD imageRect, CameraSettings settings)
    {
        var vector = new Vector3(point.X - pivot.X, point.Y - pivot.Y, 0);

        // Rotate turns the card around its local center axes.
        vector = RotatedAroundX(vector, Radians(settings.RotationXDegrees));
        vector = RotatedAroundY(vector, Radians(settings.RotationYDegrees));

        // Tilt orbits the camera horizontally/vertically around the same
        // center. Applying the inverse world rotations to the card plane gives
        // the exact pinhole-camera view while remaining one invertible plane.
        vector = RotatedAroundY(vector, -Radians(settings.TiltXDegrees));
        vector = RotatedAroundX(vector, -Radians(settings.TiltYDegrees));

        return PerspectivePoint(vector, pivot, imageRect, settings.FieldOfViewDegrees, settings.RollDegrees);
    }

    /// Version 1 compatibility for saved development presets/documents.
    private static PointD LegacyProjectedPoint(
        PointD point, PointD pivot, RectD imageRect, CameraSettings settings)
    {
        var vector = new Vector3(point.X - pivot.X, point.Y - pivot.Y, 0);

        vector = RotatedAroundX(vector, Radians(settings.RotationXDegrees));
        vector = RotatedAroundY(vector, Radians(settings.RotationYDegrees));

        double tiltX = Math.Tan(Radians(settings.TiltXDegrees)) * 0.42;
        double tiltY = Math.Tan(Radians(settings.TiltYDegrees)) * 0.42;
        double tiltedX = vector.X + vector.Y * tiltY;
        double tiltedY = vector.Y + vector.X * tiltX;
        vector = vector with { X = tiltedX, Y = tiltedY };

        return PerspectivePoint(vector, pivot, imageRect, settings.FieldOfViewDegrees, settings.RollDegrees);
    }

    private static PointD PerspectivePoint(
        Vector3 vector, PointD pivot, RectD imageRect, double fieldOfViewDegrees, double rollDegrees)
    {
        double fov = Math.Clamp(fieldOfViewDegrees, 18, 80);
        double longestEdge = Math.Max(imageRect.Width, imageRect.Height);
        double focalDistance = longestEdge * (0.35 + 0.5 / Math.Max(Math.Tan(Radians(fov) / 2), 0.01));
        double denominator = Math.Max(focalDistance - vector.Z, focalDistance * 0.12);
        double perspectiveScale = focalDistance / denominator;
        double x = vector.X * perspectiveScale;
        double y = vector.Y * perspectiveScale;

        double roll = Radians(rollDegrees);
        double rolledX = x * Math.Cos(roll) - y * Math.Sin(roll);
        double rolledY = x * Math.Sin(roll) + y * Math.Cos(roll);
        return new PointD(pivot.X + rolledX, pivot.Y + rolledY);
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;

    private static Vector3 RotatedAroundX(Vector3 vector, double angle) => new(
        vector.X,
        vector.Y * Math.Cos(angle) - vector.Z * Math.Sin(angle),
        vector.Y * Math.Sin(angle) + vector.Z * Math.Cos(angle));

    private static Vector3 RotatedAroundY(Vector3 vector, double angle) => new(
        vector.X * Math.Cos(angle) + vector.Z * Math.Sin(angle),
        vector.Y,
        -vector.X * Math.Sin(angle) + vector.Z * Math.Cos(angle));

    private static PointD[] CornersOf(RectD rect) =>
    [
        new PointD(rect.MinX, rect.MinY),
        new PointD(rect.MaxX, rect.MinY),
        new PointD(rect.MaxX, rect.MaxY),
        new PointD(rect.MinX, rect.MaxY),
    ];

    private static CameraQuad QuadFor(RectD rect)
    {
        var points = CornersOf(rect);
        return new CameraQuad(points[0], points[1], points[2], points[3]);
    }

    private static RectD BoundingRect(PointD[] points)
    {
        if (points.Length == 0)
        {
            return default;
        }

        double minX = points.Min(p => p.X);
        double minY = points.Min(p => p.Y);
        double maxX = points.Max(p => p.X);
        double maxY = points.Max(p => p.Y);
        return new RectD(minX, minY, maxX - minX, maxY - minY);
    }

    private readonly record struct Vector3(double X, double Y, double Z);
}
