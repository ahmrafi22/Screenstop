using Screenstop.Core.Background;
using Screenstop.Rendering;
using SkiaSharp;
using Xunit;

namespace Screenstop.Core.Tests;

/// The screenshot's border ring, rounded corners, and drop shadow are what make
/// it read as a placed object. These cover the stage growing to keep that
/// frame inside the export at every camera angle.
public class CameraFrameTests
{
    private static readonly SKColor Signature = new(214, 58, 58);

    private static SKBitmap SignatureContent(int width, int height)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(Signature);
        return bitmap;
    }

    private static BackgroundSettings Stage() => new()
    {
        Style = BackgroundStyle.Solid("#20242b"),
        Padding = 0.14,
        CornerRadius = 0.02,
        Shadow = 0.36,
        Border = new BorderSettings
        {
            IsEnabled = true,
            Thickness = 0.012,
            Color = RgbaColor.White,
            Opacity = 1,
        },
    };

    private static bool IsSignature(SKColor color) =>
        color.Red > 150 && color.Red > color.Green + 80 && color.Red > color.Blue + 80;

    /// Screenshot pixels touching the outermost two-pixel frame of the export.
    /// Anything non-zero means the card ran off the stage and took its border,
    /// corners, and shadow with it.
    private static int EdgeSignaturePixels(SKBitmap bitmap)
    {
        int hits = 0;
        for (int x = 0; x < bitmap.Width; x++)
        {
            for (int edge = 0; edge < 2; edge++)
            {
                if (IsSignature(bitmap.GetPixel(x, edge))) hits++;
                if (IsSignature(bitmap.GetPixel(x, bitmap.Height - 1 - edge))) hits++;
            }
        }

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int edge = 0; edge < 2; edge++)
            {
                if (IsSignature(bitmap.GetPixel(edge, y))) hits++;
                if (IsSignature(bitmap.GetPixel(bitmap.Width - 1 - edge, y))) hits++;
            }
        }

        return hits;
    }

    [Theory]
    [InlineData(0.4)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    [InlineData(2.5)]
    public void Zoom_Keeps_The_Card_Frame_Inside_The_Export(double zoom)
    {
        using var content = SignatureContent(1600, 1000);
        var settings = Stage();
        settings.Camera = new CameraSettings { Zoom = zoom };

        using var shot = BackgroundRenderer.Compose(content, settings);

        Assert.Equal(0, EdgeSignaturePixels(shot));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(2.0)]
    [InlineData(16.0)]
    [InlineData(45.0)]
    public void Tilt_Keeps_The_Card_Frame_Inside_The_Export(double tilt)
    {
        using var content = SignatureContent(1600, 1000);
        var settings = Stage();
        settings.Camera = new CameraSettings
        {
            TiltYDegrees = tilt,
            RotationXDegrees = tilt / 2,
            Zoom = 1,
        };

        using var shot = BackgroundRenderer.Compose(content, settings);

        Assert.Equal(0, EdgeSignaturePixels(shot));
    }

    [Fact]
    public void Zoom_Grows_The_Stage_Rather_Than_Cropping_The_Card()
    {
        using var content = SignatureContent(1600, 1000);

        var flat = Stage();
        using var flatShot = BackgroundRenderer.Compose(content, flat);

        var zoomed = Stage();
        zoomed.Camera = new CameraSettings { Zoom = 1.5 };
        using var zoomedShot = BackgroundRenderer.Compose(content, zoomed);

        Assert.True(
            zoomedShot.Width > flatShot.Width && zoomedShot.Height > flatShot.Height,
            $"zoom 1.5 produced {zoomedShot.Width}x{zoomedShot.Height} from a {flatShot.Width}x{flatShot.Height} stage");
    }

    /// A layout with no camera is byte-for-byte what it was before the camera
    /// fit existed; the flat export must not drift because of it.
    [Fact]
    public void No_Camera_Leaves_The_Flat_Stage_Alone()
    {
        using var content = SignatureContent(1600, 1000);
        var settings = Stage();

        var layout = BackgroundLayout.Make(new SizeD(content.Width, content.Height), settings);

        Assert.Equal(1904, layout.CanvasSize.Width);
        Assert.Equal(1304, layout.CanvasSize.Height);
    }

    /// Pan is an explicit offset measured against the stage, so a card the user
    /// has pushed to one side is meant to run off the edge. The fit must not
    /// chase it - widening the stage moves the card by the same proportion, so
    /// the export would grow without ever catching up.
    [Fact]
    public void Pan_Is_Not_Absorbed_Into_The_Stage_Size()
    {
        using var content = SignatureContent(1600, 1000);

        var centred = Stage();
        centred.Camera = new CameraSettings { Zoom = 1 };
        using var centredShot = BackgroundRenderer.Compose(content, centred);

        var panned = Stage();
        panned.Camera = new CameraSettings { Zoom = 1, PanX = 0.25, PanY = 0.25 };
        using var pannedShot = BackgroundRenderer.Compose(content, panned);

        Assert.Equal(centredShot.Width, pannedShot.Width);
        Assert.Equal(centredShot.Height, pannedShot.Height);
    }
}
