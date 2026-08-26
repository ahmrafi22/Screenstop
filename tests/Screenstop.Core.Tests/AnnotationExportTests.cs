using Screenstop.Core.Annotations;
using Screenstop.Rendering;
using SkiaSharp;
using Xunit;

namespace Screenstop.Core.Tests;

/// Non-destructive editing: edits persist as a `.screenstop` sidecar next to
/// the image; the image file itself is never overwritten. These tests cover
/// the sidecar round-trip and the composited-load helper.
public class AnnotationExportTests : IDisposable
{
    private readonly string _dir;

    public AnnotationExportTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ScreenstopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
    }

    private string WriteTestImage(string name = "shot.png")
    {
        using var bitmap = new SKBitmap(new SKImageInfo(120, 90, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        string path = Path.Combine(_dir, name);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    private static AnnotationDocument SampleDocument()
    {
        var document = new AnnotationDocument();
        document.Annotations.Add(new Annotation
        {
            Tool = AnnotationTool.Rectangle,
            Rect = new NormalizedRect(0.2, 0.2, 0.5, 0.5),
            Color = AnnotationColor.Red,
            StrokeWidth = 0.02,
        });
        return document;
    }

    [Fact]
    public void Sidecar_round_trip_preserves_annotations()
    {
        string imagePath = WriteTestImage();
        SampleDocument().Save(imagePath);

        Assert.True(File.Exists(AnnotationDocument.SidecarPathFor(imagePath)));

        var loaded = AnnotationDocument.Load(imagePath);
        Assert.NotNull(loaded);
        Assert.Single(loaded!.Annotations);
        Assert.Equal(AnnotationTool.Rectangle, loaded.Annotations[0].Tool);
        Assert.Equal(0.2, loaded.Annotations[0].Rect.X, 5);
        Assert.Equal(AnnotationColor.Red, loaded.Annotations[0].Color);
    }

    [Fact]
    public void Load_returns_null_when_no_sidecar()
    {
        string imagePath = WriteTestImage();
        Assert.Null(AnnotationDocument.Load(imagePath));
        Assert.False(AnnotationExport.HasAnnotations(imagePath));
    }

    [Fact]
    public void Corrupt_sidecar_returns_null_not_throw()
    {
        string imagePath = WriteTestImage();
        File.WriteAllText(AnnotationDocument.SidecarPathFor(imagePath), "{ not valid json");

        Assert.Null(AnnotationDocument.Load(imagePath));
        Assert.False(AnnotationExport.HasAnnotations(imagePath));
    }

    [Fact]
    public void HasAnnotations_true_only_with_content()
    {
        string imagePath = WriteTestImage();

        // Empty document: sidecar exists but has no annotations.
        new AnnotationDocument().Save(imagePath);
        Assert.False(AnnotationExport.HasAnnotations(imagePath));

        SampleDocument().Save(imagePath);
        Assert.True(AnnotationExport.HasAnnotations(imagePath));
    }

    [Fact]
    public void LoadComposited_applies_sidecar_edits()
    {
        string imagePath = WriteTestImage();
        SampleDocument().Save(imagePath);

        using var composited = AnnotationExport.LoadComposited(imagePath);
        Assert.NotNull(composited);

        // The red rect stroke crosses the left edge at x=24 (0.2*120), y=45.
        var stroke = composited!.GetPixel(24, 45);
        Assert.True(stroke.Red > 150 && stroke.Green < 120, $"expected red stroke, got {stroke}");

        // Interior stays white.
        var interior = composited.GetPixel(60, 45);
        Assert.True(interior.Red > 240 && interior.Green > 240 && interior.Blue > 240, $"expected white interior, got {interior}");
    }

    [Fact]
    public void LoadComposited_without_sidecar_returns_plain_decode()
    {
        string imagePath = WriteTestImage();

        using var composited = AnnotationExport.LoadComposited(imagePath);
        Assert.NotNull(composited);
        Assert.Equal(120, composited!.Width);
        Assert.Equal(90, composited.Height);

        var pixel = composited.GetPixel(60, 45);
        Assert.True(pixel.Red > 240 && pixel.Green > 240 && pixel.Blue > 240);
    }

    [Fact]
    public void LoadComposited_missing_image_returns_null()
    {
        Assert.Null(AnnotationExport.LoadComposited(Path.Combine(_dir, "missing.png")));
    }

    [Fact]
    public void Composite_applies_sidecar_to_provided_bitmap()
    {
        string imagePath = WriteTestImage();
        SampleDocument().Save(imagePath);

        using var bitmap = SKBitmap.Decode(imagePath);
        using var composited = AnnotationExport.Composite(bitmap, imagePath);

        var stroke = composited.GetPixel(24, 45);
        Assert.True(stroke.Red > 150 && stroke.Green < 120, $"expected red stroke, got {stroke}");
    }

    [Fact]
    public void Saving_edits_does_not_modify_source_image()
    {
        string imagePath = WriteTestImage();
        byte[] before = File.ReadAllBytes(imagePath);

        SampleDocument().Save(imagePath);

        byte[] after = File.ReadAllBytes(imagePath);
        Assert.Equal(before, after); // non-destructive: image untouched
        Assert.True(File.Exists(AnnotationDocument.SidecarPathFor(imagePath)));
    }
}
