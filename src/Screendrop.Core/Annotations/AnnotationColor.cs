namespace Screendrop.Core.Annotations;

/// RGBA color stored as 0..1 components, serializable and UI-framework-free.
///
/// The named palette mirrors the mac app's AnnotationSwatch set exactly
/// (same order, same sRGB values) so captures annotate with identical
/// colors on both platforms.
public readonly record struct AnnotationColor(double R, double G, double B, double A = 1.0)
{
    public static readonly AnnotationColor Black = new(0.02, 0.02, 0.024);
    public static readonly AnnotationColor Red = new(0.97, 0.22, 0.2);
    public static readonly AnnotationColor Orange = new(1.0, 0.53, 0.08);
    public static readonly AnnotationColor Yellow = new(1, 0.82, 0.18);
    public static readonly AnnotationColor Green = new(0.18, 0.72, 0.36);
    public static readonly AnnotationColor Turquoise = new(0.20, 0.77, 0.72);
    public static readonly AnnotationColor Blue = new(0.18, 0.48, 1);
    public static readonly AnnotationColor Purple = new(0.55, 0.30, 0.95);
    public static readonly AnnotationColor Pink = new(1.0, 0.18, 0.43);
    public static readonly AnnotationColor White = new(0.96, 0.96, 0.96);

    /// mac AnnotationSwatch.allCases order: black..white.
    public static IReadOnlyList<AnnotationColor> Palette { get; } =
        new[] { Black, Red, Orange, Yellow, Green, Turquoise, Blue, Purple, Pink, White };

    public byte R255 => (byte)Math.Round(Math.Clamp(R, 0, 1) * 255);

    public byte G255 => (byte)Math.Round(Math.Clamp(G, 0, 1) * 255);

    public byte B255 => (byte)Math.Round(Math.Clamp(B, 0, 1) * 255);

    public byte A255 => (byte)Math.Round(Math.Clamp(A, 0, 1) * 255);
}
