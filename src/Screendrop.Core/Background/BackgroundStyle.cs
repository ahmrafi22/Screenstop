namespace Screendrop.Core.Background;

public enum BackgroundStyleKind
{
    None,
    Solid,
    Gradient,
    Wallpaper,
}

public readonly record struct GradientPoint(double X, double Y)
{
    public static readonly GradientPoint Top = new(0.5, 0);
    public static readonly GradientPoint Bottom = new(0.5, 1);
    public static readonly GradientPoint TopLeading = new(0, 0);
    public static readonly GradientPoint TopTrailing = new(1, 0);
    public static readonly GradientPoint BottomLeading = new(0, 1);
    public static readonly GradientPoint BottomTrailing = new(1, 1);
}

/// Persistable background fill. Solid/gradient fills reference the built-in
/// preset libraries by id (mac parity: fills are never freeform colors).
public sealed class BackgroundStyle : IEquatable<BackgroundStyle>
{
    public BackgroundStyleKind Kind { get; set; } = BackgroundStyleKind.None;

    public string? ColorId { get; set; }

    public string? GradientId { get; set; }

    public string? WallpaperPath { get; set; }

    public static BackgroundStyle None() => new() { Kind = BackgroundStyleKind.None };

    public static BackgroundStyle Solid(string colorId) => new()
    {
        Kind = BackgroundStyleKind.Solid,
        ColorId = colorId,
    };

    public static BackgroundStyle Gradient(string gradientId) => new()
    {
        Kind = BackgroundStyleKind.Gradient,
        GradientId = gradientId,
    };

    public static BackgroundStyle Wallpaper(string path) => new()
    {
        Kind = BackgroundStyleKind.Wallpaper,
        WallpaperPath = path,
    };

    public BackgroundColor? ResolveColor() =>
        Kind == BackgroundStyleKind.Solid ? BackgroundColor.ByColorId(ColorId) : null;

    public BackgroundGradient? ResolveGradient() =>
        Kind == BackgroundStyleKind.Gradient ? BackgroundGradient.ByGradientId(GradientId) : null;

    public bool Equals(BackgroundStyle? other)
    {
        if (other is null)
        {
            return false;
        }

        if (Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            BackgroundStyleKind.Solid => ColorId == other.ColorId,
            BackgroundStyleKind.Gradient => GradientId == other.GradientId,
            BackgroundStyleKind.Wallpaper => string.Equals(
                NormalizePath(WallpaperPath),
                NormalizePath(other.WallpaperPath),
                StringComparison.OrdinalIgnoreCase),
            _ => true,
        };
    }

    public override int GetHashCode() => HashCode.Combine(Kind, ColorId, GradientId, NormalizePath(WallpaperPath));

    private static string NormalizePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path);
}
