using Screenstop.Core.Annotations;
using SkiaSharp;

namespace Screenstop.Rendering;

/// Loads a captured image together with its annotation sidecar (if any) and
/// produces the composited result. This is the single place the rest of the
/// app asks "what does this screenshot look like with its edits applied?",
/// keeping the staged file on disk non-destructive (mac parity: the original
/// capture is never overwritten; edits live in `<image>.screenstop`).
public static class AnnotationExport
{
    /// True when a sidecar with at least one annotation exists for the image.
    public static bool HasAnnotations(string imagePath)
    {
        var document = AnnotationDocument.Load(imagePath);
        return document is not null && document.Annotations.Count > 0;
    }

    /// Decodes the image and composites its sidecar annotations (if any).
    /// Returns the plain decode when there is nothing to apply. Returns null
    /// only if the image itself cannot be decoded.
    public static SKBitmap? LoadComposited(string imagePath)
    {
        var bitmap = SKBitmap.Decode(imagePath);
        if (bitmap is null)
        {
            return null;
        }

        var document = AnnotationDocument.Load(imagePath);
        if (document is null || document.Annotations.Count == 0)
        {
            return bitmap;
        }

        using (bitmap)
        {
            return AnnotationRenderer.Render(bitmap, document);
        }
    }

    /// Composites an already-decoded bitmap with a sidecar loaded from
    /// <paramref name="imagePath"/>. Used when the caller already holds the
    /// pixels (e.g. the after-capture pipeline) and only needs edits applied.
    public static SKBitmap Composite(SKBitmap bitmap, string imagePath)
    {
        var document = AnnotationDocument.Load(imagePath);
        if (document is null || document.Annotations.Count == 0)
        {
            return bitmap.Copy();
        }

        return AnnotationRenderer.Render(bitmap, document);
    }
}
