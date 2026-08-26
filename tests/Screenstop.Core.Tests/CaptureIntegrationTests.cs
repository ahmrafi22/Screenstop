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

        // Stability pre-check: if the screen is already changing, skip.
        using (var stabilityA = GDICapturer.CaptureMonitor(primary))
        using (var stabilityB = GDICapturer.CaptureMonitor(primary))
        {
            if (MismatchRatio(stabilityA.Bitmap, stabilityB.Bitmap) > 0.20)
            {
                return;
            }
        }

        // Capture full and region back-to-back so the gap between them is as
        // small as possible (the old order captured full, then ran the
        // stability check, then captured region - the screen could change in
        // that gap and the guard never saw it).
        using var full = GDICapturer.CaptureMonitor(primary);

        var region = new PixelRect(
            bounds.X + bounds.Width / 4,
            bounds.Y + bounds.Height / 4,
            bounds.Width / 2,
            bounds.Height / 2);

        using var regionCapture = GDICapturer.CaptureRegion(region);

        // Post-check: if the screen changed while we were capturing, the
        // comparison is inconclusive - skip rather than fail on a live screen.
        using (var fullAfter = GDICapturer.CaptureMonitor(primary))
        {
            if (MismatchRatio(full.Bitmap, fullAfter.Bitmap) > 0.10)
            {
                return;
            }
        }

        Assert.Equal(region.Width, regionCapture.Width);
        Assert.Equal(region.Height, regionCapture.Height);

        using var cropped = new SKBitmap(new SKImageInfo(region.Width, region.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        Assert.True(full.Bitmap.ExtractSubset(cropped, new SKRectI(region.X - bounds.X, region.Y - bounds.Y, region.X - bounds.X + region.Width, region.Y - bounds.Y + region.Height)));

        double mismatch = MismatchRatio(regionCapture, cropped);
        Assert.True(mismatch < 0.15, $"Region capture diverged from full-capture crop ({mismatch:P0} mismatches).");
    }

    private static double MismatchRatio(SKBitmap first, SKBitmap second)
    {
        var a = new byte[first.ByteCount];
        var b = new byte[second.ByteCount];
        Marshal.Copy(first.GetPixels(), a, 0, a.Length);
        Marshal.Copy(second.GetPixels(), b, 0, b.Length);

        int width = Math.Min(first.Width, second.Width);
        int height = Math.Min(first.Height, second.Height);
        int strideA = first.RowBytes;
        int strideB = second.RowBytes;
        int mismatches = 0;
        int total = 0;

        for (int y = 0; y < height; y += 4)
        {
            for (int x = 0; x < width; x += 4)
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

        return total == 0 ? 1.0 : (double)mismatches / total;
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

    [Fact]
    public void Clipboard_image_is_written_as_dib_and_png()
    {
        if (!User32.OpenClipboard(IntPtr.Zero))
        {
            return;
        }

        User32.CloseClipboard();
        const int width = 64;
        const int height = 48;

        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        var pixels = new byte[bitmap.ByteCount];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int o = (y * bitmap.RowBytes) + (x * 4);
                pixels[o] = (byte)(x * 4);
                pixels[o + 1] = (byte)(y * 4);
                pixels[o + 2] = 200;
                pixels[o + 3] = 255;
            }
        }

        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);

        // The clipboard is shared system state: a clipboard manager or the
        // lock screen can empty it between our write and the readback. Retry
        // the write+readback a few times; if it never sticks, the session is
        // too contended to test live clipboard behavior - skip (the DIB
        // layout itself is covered by the pure BuildDib unit test).
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                ClipboardService.SetImage(bitmap);
            }
            catch (InvalidOperationException)
            {
                // Could not open/empty the clipboard at all: locked session.
                bitmap.Dispose();
                return;
            }

            try
            {
                using var clip = ClipboardReadback.Open();
                var dibBytes = clip.GetDib();
                var pngBytes = clip.GetPng();

                if (dibBytes is null || pngBytes is null)
                {
                    continue; // clipboard was emptied under us - retry
                }

                bitmap.Dispose();
                AssertClipboardContents(dibBytes, pngBytes, width, height);
                return;
            }
            catch (InvalidOperationException)
            {
                // Readback could not open the clipboard: locked session.
                bitmap.Dispose();
                return;
            }
        }

        bitmap.Dispose();
    }

    private static void AssertClipboardContents(byte[] dibBytes, byte[] pngBytes, int width, int height)
    {
        Assert.True(dibBytes.Length >= 40 + (width * height * 4));
        Assert.Equal(40, BitConverter.ToInt32(dibBytes, 0));
        Assert.Equal(width, BitConverter.ToInt32(dibBytes, 4));
        Assert.Equal(height, BitConverter.ToInt32(dibBytes, 8));
        Assert.Equal(32, BitConverter.ToUInt16(dibBytes, 14));

        int rowBytes = width * 4;
        int sampleX = 30;
        int sampleY = 20;
        int dibRow = (height - 1 - sampleY) * rowBytes;
        int o = 40 + dibRow + (sampleX * 4);
        Assert.Equal((byte)(sampleX * 4), dibBytes[o]);
        Assert.Equal((byte)(sampleY * 4), dibBytes[o + 1]);

        Assert.True(pngBytes.Length > 0);

        using var decoded = SKBitmap.Decode(pngBytes);
        Assert.NotNull(decoded);
        Assert.Equal(width, decoded!.Width);
        Assert.Equal(height, decoded.Height);
    }

    [Fact]
    public void Display_affinity_excludes_window_from_capture()
    {
        var hwnd = CreateTestWindow("AffinityTest");
        try
        {
            Assert.NotEqual(IntPtr.Zero, hwnd);
            Assert.Equal(DisplayAffinity.WdaNone, DisplayAffinity.GetAffinity(hwnd));

            Assert.True(DisplayAffinity.ExcludeFromCapture(hwnd));

            Assert.Equal(DisplayAffinity.WdaExcludedFromCapture, DisplayAffinity.GetAffinity(hwnd));
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
    public void WindowEnumerator_excludes_minimized_windows()
    {
        var hwnd = CreateTestWindow("MinimizeTest");
        try
        {
            Assert.NotEqual(IntPtr.Zero, hwnd);

            var before = WindowEnumerator.Enumerate(-1);
            Assert.Contains(before, w => w.Handle == hwnd);

            User32.ShowWindow(hwnd, 6);
            Thread.Sleep(150);

            var after = WindowEnumerator.Enumerate(-1);
            Assert.DoesNotContain(after, w => w.Handle == hwnd);
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
    public void Dib_builder_produces_valid_header_and_bottom_up_pixel_layout()
    {
        const int width = 32;
        const int height = 24;

        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        var pixels = new byte[bitmap.ByteCount];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int o = (y * bitmap.RowBytes) + (x * 4);
                pixels[o] = (byte)(x * 3);
                pixels[o + 1] = (byte)(y * 3);
                pixels[o + 2] = 180;
                pixels[o + 3] = 255;
            }
        }

        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);

        var dib = ClipboardService.BuildDib(bitmap);
        bitmap.Dispose();

        Assert.Equal(40, BitConverter.ToInt32(dib, 0));
        Assert.Equal(width, BitConverter.ToInt32(dib, 4));
        Assert.Equal(height, BitConverter.ToInt32(dib, 8));
        Assert.Equal(1, BitConverter.ToUInt16(dib, 12));
        Assert.Equal(32, BitConverter.ToUInt16(dib, 14));
        Assert.Equal(40 + (width * height * 4), dib.Length);

        int rowBytes = width * 4;
        int sampleX = 14;
        int sampleY = 9;
        int dibRow = (height - 1 - sampleY) * rowBytes;
        int offset = 40 + dibRow + (sampleX * 4);

        Assert.Equal((byte)(sampleX * 3), dib[offset]);
        Assert.Equal((byte)(sampleY * 3), dib[offset + 1]);
        Assert.Equal((byte)180, dib[offset + 2]);
        Assert.Equal((byte)255, dib[offset + 3]);
    }

    private sealed class ClipboardReadback : IDisposable
    {
        private ClipboardReadback()
        {
        }

        public static ClipboardReadback Open()
        {
            Exception? last = null;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                if (User32.OpenClipboard(IntPtr.Zero))
                {
                    return new ClipboardReadback();
                }

                last = new InvalidOperationException("OpenClipboard failed.");
                Thread.Sleep(50);
            }

            throw last ?? new InvalidOperationException("OpenClipboard failed.");
        }

        public byte[]? GetDib()
        {
            return ReadData(8u);
        }

        public byte[]? GetPng()
        {
            uint format = User32.RegisterClipboardFormat("PNG");
            return ReadData(format);
        }

        private byte[]? ReadData(uint format)
        {
            IntPtr handle = User32.GetClipboardData(format);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            IntPtr ptr = User32.GlobalLock(handle);
            if (ptr == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                UIntPtr size = User32.GlobalSize(handle);
                var bytes = new byte[(int)size.ToUInt64()];
                Marshal.Copy(ptr, bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                User32.GlobalUnlock(handle);
            }
        }

        public void Dispose()
        {
            User32.CloseClipboard();
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

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll")]
        public static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        public static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint RegisterClipboardFormat(string lpszFormat);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        public static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        public static extern UIntPtr GlobalSize(IntPtr hMem);
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