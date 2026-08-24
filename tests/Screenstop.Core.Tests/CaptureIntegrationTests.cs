using System.Runtime.InteropServices;
using Screenstop.Capture;
using SkiaSharp;
using Xunit;

namespace Screenstop.Core.Tests;

public class CaptureIntegrationTests
{
    private static MonitorInfo PrimaryMonitor()
    {
        var monitors = MonitorEnumerator.Enumerate();
        Assert.NotEmpty(monitors);
        return monitors.First(m => m.IsPrimary);
    }

    [Fact]
    public void Monitor_enumeration_finds_at_least_one_primary_display()
    {
        var monitors = MonitorEnumerator.Enumerate();

        Assert.NotEmpty(monitors);
        Assert.Contains(monitors, m => m.IsPrimary);
        Assert.All(monitors, m => Assert.False(m.PhysicalBounds.IsEmpty));
    }

    [Fact]
    public void Primary_monitor_is_rooted_at_virtual_screen_origin()
    {
        var primary = PrimaryMonitor();

        Assert.Equal(0, primary.PhysicalBounds.X);
        Assert.Equal(0, primary.PhysicalBounds.Y);
    }

    [Fact]
    public void GDI_capture_matches_monitor_pixel_size_with_content()
    {
        var primary = PrimaryMonitor();

        using var capture = GDICapturer.CaptureMonitor(primary);

        Assert.Equal(primary.PhysicalBounds.Width, capture.Bitmap.Width);
        Assert.Equal(primary.PhysicalBounds.Height, capture.Bitmap.Height);

        var seen = new HashSet<uint>();
        int width = capture.Bitmap.Width;
        int height = capture.Bitmap.Height;
        int stride = capture.Bitmap.RowBytes;
        var pixels = new byte[capture.Bitmap.ByteCount];
        Marshal.Copy(capture.Bitmap.GetPixels(), pixels, 0, pixels.Length);

        for (int y = 0; y < height; y += 8)
        {
            for (int x = 0; x < width; x += 8)
            {
                int offset = (y * stride) + (x * 4);
                uint key = (uint)(pixels[offset]
                    | (pixels[offset + 1] << 8)
                    | (pixels[offset + 2] << 16)
                    | (pixels[offset + 3] << 24));
                seen.Add(key);
            }
        }

        Assert.True(seen.Count > 1, "Captured frame appears to be a single flat color.");
    }

    [Fact]
    public void Captured_frame_encodes_to_png_and_roundtrips()
    {
        var primary = PrimaryMonitor();

        using var capture = GDICapturer.CaptureMonitor(primary);
        string path = Path.Combine(Path.GetTempPath(), $"screenstop-test-{Guid.NewGuid():N}.png");

        try
        {
            using (var image = SKImage.FromBitmap(capture.Bitmap))
            using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var stream = File.Create(path))
            {
                encoded.SaveTo(stream);
            }

            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 0);

            using var decoded = SKBitmap.Decode(path);
            Assert.NotNull(decoded);
            Assert.Equal(capture.Bitmap.Width, decoded!.Width);
            Assert.Equal(capture.Bitmap.Height, decoded.Height);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
