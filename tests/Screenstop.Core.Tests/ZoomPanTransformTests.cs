using Screenstop.Core.Geometry;
using Xunit;

namespace Screenstop.Core.Tests;

public sealed class ZoomPanTransformTests
{
    // Canvas 1000x800, image 2000x1000 → fit scale 0.5, display 1000x500,
    // centered vertically with 150px letterbox top and bottom.
    private static ZoomPanTransform MakeTransform()
    {
        var t = new ZoomPanTransform
        {
            CanvasWidth = 1000,
            CanvasHeight = 800,
            ImageWidth = 2000,
            ImageHeight = 1000,
        };
        t.Recompute();
        return t;
    }

    [Fact]
    public void Fit_zoom_centers_the_image()
    {
        var t = MakeTransform();

        Assert.Equal(1.0, t.Zoom);
        Assert.Equal(1000, t.DisplayWidth, 3);
        Assert.Equal(500, t.DisplayHeight, 3);
        Assert.Equal(0, t.OffsetX, 3);
        Assert.Equal(150, t.OffsetY, 3);
    }

    [Fact]
    public void Zoom_is_clamped_to_the_supported_range()
    {
        var t = MakeTransform();

        t.SetZoom(0.1);
        Assert.Equal(ZoomPanTransform.MinZoom, t.Zoom);

        t.SetZoom(100);
        Assert.Equal(ZoomPanTransform.MaxZoom, t.Zoom);
    }

    [Fact]
    public void Zoom_at_center_keeps_the_center_anchored()
    {
        var t = MakeTransform();

        t.ZoomAt(500, 400, 2.0);

        var (x, y) = t.ToCanvas(0.5, 0.5);
        Assert.Equal(500, x, 1);
        Assert.Equal(400, y, 1);
        Assert.Equal(2.0, t.Zoom, 3);
    }

    [Fact]
    public void Zoom_at_cursor_keeps_the_cursor_point_anchored()
    {
        var t = MakeTransform();

        t.ZoomAt(250, 400, 2.0);

        var (x, y) = t.ToCanvas(0.25, 0.5);
        Assert.Equal(250, x, 1);
        Assert.Equal(400, y, 1);
    }

    [Fact]
    public void Pan_does_nothing_while_the_image_fits()
    {
        var t = MakeTransform();

        t.PanBy(300, 200);

        Assert.Equal(0, t.PanX, 3);
        Assert.Equal(0, t.PanY, 3);
        Assert.Equal(0, t.OffsetX, 3);
        Assert.Equal(150, t.OffsetY, 3);
    }

    [Fact]
    public void Pan_moves_the_image_when_zoomed_in()
    {
        var t = MakeTransform();
        t.SetZoom(2.0);

        t.PanBy(-100, -50);

        Assert.Equal(-100, t.PanX, 3);
        Assert.Equal(-50, t.PanY, 3);
    }

    [Fact]
    public void Pan_is_clamped_so_the_image_stays_partly_visible()
    {
        var t = MakeTransform();
        t.SetZoom(2.0); // display 2000x1000 in a 1000x800 canvas

        t.PanBy(100_000, 0);
        double rightEdgeOffset = t.OffsetX + t.DisplayWidth;
        Assert.True(t.OffsetX <= t.CanvasWidth, "image left edge passed the canvas");
        Assert.True(rightEdgeOffset >= 80, $"only {rightEdgeOffset - t.CanvasWidth}px would remain visible");

        t.PanBy(-200_000, 0);
        Assert.True(t.OffsetX + t.DisplayWidth >= 80, "image would be pushed fully off-screen left");

        t.PanBy(0, 100_000);
        Assert.True(t.OffsetY + t.DisplayHeight >= 80, "image would be pushed fully off-screen bottom");
    }

    [Fact]
    public void Reset_returns_to_fit_and_center()
    {
        var t = MakeTransform();
        t.ZoomAt(250, 300, 4.0);
        t.PanBy(120, 80);

        t.Reset();

        Assert.Equal(1.0, t.Zoom, 3);
        Assert.Equal(0, t.PanX, 3);
        Assert.Equal(0, t.PanY, 3);
        Assert.Equal(0, t.OffsetX, 3);
        Assert.Equal(150, t.OffsetY, 3);
    }

    [Fact]
    public void Canvas_and_normalized_coordinates_round_trip()
    {
        var t = MakeTransform();
        t.ZoomAt(400, 350, 3.0);

        var (cx, cy) = t.ToCanvas(0.3, 0.7);
        var (nx, ny) = t.ToNormalized(cx, cy);

        Assert.Equal(0.3, nx, 3);
        Assert.Equal(0.7, ny, 3);
    }

    [Fact]
    public void To_normalized_clamps_points_outside_the_image()
    {
        var t = MakeTransform();

        var (nx, ny) = t.ToNormalized(-500, -500);
        Assert.Equal(0, nx, 3);
        Assert.Equal(0, ny, 3);

        var (fx, fy) = t.ToNormalized(5000, 5000);
        Assert.Equal(1, fx, 3);
        Assert.Equal(1, fy, 3);
    }

    [Fact]
    public void Repeated_zoom_at_cursor_stays_stable()
    {
        // Stability check for the 4K workflow: many zoom steps at the same
        // cursor position must not drift the anchored point or explode the pan.
        var t = MakeTransform();

        for (int i = 0; i < 20; i++)
        {
            t.ZoomAt(600, 300, t.Zoom * 1.2);
        }

        Assert.True(t.Zoom <= ZoomPanTransform.MaxZoom);
        Assert.False(double.IsNaN(t.OffsetX));
        Assert.False(double.IsNaN(t.OffsetY));

        var (nx, ny) = t.ToNormalized(600, 300);
        Assert.InRange(nx, 0, 1);
        Assert.InRange(ny, 0, 1);
    }
}
