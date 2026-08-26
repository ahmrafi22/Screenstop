using Screendrop.Core.Annotations;
using Xunit;

namespace Screendrop.Core.Tests;

public class CropTransformTests
{
    [Fact]
    public void Validate_rejects_degenerate_and_tiny_rects()
    {
        Assert.Null(CropTransform.Validate(new NormalizedRect(0.5, 0.5, 0, 0)));
        Assert.Null(CropTransform.Validate(new NormalizedRect(0.4, 0.4, 0.005, 0.3)));
        Assert.Null(CropTransform.Validate(new NormalizedRect(-0.2, -0.2, 0.01, 0.01)));
    }

    [Fact]
    public void Validate_clamps_into_the_unit_square()
    {
        var result = CropTransform.Validate(new NormalizedRect(-0.1, 0.2, 0.5, 0.9));
        Assert.NotNull(result);
        var rect = result.Value;
        Assert.Equal(0, rect.X, 6);
        Assert.Equal(0.2, rect.Y, 6);
        Assert.True(rect.Right <= 1);
        Assert.True(rect.Bottom <= 1);
    }

    [Fact]
    public void Remap_shifts_points_into_crop_space()
    {
        var crop = new NormalizedRect(0.25, 0.25, 0.5, 0.5);

        var center = CropTransform.Remap(new NormalizedPoint(0.5, 0.5), crop);
        Assert.Equal(0.5, center.X, 6);
        Assert.Equal(0.5, center.Y, 6);

        var corner = CropTransform.Remap(new NormalizedPoint(0.75, 0.25), crop);
        Assert.Equal(1.0, corner.X, 6);
        Assert.Equal(0.0, corner.Y, 6);
    }

    [Fact]
    public void TransformDocument_remaps_all_geometry_kinds()
    {
        var crop = new NormalizedRect(0.5, 0.5, 0.5, 0.5); // bottom-right quadrant
        var document = new AnnotationDocument
        {
            Annotations =
            {
                new Annotation { Tool = AnnotationTool.Rectangle, Rect = new NormalizedRect(0.6, 0.6, 0.2, 0.2) },
                new Annotation
                {
                    Tool = AnnotationTool.Line,
                    Start = new NormalizedPoint(0.5, 1.0),
                    End = new NormalizedPoint(1.0, 0.5),
                },
                new Annotation
                {
                    Tool = AnnotationTool.Freehand,
                    Points = { new NormalizedPoint(0.55, 0.55), new NormalizedPoint(0.95, 0.95) },
                },
            },
        };

        var transformed = CropTransform.TransformDocument(document, crop);

        var rectangle = transformed.Annotations[0].Rect;
        // (0.6-0.5)/0.5 = 0.2 ; width 0.2/0.5 = 0.4
        Assert.Equal(0.2, rectangle.X, 6);
        Assert.Equal(0.2, rectangle.Y, 6);
        Assert.Equal(0.4, rectangle.Width, 6);
        Assert.Equal(0.4, rectangle.Height, 6);

        var line = transformed.Annotations[1];
        Assert.Equal(0.0, line.Start.X, 6);
        Assert.Equal(1.0, line.Start.Y, 6);
        Assert.Equal(1.0, line.End.X, 6);
        Assert.Equal(0.0, line.End.Y, 6);

        var freehand = transformed.Annotations[2];
        Assert.Equal(0.1, freehand.Points[0].X, 6);
        Assert.Equal(0.9, freehand.Points[1].X, 6);
    }

    [Fact]
    public void TransformDocument_keeps_annotations_outside_untouched_but_offscreen()
    {
        var crop = new NormalizedRect(0, 0, 0.5, 0.5); // top-left quadrant
        var document = new AnnotationDocument
        {
            Annotations = { new Annotation { Tool = AnnotationTool.Rectangle, Rect = new NormalizedRect(0.8, 0.8, 0.1, 0.1) } },
        };

        var transformed = CropTransform.TransformDocument(document, crop);
        var rect = transformed.Annotations[0].Rect;

        // (0.8-0)/0.5 = 1.6 — outside the cropped image, clamped to the edge.
        Assert.True(rect.X >= 1 || rect.Right >= 1 || rect.X < 0);
    }
}
