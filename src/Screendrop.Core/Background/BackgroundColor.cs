namespace Screendrop.Core.Background;

/// A named solid background color (mac `AnnotationBackgroundColor` parity).
/// Components are sRGB 0..1.
public sealed record BackgroundColor(string ColorId, string Title, double R, double G, double B, double A = 1.0)
{
    public static readonly BackgroundColor Black = new("black", "Black", 0.02, 0.02, 0.024);
    public static readonly BackgroundColor White = new("white", "White", 0.96, 0.96, 0.94);
    public static readonly BackgroundColor Graphite = new("graphite", "Graphite", 0.17, 0.18, 0.21);
    public static readonly BackgroundColor Red = new("red", "Red", 0.94, 0.23, 0.28);
    public static readonly BackgroundColor Orange = new("orange", "Orange", 0.97, 0.52, 0.16);
    public static readonly BackgroundColor Yellow = new("yellow", "Yellow", 0.96, 0.73, 0.23);
    public static readonly BackgroundColor Green = new("green", "Green", 0.23, 0.61, 0.36);
    public static readonly BackgroundColor Blue = new("blue", "Blue", 0.16, 0.50, 0.88);
    public static readonly BackgroundColor Purple = new("purple", "Purple", 0.48, 0.26, 0.91);
    public static readonly BackgroundColor Blush = new("blush", "Blush", 0.93, 0.66, 0.62);
    public static readonly BackgroundColor Mint = new("mint", "Mint", 0.66, 0.90, 0.73);
    public static readonly BackgroundColor Sky = new("sky", "Sky", 0.63, 0.79, 0.94);
    public static readonly BackgroundColor Lavender = new("lavender", "Lavender", 0.80, 0.76, 0.92);
    public static readonly BackgroundColor Peach = new("peach", "Peach", 0.98, 0.80, 0.69);
    public static readonly BackgroundColor Sage = new("sage", "Sage", 0.74, 0.82, 0.70);
    public static readonly BackgroundColor Sand = new("sand", "Sand", 0.91, 0.87, 0.76);

    public static readonly IReadOnlyList<BackgroundColor> PlainPresets = new[]
    {
        Black, White, Graphite, Red, Orange, Yellow,
        Green, Blue, Purple, Blush, Mint, Sky,
        Lavender, Peach, Sage, Sand,
    };

    public static BackgroundColor? ByColorId(string? id) =>
        PlainPresets.FirstOrDefault(c => c.ColorId == id);
}
