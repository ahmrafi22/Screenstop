namespace Screenstop.Core.Background;

public enum ProgressiveBlurMode
{
    Radial,
    Directional,
}

public enum ProgressiveBlurEdgeMode
{
    /// Blur is applied to the screenshot only, clipped to its bounds.
    Clipped,

    /// Blur is applied to the whole rendered scene, bleeding past the card.
    Bleed,
}

public static class ProgressiveBlurModeExtensions
{
    public static string Title(this ProgressiveBlurMode mode) => mode switch
    {
        ProgressiveBlurMode.Radial => "Radial",
        ProgressiveBlurMode.Directional => "Directional",
        _ => mode.ToString(),
    };

    public static string Title(this ProgressiveBlurEdgeMode mode) => mode switch
    {
        ProgressiveBlurEdgeMode.Clipped => "Screenshot",
        ProgressiveBlurEdgeMode.Bleed => "Scene",
        _ => mode.ToString(),
    };
}

/// Depth-of-field style focus blur (mac `AnnotationProgressiveBlurSettings`).
public sealed class ProgressiveBlurSettings : IEquatable<ProgressiveBlurSettings>
{
    public bool IsEnabled { get; set; }

    public ProgressiveBlurEdgeMode EdgeMode { get; set; } = ProgressiveBlurEdgeMode.Bleed;

    public ProgressiveBlurMode Mode { get; set; } = ProgressiveBlurMode.Radial;

    /// Maximum blur radius, normalized by the preview/export scale at render time.
    public double Strength { get; set; } = 18;

    /// Width of the transition from sharp to blurred, normalized to 0...1.
    public double Falloff { get; set; } = 0.55;

    /// Size of the sharp focal area, normalized from a small detail to the
    /// farthest image edge.
    public double FocusSize { get; set; } = 0.45;

    /// Top-left-origin normalized focal point within the active blur layer.
    public double FocusX { get; set; } = 0.5;

    public double FocusY { get; set; } = 0.5;

    /// Direction of the in-focus band. Zero degrees is horizontal.
    public double DirectionDegrees { get; set; }

    public bool IsActive => IsEnabled && Strength > 0.01;

    public bool Equals(ProgressiveBlurSettings? other) =>
        other is not null
        && IsEnabled == other.IsEnabled
        && EdgeMode == other.EdgeMode
        && Mode == other.Mode
        && Strength == other.Strength
        && Falloff == other.Falloff
        && FocusSize == other.FocusSize
        && FocusX == other.FocusX
        && FocusY == other.FocusY
        && DirectionDegrees == other.DirectionDegrees;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(IsEnabled);
        hash.Add(EdgeMode);
        hash.Add(Mode);
        hash.Add(Strength);
        hash.Add(Falloff);
        hash.Add(FocusSize);
        hash.Add(FocusX);
        hash.Add(FocusY);
        hash.Add(DirectionDegrees);
        return hash.ToHashCode();
    }

    public ProgressiveBlurSettings Clone() => new()
    {
        IsEnabled = IsEnabled,
        EdgeMode = EdgeMode,
        Mode = Mode,
        Strength = Strength,
        Falloff = Falloff,
        FocusSize = FocusSize,
        FocusX = FocusX,
        FocusY = FocusY,
        DirectionDegrees = DirectionDegrees,
    };
}

/// Tiled rotated text watermark across the whole canvas.
public sealed class WatermarkSettings : IEquatable<WatermarkSettings>
{
    public bool IsEnabled { get; set; }

    public string Text { get; set; } = string.Empty;

    public double Density { get; set; } = 4;

    public double FontSize { get; set; } = 72;

    public double RotationDegrees { get; set; } = 45;

    public double Opacity { get; set; } = 0.18;

    public RgbaColor Color { get; set; } = RgbaColor.Mercury;

    public bool IsVisible => !string.IsNullOrWhiteSpace(Text) && Opacity > 0;

    public bool Equals(WatermarkSettings? other) =>
        other is not null
        && IsEnabled == other.IsEnabled
        && Text == other.Text
        && Density == other.Density
        && FontSize == other.FontSize
        && RotationDegrees == other.RotationDegrees
        && Opacity == other.Opacity
        && Color == other.Color;

    public override int GetHashCode() => HashCode.Combine(
        IsEnabled, Text, Density, FontSize, RotationDegrees, Opacity, Color);

    public WatermarkSettings Clone() => new()
    {
        IsEnabled = IsEnabled,
        Text = Text,
        Density = Density,
        FontSize = FontSize,
        RotationDegrees = RotationDegrees,
        Opacity = Opacity,
        Color = Color,
    };
}
