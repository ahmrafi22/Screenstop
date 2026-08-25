using Screenstop.Rendering;
using SkiaSharp;
using Xunit;

namespace Screenstop.Core.Tests;

public class JpegCompressorTests
{
    private static SKBitmap CreateGradientBitmap(int width = 256, int height = 256)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, new SKColor((byte)(x * 255 / width), (byte)(y * 255 / height), 128));
            }
        }

        return bitmap;
    }

    [Fact]
    public void Encode_produces_decodeable_jpeg_with_same_dimensions()
    {
        using var bitmap = CreateGradientBitmap();

        var jpeg = JpegCompressor.Encode(bitmap, 0.8);

        Assert.True(jpeg.Length > 0);
        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);
        using var decoded = SKBitmap.Decode(jpeg);
        Assert.NotNull(decoded);
        Assert.Equal(bitmap.Width, decoded!.Width);
        Assert.Equal(bitmap.Height, decoded.Height);
    }

    [Fact]
    public void Higher_quality_produces_larger_file_on_gradient_content()
    {
        using var bitmap = CreateGradientBitmap();

        var lowQuality = JpegCompressor.Encode(bitmap, 0.1);
        var highQuality = JpegCompressor.Encode(bitmap, 1.0);

        Assert.True(highQuality.Length > lowQuality.Length, $"high={highQuality.Length} low={lowQuality.Length}");
    }

    [Fact]
    public void Quality_is_clamped_to_valid_range()
    {
        using var bitmap = CreateGradientBitmap();

        var clamped = JpegCompressor.Encode(bitmap, 5.0);
        var max = JpegCompressor.Encode(bitmap, 1.0);

        Assert.Equal(max.Length, clamped.Length);
    }

    [Fact]
    public void EncodePng_roundtrips_pixel_dimensions()
    {
        using var bitmap = CreateGradientBitmap(64, 48);

        var png = JpegCompressor.EncodePng(bitmap);

        using var decoded = SKBitmap.Decode(png);
        Assert.NotNull(decoded);
        Assert.Equal(64, decoded!.Width);
        Assert.Equal(48, decoded.Height);
    }
}
