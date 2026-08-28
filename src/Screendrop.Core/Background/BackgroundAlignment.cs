namespace Screendrop.Core.Background;

public enum BackgroundAspectRatio
{
    Auto,
    Square,
    FourThree,
    ThreeTwo,
    SixteenNine,
}

public static class BackgroundAspectRatioExtensions
{
    public static string Title(this BackgroundAspectRatio ratio) => ratio switch
    {
        BackgroundAspectRatio.Auto => "Auto",
        BackgroundAspectRatio.Square => "1:1",
        BackgroundAspectRatio.FourThree => "4:3",
        BackgroundAspectRatio.ThreeTwo => "3:2",
        BackgroundAspectRatio.SixteenNine => "16:9",
        _ => ratio.ToString(),
    };

    public static double? Value(this BackgroundAspectRatio ratio) => ratio switch
    {
        BackgroundAspectRatio.Auto => null,
        BackgroundAspectRatio.Square => 1,
        BackgroundAspectRatio.FourThree => 4.0 / 3.0,
        BackgroundAspectRatio.ThreeTwo => 3.0 / 2.0,
        BackgroundAspectRatio.SixteenNine => 16.0 / 9.0,
        _ => null,
    };
}

public enum BackgroundAlignment
{
    TopLeading,
    Top,
    TopTrailing,
    Leading,
    Center,
    Trailing,
    BottomLeading,
    Bottom,
    BottomTrailing,
}

public static class BackgroundAlignmentExtensions
{
    public static string Title(this BackgroundAlignment alignment) => alignment switch
    {
        BackgroundAlignment.TopLeading => "Top left",
        BackgroundAlignment.Top => "Top",
        BackgroundAlignment.TopTrailing => "Top right",
        BackgroundAlignment.Leading => "Left",
        BackgroundAlignment.Center => "Center",
        BackgroundAlignment.Trailing => "Right",
        BackgroundAlignment.BottomLeading => "Bottom left",
        BackgroundAlignment.Bottom => "Bottom",
        BackgroundAlignment.BottomTrailing => "Bottom right",
        _ => alignment.ToString(),
    };

    public static double XFactor(this BackgroundAlignment alignment) => alignment switch
    {
        BackgroundAlignment.TopLeading or BackgroundAlignment.Leading or BackgroundAlignment.BottomLeading => 0,
        BackgroundAlignment.Top or BackgroundAlignment.Center or BackgroundAlignment.Bottom => 0.5,
        _ => 1,
    };

    public static double YFactor(this BackgroundAlignment alignment) => alignment switch
    {
        BackgroundAlignment.TopLeading or BackgroundAlignment.Top or BackgroundAlignment.TopTrailing => 0,
        BackgroundAlignment.Leading or BackgroundAlignment.Center or BackgroundAlignment.Trailing => 0.5,
        _ => 1,
    };

    public static bool SticksToTop(this BackgroundAlignment alignment) =>
        alignment is BackgroundAlignment.TopLeading or BackgroundAlignment.Top or BackgroundAlignment.TopTrailing;

    public static bool SticksToBottom(this BackgroundAlignment alignment) =>
        alignment is BackgroundAlignment.BottomLeading or BackgroundAlignment.Bottom or BackgroundAlignment.BottomTrailing;

    public static bool SticksToLeading(this BackgroundAlignment alignment) =>
        alignment is BackgroundAlignment.TopLeading or BackgroundAlignment.Leading or BackgroundAlignment.BottomLeading;

    public static bool SticksToTrailing(this BackgroundAlignment alignment) =>
        alignment is BackgroundAlignment.TopTrailing or BackgroundAlignment.Trailing or BackgroundAlignment.BottomTrailing;

    /// Per-corner radius multipliers. A corner that touches a stuck edge gets 0.
    public static (double TopLeft, double TopRight, double BottomLeft, double BottomRight) CornerRadiusMultipliers(
        this BackgroundAlignment alignment) => alignment switch
    {
        BackgroundAlignment.Center => (1, 1, 1, 1),
        BackgroundAlignment.Top => (0, 0, 1, 1),
        BackgroundAlignment.Bottom => (1, 1, 0, 0),
        BackgroundAlignment.Leading => (0, 1, 0, 1),
        BackgroundAlignment.Trailing => (1, 0, 1, 0),
        BackgroundAlignment.TopLeading => (0, 0, 0, 1),
        BackgroundAlignment.TopTrailing => (0, 0, 1, 0),
        BackgroundAlignment.BottomLeading => (0, 1, 0, 0),
        BackgroundAlignment.BottomTrailing => (1, 0, 0, 0),
        _ => (1, 1, 1, 1),
    };
}
