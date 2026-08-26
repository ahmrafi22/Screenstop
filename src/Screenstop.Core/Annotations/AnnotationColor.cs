namespace Screenstop.Core.Annotations;

/// RGBA color stored as 0..1 components, serializable and UI-framework-free.
public readonly record struct AnnotationColor(double R, double G, double B, double A = 1.0)
{
    public static readonly AnnotationColor Red = new(1, 0.23, 0.19);
    public static readonly AnnotationColor Yellow = new(1, 0.8, 0);
    public static readonly AnnotationColor Green = new(0.2, 0.78, 0.35);
    public static readonly AnnotationColor Blue = new(0.0, 0.48, 0.96);
    public static readonly AnnotationColor Black = new(0, 0, 0);
    public static readonly AnnotationColor White = new(1, 1, 1);

    public static IReadOnlyList<AnnotationColor> Palette { get; } =
        new[] { Red, Yellow, Green, Blue, Black, White };

    public byte R255 => (byte)Math.Round(Math.Clamp(R, 0, 1) * 255);
    public byte G255 => (byte)Math.Round(Math.Clamp(G, 0, 1) * 255);
    public byte B255 => (byte)Math.Round(Math.Clamp(B, 0, 1) * 255);
    public byte A255 => (byte)Math.Round(Math.Clamp(A, 0, 1) * 255);
}
