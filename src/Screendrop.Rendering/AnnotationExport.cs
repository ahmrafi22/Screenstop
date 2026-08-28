using Screendrop.Core.Annotations;
using SkiaSharp;

namespace Screendrop.Rendering;

/// Loads a captured image together with its annotation sidecar (if any) and
/// produces the composited result. This is the single place the rest of the
/// app asks "what does this screenshot look like with its edits applied?",
/// keeping the staged file on disk non-destructive (mac parity: the original
/// capture is never overwritten; edits live in `<image>.screendrop`).
public static class AnnotationExport
{
    /// True when a sidecar with at least one annotation exists for the image.
    public static bool HasAnnotations(string imagePath)
    {
        var document = AnnotationDocument.Load(imagePath);
        return document is not null && document.Annotations.Count > 0;
    }

    /// True when a sidecar has any renderable edit (annotations or background).
    public static bool HasContent(string imagePath) =>
        HasContent(AnnotationDocument.Load(imagePath));

    private static bool HasContent(AnnotationDocument? document) =>
        document is not null
        && (document.Annotations.Count > 0
            || (document.Background?.HasRenderableContent ?? false));

    /// Decodes the image and composites its sidecar edits (annotations and/or
    /// background). Returns the plain decode when there is nothing to apply.
    /// Returns null only if the image itself cannot be decoded.
    public static SKBitmap? LoadComposited(string imagePath)
    {
        var bitmap = SKBitmap.Decode(imagePath);
        if (bitmap is null)
        {
            return null;
        }

        var document = AnnotationDocument.Load(imagePath);
        if (!HasContent(document))
        {
            return bitmap;
        }

        using (bitmap)
        {
            return AnnotationRenderer.Render(bitmap, document!);
        }
    }

    /// Composites an already-decoded bitmap with a sidecar loaded from
    /// <paramref name="imagePath"/>. Used when the caller already holds the
    /// pixels (e.g. the after-capture pipeline) and only needs edits applied.
    public static SKBitmap Composite(SKBitmap bitmap, string imagePath)
    {
        var document = AnnotationDocument.Load(imagePath);
        if (!HasContent(document))
        {
            return bitmap.Copy();
        }

        return AnnotationRenderer.Render(bitmap, document!);
    }
}
