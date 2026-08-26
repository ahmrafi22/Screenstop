namespace Screenstop.Core.Annotations;

/// A point in normalized image space: both axes in [0, 1] relative to the
/// source image dimensions. The model layer never uses pixel coordinates
/// (mac invariant: pixel conversion happens only in the renderer/canvas).
public readonly record struct NormalizedPoint(double X, double Y)
{
    public NormalizedPoint Translate(double dx, double dy) => new(X + dx, Y + dy);

    public NormalizedPoint ScaleAround(double anchorX, double anchorY, double factorX, double factorY) =>
        new(anchorX + ((X - anchorX) * factorX), anchorY + ((Y - anchorY) * factorY));
}
