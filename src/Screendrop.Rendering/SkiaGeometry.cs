using Screendrop.Core.Background;
using SkiaSharp;

namespace Screendrop.Rendering;

internal static class SkiaGeometry
{
    public static SKRect ToSK(this RectD rect) =>
        new((float)rect.X, (float)rect.Y, (float)rect.MaxX, (float)rect.MaxY);

    public static SKPoint ToSK(this PointD point) => new((float)point.X, (float)point.Y);

    /// Rounded rectangle with individual corner radii (mac `PerCornerRadii.path`
    /// parity; square corners emit straight joins).
    public static SKPath PerCornerPath(RectD rect, PerCornerRadii radii)
    {
        double maxRadius = Math.Min(rect.Width, rect.Height) / 2;
        float tl = (float)Math.Min(radii.TopLeft, maxRadius);
        float tr = (float)Math.Min(radii.TopRight, maxRadius);
        float bl = (float)Math.Min(radii.BottomLeft, maxRadius);
        float br = (float)Math.Min(radii.BottomRight, maxRadius);

        var path = new SKPath();
        var skRect = rect.ToSK();

        if (tl == tr && tr == bl && bl == br)
        {
            path.AddRoundRect(skRect, tl, tl);
            return path;
        }

        path.MoveTo(skRect.Left + tl, skRect.Top);
        path.LineTo(skRect.Right - tr, skRect.Top);
        if (tr > 0)
        {
            path.ArcTo(skRect.Right - tr, skRect.Top, skRect.Right, skRect.Top + tr, tr);
        }

        path.LineTo(skRect.Right, skRect.Bottom - br);
        if (br > 0)
        {
            path.ArcTo(skRect.Right, skRect.Bottom - br, skRect.Right - br, skRect.Bottom, br);
        }

        path.LineTo(skRect.Left + bl, skRect.Bottom);
        if (bl > 0)
        {
            path.ArcTo(skRect.Left + bl, skRect.Bottom, skRect.Left, skRect.Bottom - bl, bl);
        }

        path.LineTo(skRect.Left, skRect.Top + tl);
        if (tl > 0)
        {
            path.ArcTo(skRect.Left, skRect.Top + tl, skRect.Left + tl, skRect.Top, tl);
        }

        path.Close();
        return path;
    }
}
