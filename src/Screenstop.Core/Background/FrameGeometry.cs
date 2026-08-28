namespace Screenstop.Core.Background;

public readonly record struct PerCornerRadii(double TopLeft, double TopRight, double BottomLeft, double BottomRight)
{
    public bool IsUniform => TopLeft == TopRight && TopRight == BottomLeft && BottomLeft == BottomRight;

    /// Corners with a zero radius stay square when outset (stuck canvas edges).
    public PerCornerRadii Outset(double amount) => new(
        TopLeft > 0 ? TopLeft + amount : 0,
        TopRight > 0 ? TopRight + amount : 0,
        BottomLeft > 0 ? BottomLeft + amount : 0,
        BottomRight > 0 ? BottomRight + amount : 0);
}

/// Shared preview/export geometry for the rounded screenshot and its optional
/// outer border (mac `AnnotationScreenshotFrameGeometry` parity).
public sealed class FrameGeometry
{
    public RectD ImageRect { get; }

    public RectD CardRect { get; }

    public PerCornerRadii ImageCornerRadii { get; }

    public PerCornerRadii CardCornerRadii { get; }

    public double BorderWidth { get; }

    public FrameGeometry(RectD imageRect, RectD cardRect, BackgroundSettings settings)
    {
        ImageRect = imageRect;
        CardRect = cardRect;

        double horizontalInset = Math.Max(0, (cardRect.Width - imageRect.Width) / 2);
        double verticalInset = Math.Max(0, (cardRect.Height - imageRect.Height) / 2);
        BorderWidth = settings.Border.IsVisible
            ? Math.Min(horizontalInset, verticalInset)
            : 0;

        double baseImageRadius = settings.RequiresCanvasLayout
            ? Math.Max(0, settings.CornerRadius) * Math.Min(imageRect.Width, imageRect.Height)
            : 0;
        var multipliers = settings.EffectiveCanvasAlignment.CornerRadiusMultipliers();
        ImageCornerRadii = new PerCornerRadii(
            baseImageRadius * multipliers.TopLeft,
            baseImageRadius * multipliers.TopRight,
            baseImageRadius * multipliers.BottomLeft,
            baseImageRadius * multipliers.BottomRight);

        // A rounded outer border stays concentric with the clipped screenshot:
        // add the ring width to rounded corners, while intentionally square
        // corners (including stuck canvas edges) remain square.
        double baseCardRadius = baseImageRadius > 0 ? baseImageRadius + BorderWidth : 0;
        CardCornerRadii = new PerCornerRadii(
            baseCardRadius * multipliers.TopLeft,
            baseCardRadius * multipliers.TopRight,
            baseCardRadius * multipliers.BottomLeft,
            baseCardRadius * multipliers.BottomRight);
    }

    /// The exporter's shadow caster is clipped outside this slightly expanded
    /// path. The one-pixel outset removes antialiased caster residue without
    /// visibly separating the soft shadow from the card.
    public PerCornerRadii ImageShadowKnockoutRadii => ImageCornerRadii.Outset(1);

    public PerCornerRadii CardShadowKnockoutRadii => CardCornerRadii.Outset(1);

    public RectD ImageShadowKnockoutRect => ImageRect.Inset(-1, -1);

    public RectD CardShadowKnockoutRect => CardRect.Inset(-1, -1);
}
