using System.Runtime.InteropServices;
using SkiaSharp;

namespace Screenstop.Capture;

public static class GDICapturer
{
    public static DisplayCapture CaptureMonitor(MonitorInfo monitor)
    {
        if (monitor.PhysicalBounds.IsEmpty)
        {
            throw new ArgumentException("Monitor bounds are empty.", nameof(monitor));
        }

        var bounds = monitor.PhysicalBounds;
        int width = bounds.Width;
        int height = bounds.Height;

        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            throw new InvalidOperationException("GetDC for the screen failed.");
        }

        try
        {
            IntPtr memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
            {
                throw new InvalidOperationException("CreateCompatibleDC failed.");
            }

            try
            {
                IntPtr bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, width, height);
                if (bitmap == IntPtr.Zero)
                {
                    throw new InvalidOperationException("CreateCompatibleBitmap failed.");
                }

                try
                {
                    IntPtr previous = NativeMethods.SelectObject(memoryDc, bitmap);
                    NativeMethods.BitBlt(memoryDc, 0, 0, width, height, screenDc, bounds.X, bounds.Y, NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);
                    NativeMethods.SelectObject(memoryDc, previous);

                    var pixels = new byte[checked(width * height * 4)];
                    var source = new BITMAPINFO();
                    source.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
                    source.bmiHeader.biWidth = width;
                    source.bmiHeader.biHeight = -height;
                    source.bmiHeader.biPlanes = 1;
                    source.bmiHeader.biBitCount = 32;
                    source.bmiHeader.biCompression = 0;

                    int copiedLines = NativeMethods.GetDIBits(memoryDc, bitmap, 0, (uint)height, pixels, ref source, NativeMethods.DIB_RGB_COLORS);
                    if (copiedLines == 0)
                    {
                        throw new InvalidOperationException("GetDIBits failed to read captured frame.");
                    }

                    var imageInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
                    var skBitmap = new SKBitmap(imageInfo);
                    Marshal.Copy(pixels, 0, skBitmap.GetPixels(), pixels.Length);

                    return new DisplayCapture(monitor, skBitmap, DateTimeOffset.Now);
                }
                finally
                {
                    NativeMethods.DeleteObject(bitmap);
                }
            }
            finally
            {
                NativeMethods.DeleteDC(memoryDc);
            }
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
