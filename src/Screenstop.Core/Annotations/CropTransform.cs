namespace Screenstop.Core.Annotations;

/// <summary>
/// Pure geometry for cropping: remaps points and annotations from the
/// original image space into a smaller crop rectangle's unit space so a
/// crop never invalidates existing annotations (mac parity). No pixel or
/// UI dependencies — pixel extraction lives in Screenstop.Rendering.
/// </summary>
public static class CropTransform
{
    /// Smallest crop allowed on either axis, as a fraction of the image.
    public const double MinFraction = 0.02;

    /// Returns a usable version of the requested crop (clamped, non-degenerate),
    /// or null when the rect is too small to crop with.
    public static NormalizedRect? Validate(NormalizedRect request)
    {
        var clamped = request.ClampToUnit();
        if (clamped.IsEmpty || clamped.Width < MinFraction || clamped.Height < MinFraction)
        {
            return null;
        }

        return clamped;
    }

    /// Maps a point from the original image space into the crop's space.
    public static NormalizedPoint Remap(NormalizedPoint point, NormalizedRect crop) => new(
        (point.X - crop.X) / crop.Width,
        (point.Y - crop.Y) / crop.Height);

    public static AnnotationDocument TransformDocument(AnnotationDocument document, NormalizedRect crop)
    {
        var transformed = document.Clone();
        foreach (var annotation in transformed.Annotations)
        {
            TransformAnnotation(annotation, crop);
        }

        return transformed;
    }

    private static void TransformAnnotation(Annotation annotation, NormalizedRect crop)
    {
        switch (annotation.Tool)
        {
            case AnnotationTool.Arrow:
            case AnnotationTool.Line:
                annotation.Start = Remap(annotation.Start, crop);
                annotation.End = Remap(annotation.End, crop);
                break;

            case AnnotationTool.Freehand:
                for (int i = 0; i < annotation.Points.Count; i++)
                {
                    annotation.Points[i] = Remap(annotation.Points[i], crop);
                }

                break;

            default:
                // Rectangle-family tools (incl. highlight/pixelate/blur/text/
                // markers) all carry their geometry in Rect.
                var topLeft = Remap(new NormalizedPoint(annotation.Rect.X, annotation.Rect.Y), crop);
                var bottomRight = Remap(new NormalizedPoint(annotation.Rect.Right, annotation.Rect.Bottom), crop);
                annotation.Rect = NormalizedRect
                    .FromPoints(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y)
                    .ClampToUnit();
                break;
        }
    }
}
