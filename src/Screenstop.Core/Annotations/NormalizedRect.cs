namespace Screenstop.Core.Annotations;

/// An axis-aligned rect in normalized image space ([0, 1] on both axes).
public readonly record struct NormalizedRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + (Width / 2);

    public double CenterY => Y + (Height / 2);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static NormalizedRect FromPoints(double x1, double y1, double x2, double y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));

    public bool Contains(double nx, double ny) =>
        nx >= X && nx < Right && ny >= Y && ny < Bottom;

    public NormalizedRect Inflate(double amount) =>
        new(X - amount, Y - amount, Width + (amount * 2), Height + (amount * 2));

    public NormalizedRect Translate(double dx, double dy) => new(X + dx, Y + dy, Width, Height);

    public NormalizedRect ScaleAround(double anchorX, double anchorY, double factorX, double factorY) =>
        new(
            anchorX + ((X - anchorX) * factorX),
            anchorY + ((Y - anchorY) * factorY),
            Width * factorX,
            Height * factorY);

    /// Clamps the rect into the [0, 1] unit square.
    public NormalizedRect ClampToUnit()
    {
        double x = Math.Clamp(X, 0, 1);
        double y = Math.Clamp(Y, 0, 1);
        double right = Math.Clamp(Right, 0, 1);
        double bottom = Math.Clamp(Bottom, 0, 1);
        return new NormalizedRect(x, y, right - x, bottom - y);
    }
}
