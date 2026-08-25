namespace Screenstop.Core.Geometry;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(int px, int py)
    {
        return px >= X && px < Right && py >= Y && py < Bottom;
    }

    public bool Contains(PixelRect other)
    {
        return !other.IsEmpty
            && other.X >= X
            && other.Y >= Y
            && other.Right <= Right
            && other.Bottom <= Bottom;
    }

    public bool Intersects(PixelRect other)
    {
        return !IsEmpty && !other.IsEmpty
            && X < other.Right
            && Right > other.X
            && Y < other.Bottom
            && Bottom > other.Y;
    }

    public static PixelRect Intersect(PixelRect a, PixelRect b)
    {
        int x = Math.Max(a.X, b.X);
        int y = Math.Max(a.Y, b.Y);
        int right = Math.Min(a.Right, b.Right);
        int bottom = Math.Min(a.Bottom, b.Bottom);
        return new PixelRect(x, y, right - x, bottom - y);
    }

    public static PixelRect FromMinMax(int minX, int minY, int maxX, int maxY)
    {
        return new PixelRect(minX, minY, maxX - minX, maxY - minY);
    }
}
