namespace Screendrop.Core.Background;

/// The full mockup stage for a screenshot: fill, layout, camera, focus blur,
/// border, and watermark (mac `AnnotationBackgroundSettings` parity).
public sealed class BackgroundSettings : IEquatable<BackgroundSettings>
{
    public BackgroundStyle Style { get; set; } = BackgroundStyle.None();

    public double Padding { get; set; } = 0.08;

    public double CornerRadius { get; set; } = 0.018;

    public double Shadow { get; set; } = 0.36;

    public ShadowStyle ShadowStyle { get; set; } = ShadowStyle.Soft;

    public BorderSettings Border { get; set; } = new();

    public BackgroundAspectRatio AspectRatio { get; set; } = BackgroundAspectRatio.Auto;

    public BackgroundAlignment Alignment { get; set; } = BackgroundAlignment.Center;

    public CameraSettings Camera { get; set; } = new();

    public ProgressiveBlurSettings ProgressiveBlur { get; set; } = new();

    public WatermarkSettings Watermark { get; set; } = new();

    public bool IsEnabled => Style.Kind != BackgroundStyleKind.None;

    /// Camera transforms and scene blur need a stage even without an explicit
    /// background so their pixels have transparent breathing room instead of
    /// being cropped to the original screenshot bounds.
    public bool UsesCanvasLayout =>
        IsEnabled
        || Camera.HasEffect
        || (ProgressiveBlur.IsActive && ProgressiveBlur.EdgeMode == ProgressiveBlurEdgeMode.Bleed);

    /// An outer screenshot border needs a canvas large enough to preserve the
    /// ring even when no fill, camera transform, or scene blur is active.
    public bool RequiresCanvasLayout => UsesCanvasLayout || Border.IsVisible;

    /// Once the card leaves the flat plane, "stuck" edges no longer describe
    /// the projected geometry. Camera pan replaces alignment for that mode.
    public BackgroundAlignment EffectiveCanvasAlignment =>
        Camera.HasEffect || !UsesCanvasLayout ? BackgroundAlignment.Center : Alignment;

    public bool HasRenderableContent =>
        IsEnabled
        || Camera.HasEffect
        || ProgressiveBlur.IsActive
        || Border.IsVisible
        || Watermark.IsVisible;

    /// Preset comparison ignores hidden recent-wallpaper state and disabled
    /// blur/border so an otherwise identical preset doesn't appear edited.
    public BackgroundSettings PresetComparable()
    {
        var comparable = Clone();
        if (comparable.Style.Kind != BackgroundStyleKind.Wallpaper)
        {
            comparable.Style = comparable.Style.Kind switch
            {
                BackgroundStyleKind.Solid => BackgroundStyle.Solid(comparable.Style.ColorId ?? string.Empty),
                BackgroundStyleKind.Gradient => BackgroundStyle.Gradient(comparable.Style.GradientId ?? string.Empty),
                _ => BackgroundStyle.None(),
            };
        }

        if (!comparable.ProgressiveBlur.IsEnabled)
        {
            comparable.ProgressiveBlur = new ProgressiveBlurSettings();
        }

        if (!comparable.Border.IsEnabled)
        {
            comparable.Border = new BorderSettings();
        }

        return comparable;
    }

    public bool Equals(BackgroundSettings? other)
    {
        if (other is null)
        {
            return false;
        }

        return Style.Equals(other.Style)
            && Padding == other.Padding
            && CornerRadius == other.CornerRadius
            && Shadow == other.Shadow
            && ShadowStyle == other.ShadowStyle
            && Border.Equals(other.Border)
            && AspectRatio == other.AspectRatio
            && Alignment == other.Alignment
            && Camera.Equals(other.Camera)
            && ProgressiveBlur.Equals(other.ProgressiveBlur)
            && Watermark.Equals(other.Watermark);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Style);
        hash.Add(Padding);
        hash.Add(CornerRadius);
        hash.Add(Shadow);
        hash.Add(ShadowStyle);
        hash.Add(Border);
        hash.Add(AspectRatio);
        hash.Add(Alignment);
        hash.Add(Camera);
        hash.Add(ProgressiveBlur);
        hash.Add(Watermark);
        return hash.ToHashCode();
    }

    public BackgroundSettings Clone() => new()
    {
        Style = new BackgroundStyle
        {
            Kind = Style.Kind,
            ColorId = Style.ColorId,
            GradientId = Style.GradientId,
            WallpaperPath = Style.WallpaperPath,
        },
        Padding = Padding,
        CornerRadius = CornerRadius,
        Shadow = Shadow,
        ShadowStyle = ShadowStyle,
        Border = Border.Clone(),
        AspectRatio = AspectRatio,
        Alignment = Alignment,
        Camera = Camera.Clone(),
        ProgressiveBlur = ProgressiveBlur.Clone(),
        Watermark = Watermark.Clone(),
    };
}
