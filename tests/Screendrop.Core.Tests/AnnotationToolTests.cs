using Screendrop.Core.Annotations;
using Xunit;

namespace Screendrop.Core.Tests;

/// <summary>
/// The three tools added after v1 (line / highlight / solid rectangle) must
/// keep their numeric enum values stable: saved sidecar documents store the
/// raw integer, so reordering would silently corrupt existing annotations.
/// </summary>
public class AnnotationToolTests
{
    [Fact]
    public void New_tools_have_stable_numeric_values()
    {
        Assert.Equal(9, (int)AnnotationTool.Line);
        Assert.Equal(10, (int)AnnotationTool.Highlight);
        Assert.Equal(11, (int)AnnotationTool.FilledRectangle);
    }

    [Fact]
    public void Line_uses_end_points_for_geometry_and_hit_testing()
    {
        var line = new Annotation
        {
            Tool = AnnotationTool.Line,
            Start = new NormalizedPoint(0.1, 0.1),
            End = new NormalizedPoint(0.9, 0.9),
        };

        // Geometry lives in Start/End: the bounds span both points.
        var bounds = line.Bounds();
        Assert.Equal(0.1, bounds.X, 6);
        Assert.Equal(0.1, bounds.Y, 6);
        Assert.Equal(0.9, bounds.Right, 6);
        Assert.Equal(0.9, bounds.Bottom, 6);

        // Hit-testing follows the segment, not a bounding box.
        Assert.True(line.HitTest(0.5, 0.5, 0.01));
        Assert.False(line.HitTest(0.5, 0.2, 0.01));
    }

    [Fact]
    public void Line_and_arrow_share_endpoint_drag_behavior()
    {
        // Both endpoint-driven tools report UsesEndPoints so the canvas gives
        // them the same drag-to-draw and per-endpoint selection handles.
        Assert.True(AnnotationTool.Line.UsesEndPoints());
        Assert.True(AnnotationTool.Arrow.UsesEndPoints());
        Assert.False(AnnotationTool.Rectangle.UsesEndPoints());
        Assert.False(AnnotationTool.Highlight.UsesEndPoints());
    }
}
