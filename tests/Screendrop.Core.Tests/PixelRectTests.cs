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
}
