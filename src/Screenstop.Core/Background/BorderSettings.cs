namespace Screenstop.Core.Background;

/// sRGB 0..1 color used by the border and watermark (freeform, unlike fills).
public readonly record struct RgbaColor(double Red, double Green, double Blue, double Alpha = 1.0)
{
    public static readonly RgbaColor White = new(0.96, 0.96, 0.96);
    public static readonly RgbaColor Mercury = new(0.90, 0.90, 0.90);
    public static readonly RgbaColor Black = new(0.02, 0.02, 0.024);
}

/// Annotation swatch library (mac `AnnotationSwatch` parity) used for the
/// screenshot border color strip.
public static class SwatchLibrary
{
    public static readonly IReadOnlyList<(string Id, string Title, RgbaColor Color)> Swatches = new[]
    {
        ("black", "Black", new RgbaColor(0.02, 0.02, 0.024)),
        ("red", "Red", new RgbaColor(0.97, 0.22, 0.20)),
        ("orange", "Orange", new RgbaColor(1.0, 0.53, 0.08)),
        ("yellow", "Yellow", new RgbaColor(1.0, 0.82, 0.18)),
        ("green", "Green", new RgbaColor(0.18, 0.72, 0.36)),
        ("turquoise", "Turquoise", new RgbaColor(0.20, 0.77, 0.72)),
        ("blue", "Blue", new RgbaColor(0.18, 0.48, 1.0)),
        ("purple", "Purple", new RgbaColor(0.55, 0.30, 0.95)),
        ("pink", "Pink", new RgbaColor(1.0, 0.18, 0.43)),
        ("white", "White", new RgbaColor(0.96, 0.96, 0.96)),
    };
}

/// Rendering style for the outer border ring.
public enum BorderStyle
{
    /// Flat, single-color ring.
    Solid,

    /// Glassmorphism: translucent ring with a top-left→bottom-right brightness
    /// gradient plus an inner bevel highlight/shadow (thin pane-of-glass look).
    Glass,
}

public static class BorderStyleExtensions
{
    public static string Title(this BorderStyle style) => style switch
    {
        BorderStyle.Solid => "Solid",
        BorderStyle.Glass => "Glass",
        _ => style.ToString(),
    };
}

/// Outer screenshot border ring. Thickness is a fraction of the screenshot's
/// shortest edge so presets keep the same visual weight across capture sizes.
public sealed class BorderSettings : IEquatable<BorderSettings>
{
    public bool IsEnabled { get; set; }

    public BorderStyle Style { get; set; } = BorderStyle.Solid;

    public RgbaColor Color { get; set; } = RgbaColor.White;

    public double Thickness { get; set; } = 0.012;

    public double Opacity { get; set; } = 1;

    public bool IsVisible => IsEnabled && Thickness > 0.0001 && Opacity > 0.0001;

    public double PixelThickness(double imageSizeWidth, double imageSizeHeight)
    {
        if (!IsVisible || imageSizeWidth <= 0 || imageSizeHeight <= 0)
        {
            return 0;
        }

        return Math.Max(0, Thickness) * Math.Min(imageSizeWidth, imageSizeHeight);
    }

    public bool Equals(BorderSettings? other) =>
        other is not null
        && IsEnabled == other.IsEnabled
        && Style == other.Style
        && Color == other.Color
        && Thickness == other.Thickness
        && Opacity == other.Opacity;

    public override int GetHashCode() => HashCode.Combine(IsEnabled, Style, Color, Thickness, Opacity);

    public BorderSettings Clone() => new()
    {
        IsEnabled = IsEnabled,
        Style = Style,
        Color = Color,
        Thickness = Thickness,
        Opacity = Opacity,
    };
}
