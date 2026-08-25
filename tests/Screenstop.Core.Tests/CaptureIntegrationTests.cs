using System.Runtime.InteropServices;
using Screenstop.Capture;
using Screenstop.Core.Geometry;
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
    public void Region_capture_matches_full_capture_crop()
    {
        var primary = PrimaryMonitor();
        var bounds = primary.PhysicalBounds;

        using var full = GDICapturer.CaptureMonitor(primary);

        var region = new PixelRect(
            bounds.X + bounds.Width / 4,
            bounds.Y + bounds.Height / 4,
            bounds.Width / 2,
            bounds.Height / 2);

        using var regionCapture = GDICapturer.CaptureRegion(region);
        Assert.Equal(region.Width, regionCapture.Width);
        Assert.Equal(region.Height, regionCapture.Height);

        using var cropped = new SKBitmap(new SKImageInfo(region.Width, region.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        Assert.True(full.Bitmap.ExtractSubset(cropped, new SKRectI(region.X - bounds.X, region.Y - bounds.Y, region.X - bounds.X + region.Width, region.Y - bounds.Y + region.Height)));

        var a = new byte[regionCapture.ByteCount];
        var b = new byte[cropped.ByteCount];
        Marshal.Copy(regionCapture.GetPixels(), a, 0, a.Length);
        Marshal.Copy(cropped.GetPixels(), b, 0, b.Length);

        int strideA = regionCapture.RowBytes;
        int strideB = cropped.RowBytes;
        int mismatches = 0;
        int total = 0;

        for (int y = 0; y < regionCapture.Height; y += 4)
        {
            for (int x = 0; x < regionCapture.Width; x += 4)
            {
                total++;
                int oa = (y * strideA) + (x * 4);
                int ob = (y * strideB) + (x * 4);
                for (int c = 0; c < 4; c++)
                {
                    if (a[oa + c] != b[ob + c])
                    {
                        mismatches++;
                        break;
                    }
                }
            }
        }

        double mismatchRatio = (double)mismatches / total;
        Assert.True(mismatchRatio < 0.05, $"Region capture diverged from full-capture crop ({mismatchRatio:P0} mismatches).");
    }

    [Fact]
    public void Monitor_dpi_scale_is_reported_and_roundtrips()
    {
        var primary = PrimaryMonitor();
        var (scaleX, scaleY) = MonitorGeometry.GetScale(primary);

        Assert.InRange(scaleX, 0.75, 4.0);
        Assert.InRange(scaleY, 0.75, 4.0);

        var dip = new PixelRect(100, 100, 500, 300);
        var physical = MonitorGeometry.DipToPhysical(primary, scaleX, scaleY, dip.X, dip.Y, dip.Width, dip.Height);

        Assert.Equal((int)Math.Round(100 * scaleX) + primary.PhysicalBounds.X, physical.X);
        Assert.Equal((int)Math.Round(100 * scaleY) + primary.PhysicalBounds.Y, physical.Y);
        Assert.Equal((int)Math.Round(500 * scaleX), physical.Width);
        Assert.Equal((int)Math.Round(300 * scaleY), physical.Height);
    }

    [Fact]
    public void Window_capture_via_print_window_matches_dimensions_with_content()
    {
        var hwnd = CreateTestWindow("WindowCapturer Test 123");
        try
        {
            Assert.NotEqual(IntPtr.Zero, hwnd);
            Assert.True(User32.GetWindowRect(hwnd, out var rect));

            var bounds = new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            var window = new WindowInfo(hwnd, "test", bounds);

            using var capture = WindowCapturer.CaptureWindow(window);

            Assert.Equal(bounds.Width, capture.Width);
            Assert.Equal(bounds.Height, capture.Height);

            var seen = new HashSet<uint>();
            int stride = capture.RowBytes;
            var pixels = new byte[capture.ByteCount];
            Marshal.Copy(capture.GetPixels(), pixels, 0, pixels.Length);

            for (int y = 0; y < capture.Height; y += 6)
            {
                for (int x = 0; x < capture.Width; x += 6)
                {
                    int offset = (y * stride) + (x * 4);
                    uint key = (uint)(pixels[offset] | (pixels[offset + 1] << 8) | (pixels[offset + 2] << 16) | (pixels[offset + 3] << 24));
                    seen.Add(key);
                }
            }

            Assert.True(seen.Count > 1, "Captured window frame appears to be a single flat color.");
        }
        finally
        {
            if (hwnd != IntPtr.Zero)
            {
                User32.DestroyWindow(hwnd);
            }
        }
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

    private static IntPtr CreateTestWindow(string title)
    {
        const int WsOverlapped = 0x00000000;
        const int WsVisible = 0x10000000;
        const int SwShow = 5;

        var hwnd = User32.CreateWindowEx(
            0,
            "STATIC",
            title,
            WsOverlapped | WsVisible,
            60,
            60,
            640,
            480,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (hwnd != IntPtr.Zero)
        {
            User32.ShowWindow(hwnd, SwShow);
            User32.UpdateWindow(hwnd);
            Thread.Sleep(200);
        }

        return hwnd;
    }

    private static class User32
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool UpdateWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool DestroyWindow(IntPtr hWnd);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}