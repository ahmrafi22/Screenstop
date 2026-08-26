using Screenstop.Core.Annotations;
using Screenstop.Core.Geometry;
using Xunit;

namespace Screenstop.Core.Tests;

public sealed class ViewportTests
{
    // A 1000x500 canvas showing an image that fits at 1000x500 (fit == canvas).
    private const double CanvasW = 1000;
    private const double CanvasH = 500;
    private const double FitW = 1000;
    private const double FitH = 500;

    [Fact]
    public void Fit_is_zoom_one_centered()
    {
        Assert.Equal(1, Viewport.Fit.Zoom);
        Assert.Equal(0.5, Viewport.Fit.Center.X);
        Assert.Equal(0.5, Viewport.Fit.Center.Y);
    }

    [Fact]
    public void Fit_offset_is_zero_when_image_fills_canvas()
    {
        var (x, y) = Viewport.Fit.Offset(CanvasW, CanvasH, FitW, FitH);

        Assert.Equal(0, x, 6);
        Assert.Equal(0, y, 6);
    }

    [Fact]
    public void Zoom_at_center_doubles_display_size_and_stays_centered()
    {
        var focus = new NormalizedPoint(0.5, 0.5);
        var zoomed = Viewport.Fit.ZoomAt(focus, 2.0, CanvasW, CanvasH, FitW, FitH);

        Assert.Equal(2.0, zoomed.Zoom, 6);

        var (w, h) = zoomed.DisplaySize(FitW, FitH);
        Assert.Equal(2000, w, 6);
        Assert.Equal(1000, h, 6);

        // Center of the image still at the center of the canvas.
        var (offX, offY) = zoomed.Offset(CanvasW, CanvasH, FitW, FitH);
        Assert.Equal(-500, offX, 6);
        Assert.Equal(-250, offY, 6);
    }

    [Fact]
    public void Zoom_keeps_the_focus_point_stable_on_screen()
    {
        var focus = new NormalizedPoint(0.25, 0.75);

        var before = Viewport.Fit;
        var (beforeW, beforeH) = before.DisplaySize(FitW, FitH);
        var (beforeOffX, beforeOffY) = before.Offset(CanvasW, CanvasH, FitW, FitH);
        double focusPxX = beforeOffX + (focus.X * beforeW);
        double focusPxY = beforeOffY + (focus.Y * beforeH);

        var after = before.ZoomAt(focus, 2.5, CanvasW, CanvasH, FitW, FitH);
        var (afterW, afterH) = after.DisplaySize(FitW, FitH);
        var (afterOffX, afterOffY) = after.Offset(CanvasW, CanvasH, FitW, FitH);
        double newFocusPxX = afterOffX + (focus.X * afterW);
        double newFocusPxY = afterOffY + (focus.Y * afterH);

        Assert.Equal(focusPxX, newFocusPxX, 4);
        Assert.Equal(focusPxY, newFocusPxY, 4);
    }

    [Fact]
    public void Zoom_is_clamped_to_max()
    {
        var viewport = Viewport.Fit;
        for (int i = 0; i < 20; i++)
        {
            viewport = viewport.ZoomAt(new NormalizedPoint(0.5, 0.5), 2.0, CanvasW, CanvasH, FitW, FitH);
        }

        Assert.Equal(Viewport.MaxZoom, viewport.Zoom, 6);
    }

    [Fact]
    public void Zoom_out_never_goes_below_fit()
    {
        var zoomed = Viewport.Fit.ZoomAt(new NormalizedPoint(0.5, 0.5), 3.0, CanvasW, CanvasH, FitW, FitH);
        var zoomedOut = zoomed.ZoomAt(new NormalizedPoint(0.5, 0.5), 0.1, CanvasW, CanvasH, FitW, FitH);

        Assert.Equal(Viewport.MinZoom, zoomedOut.Zoom, 6);
    }

    [Fact]
    public void Zoom_with_nonpositive_factor_is_a_no_op()
    {
        var viewport = Viewport.Fit.ZoomAt(new NormalizedPoint(0.5, 0.5), 0, CanvasW, CanvasH, FitW, FitH);

        Assert.Equal(Viewport.Fit, viewport);
    }

    [Fact]
    public void Pan_moves_the_view_against_the_drag_direction()
    {
        var zoomed = Viewport.Fit.ZoomAt(new NormalizedPoint(0.5, 0.5), 2.0, CanvasW, CanvasH, FitW, FitH);

        // Drag right by 100 canvas px → content moves right → center moves left.
        var panned = zoomed.Pan(100, 0, CanvasW, CanvasH, FitW, FitH);

        Assert.True(panned.Center.X < zoomed.Center.X);
        Assert.Equal(zoomed.Center.Y, panned.Center.Y, 6);
    }

    [Fact]
    public void Pan_is_clamped_so_the_image_always_covers_the_canvas()
    {
        var zoomed = Viewport.Fit.ZoomAt(new NormalizedPoint(0.5, 0.5), 2.0, CanvasW, CanvasH, FitW, FitH);

        // Drag absurdly far left: the right edge of the image must never
        // scroll past the right edge of the canvas (no blank strip).
        var panned = zoomed.Pan(-100_000, 0, CanvasW, CanvasH, FitW, FitH);

        var (w, _) = panned.DisplaySize(FitW, FitH);
        var (offX, _) = panned.Offset(CanvasW, CanvasH, FitW, FitH);
        Assert.True(offX + w >= CanvasW - 0.001, $"image right edge {offX + w} < canvas {CanvasW}");
        Assert.True(offX <= 0.001, $"image left edge {offX} > 0");
    }

    [Fact]
    public void Pan_at_fit_zoom_stays_centered()
    {
        // Nothing to pan when the image fits exactly.
        var panned = Viewport.Fit.Pan(50, 50, CanvasW, CanvasH, FitW, FitH);

        Assert.Equal(0.5, panned.Center.X, 6);
        Assert.Equal(0.5, panned.Center.Y, 6);
    }

    [Fact]
    public void Clamped_pins_center_when_image_smaller_than_canvas_on_axis()
    {
        // Portrait image in a landscape canvas: at zoom 1 the width fits
        // exactly but the height has slack → Y stays pinned to center.
        var viewport = new Viewport(1.0, new NormalizedPoint(0.5, 0.9));
        var clamped = viewport.Clamped(CanvasW, CanvasH, FitW, FitH * 0.5);

        Assert.Equal(0.5, clamped.Center.Y, 6);
    }

    [Fact]
    public void Zoom_then_pan_round_trip_keeps_view_valid()
    {
        var viewport = Viewport.Fit;
        var focus = new NormalizedPoint(0.8, 0.2);

        viewport = viewport.ZoomAt(focus, 4.0, CanvasW, CanvasH, FitW, FitH);
        viewport = viewport.Pan(-150, 80, CanvasW, CanvasH, FitW, FitH);
        viewport = viewport.ZoomAt(focus, 0.5, CanvasW, CanvasH, FitW, FitH);

        Assert.InRange(viewport.Zoom, Viewport.MinZoom, Viewport.MaxZoom);
        Assert.InRange(viewport.Center.X, 0, 1);
        Assert.InRange(viewport.Center.Y, 0, 1);
    }
}
