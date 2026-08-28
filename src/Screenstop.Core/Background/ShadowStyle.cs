namespace Screenstop.Core.Background;

/// Card shadow shape (mac `AnnotationShadowStyle` parity, ported from Framekit).
/// Each style rescales the same base drop.
public enum ShadowStyle
{
    Soft,
    Long,
    Glow,
    Crisp,
}

public readonly record struct ShadowLayer(double YOffset, double Radius, double Alpha)
{
    public double CoreGraphicsBlur => Radius * 2;
}

public static class ShadowStyleExtensions
{
    public static string Title(this ShadowStyle style) => style switch
    {
        ShadowStyle.Soft => "Soft",
        ShadowStyle.Long => "Long",
        ShadowStyle.Glow => "Glow",
        ShadowStyle.Crisp => "Crisp",
        _ => style.ToString(),
    };

    public static double RadiusScale(this ShadowStyle style) => style switch
    {
        ShadowStyle.Soft => 1,
        ShadowStyle.Long => 1.2,
        ShadowStyle.Glow => 1.6,
        ShadowStyle.Crisp => 0.8,
        _ => 1,
    };

    public static double YOffsetScale(this ShadowStyle style) => style switch
    {
        ShadowStyle.Soft => 0.3,
        ShadowStyle.Long => 0.9,
        ShadowStyle.Glow => 0,
        ShadowStyle.Crisp => 0.2,
        _ => 0.3,
    };

    public static double OpacityScale(this ShadowStyle style) => style switch
    {
        ShadowStyle.Soft => 1,
        ShadowStyle.Long => 0.85,
        ShadowStyle.Glow => 0.7,
        ShadowStyle.Crisp => 1.1,
        _ => 1,
    };

    /// The slider mostly grows the drop rather than darkening it: opacity
    /// reaches its ceiling early and stays there, which keeps a large shadow
    /// from turning into a black smear.
    public static ShadowLayer? Layer(this ShadowStyle style, double strength, double referenceEdge)
    {
        strength = Math.Clamp(strength, 0, 1);
        if (strength <= 0 || referenceEdge <= 0)
        {
            return null;
        }

        double radius = referenceEdge * 0.17 * strength * style.RadiusScale();
        double alpha = Math.Min(0.5, Math.Min(0.35, 0.08 + strength * 1.35) * style.OpacityScale());
        return new ShadowLayer(radius * style.YOffsetScale(), radius, alpha);
    }
}
