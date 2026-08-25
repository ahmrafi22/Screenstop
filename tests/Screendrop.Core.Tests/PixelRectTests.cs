using Screendrop.Core.Geometry;
using Xunit;

namespace Screendrop.Core.Tests;

public class PixelRectTests
{
    [Fact]
    public void Edges_derive_from_origin_and_size()
    {
        var rect = new PixelRect(-100, -50, 1920, 1080);

        Assert.Equal(1820, rect.Right);
        Assert.Equal(1030, rect.Bottom);
        Assert.False(rect.IsEmpty);
    }

    [Fact]
    public void Non_positive_dimensions_are_empty()
    {
        Assert.True(new PixelRect(0, 0, 0, 100).IsEmpty);
        Assert.True(new PixelRect(0, 0, 100, -1).IsEmpty);
        Assert.False(new PixelRect(5, 5, 1, 1).IsEmpty);
    }

    [Fact]
    public void Contains_point_and_rect()
    {
        var rect = new PixelRect(10, 10, 100, 50);

        Assert.True(rect.Contains(10, 10));
        Assert.True(rect.Contains(109, 59));
        Assert.False(rect.Contains(110, 60));
        Assert.False(rect.Contains(9, 20));
        Assert.False(rect.Contains(20, 9));

        Assert.True(rect.Contains(new PixelRect(20, 20, 5, 5)));
        Assert.True(rect.Contains(new PixelRect(10, 10, 100, 50)));
        Assert.False(rect.Contains(new PixelRect(20, 20, 100, 5)));
    }

    [Fact]
    public void Intersects_and_intersection()
    {
        var a = new PixelRect(0, 0, 100, 100);
        var b = new PixelRect(50, 50, 100, 100);
        var c = new PixelRect(200, 200, 10, 10);

        Assert.True(a.Intersects(b));
        Assert.False(a.Intersects(c));

        var overlap = PixelRect.Intersect(a, b);
        Assert.Equal(new PixelRect(50, 50, 50, 50), overlap);
    }

    [Fact]
    public void FromMinMax_normalizes_drag_coordinates()
    {
        var rect = PixelRect.FromMinMax(maxX: 500, maxY: 400, minX: 200, minY: 100);

        Assert.Equal(new PixelRect(200, 100, 300, 300), rect);
    }
}
