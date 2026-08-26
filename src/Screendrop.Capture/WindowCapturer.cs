using System.Runtime.InteropServices;
using Screendrop.Core.Geometry;
using SkiaSharp;

namespace Screendrop.Capture;

public static class WindowCapturer
{
    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    public static SKBitmap CaptureWindow(WindowInfo window)
    {
        var bounds = window.Bounds;
        int w = bounds.Width;
        int h = bounds.Height;
        if (w <= 0 || h <= 0)
        {
            throw new ArgumentException("Window bounds are empty.", nameof(window));
        }

        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        try
        {
            IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
            try
            {
                IntPtr bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, w, h);
                try
                {
                    NativeMethods.SelectObject(memDc, bitmap);
                    bool printSucceeded = NativeMethods.PrintWindow(window.Handle, memDc, PW_RENDERFULLCONTENT);

                    bool useFallback = !printSucceeded || IsPrintWindowFlat(memDc, bitmap, w, h);

                    if (useFallback)
                    {
                        NativeMethods.SelectObject(memDc, IntPtr.Zero);
                        NativeMethods.DeleteObject(bitmap);
                        NativeMethods.DeleteDC(memDc);
                        return GDICapturer.CaptureRegion(bounds);
                    }

                    var bmi = new BITMAPINFO();
                    bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
                    bmi.bmiHeader.biWidth = w;
                    bmi.bmiHeader.biHeight = -h;
                    bmi.bmiHeader.biPlanes = 1;
                    bmi.bmiHeader.biBitCount = 32;
                    bmi.bmiHeader.biCompression = 0;

                    // Same direct-read as GDICapturer: no intermediate buffer.
                    var imageInfo = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
                    var skBitmap = new SKBitmap(imageInfo, w * 4);

                    int lines = NativeMethods.GetDIBits(memDc, bitmap, 0, (uint)h, skBitmap.GetPixels(), ref bmi, NativeMethods.DIB_RGB_COLORS);
                    if (lines == 0)
                    {
                        skBitmap.Dispose();
                        throw new InvalidOperationException("GetDIBits failed after PrintWindow.");
                    }

                    return skBitmap;
                }
                finally
                {
                    NativeMethods.DeleteObject(bitmap);
                }
            }
            finally
            {
                NativeMethods.DeleteDC(memDc);
            }
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// Detects a blank/flat PrintWindow result by reading a handful of
    /// sample scanlines instead of the whole frame. A window that renders
    /// nothing (or that PrintWindow cannot composite) comes back as a
    /// single solid color and is better served by the BitBlt fallback.
    private static bool IsPrintWindowFlat(IntPtr memDc, IntPtr bitmap, int w, int h)
    {
        const int SampleRows = 8;
        int rowCount = Math.Min(SampleRows, h);

        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
        bmi.bmiHeader.biWidth = w;
        bmi.bmiHeader.biHeight = -h;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0;

        var row = new byte[w * 4];
        var seen = new HashSet<uint>();

        for (int i = 0; i < rowCount; i++)
        {
            uint y = rowCount == 1 ? 0 : (uint)(i * (h - 1) / (rowCount - 1));
            if (NativeMethods.GetDIBits(memDc, bitmap, y, 1, row, ref bmi, NativeMethods.DIB_RGB_COLORS) == 0)
            {
                return true;
            }

            for (int x = 0; x < w; x += 8)
            {
                int o = x * 4;
                uint key = (uint)(row[o] | (row[o + 1] << 8) | (row[o + 2] << 16) | (row[o + 3] << 24));
                seen.Add(key);
                if (seen.Count > 1)
                {
                    return false;
                }
            }
        }

        return seen.Count <= 1;
    }
}