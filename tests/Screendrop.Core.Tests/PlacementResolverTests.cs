using Screendrop.Core.Geometry;
using Xunit;

namespace Screendrop.Core.Tests;

public class PlacementResolverTests
{
    private static readonly PixelRect Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void Panel_is_centered_horizontally()
    {
        var result = PlacementResolver.ResolveBottomCenter(Screen, 320, 120);

        Assert.Equal(800, result.X);
    }

    [Fact]
    public void Panel_sits_above_bottom_edge_with_margin()
    {
        var result = PlacementResolver.ResolveBottomCenter(Screen, 320, 120);

        Assert.Equal(1080 - 120 - PlacementResolver.BottomMargin, result.Y);
        Assert.Equal(320, result.Width);
        Assert.Equal(120, result.Height);
    }

    [Fact]
    public void Oversized_panel_clamps_to_screen_edges()
    {
        var result = PlacementResolver.ResolveBottomCenter(Screen, 2500, 2000);

        Assert.Equal(0, result.X);
        Assert.Equal(0, result.Y);
        Assert.Equal(2500, result.Width);
        Assert.Equal(2000, result.Height);
    }

    [Fact]
    public void Non_zero_screen_origin_is_preserved()
    {
        var secondary = new PixelRect(1920, 0, 1920, 1080);

        var result = PlacementResolver.ResolveBottomCenter(secondary, 320, 120);

        Assert.Equal(1920 + 800, result.X);
        Assert.Equal(1080 - 120 - PlacementResolver.BottomMargin, result.Y);
    }

    [Fact]
    public void Zero_sized_panel_returns_empty_rect()
    {
        var result = PlacementResolver.ResolveBottomCenter(Screen, 0, 0);

        Assert.True(result.IsEmpty);
    }
}
