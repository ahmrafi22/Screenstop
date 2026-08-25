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

                    var pixels = new byte[checked(w * h * 4)];
                    var bmi = new BITMAPINFO();
                    bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
                    bmi.bmiHeader.biWidth = w;
                    bmi.bmiHeader.biHeight = -h;
                    bmi.bmiHeader.biPlanes = 1;
                    bmi.bmiHeader.biBitCount = 32;
                    bmi.bmiHeader.biCompression = 0;

                    int lines = NativeMethods.GetDIBits(memDc, bitmap, 0, (uint)h, pixels, ref bmi, NativeMethods.DIB_RGB_COLORS);
                    if (lines == 0)
                    {
                        throw new InvalidOperationException("GetDIBits failed after PrintWindow.");
                    }

                    var imageInfo = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
                    var skBitmap = new SKBitmap(imageInfo);
                    Marshal.Copy(pixels, 0, skBitmap.GetPixels(), pixels.Length);

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

    private static bool IsPrintWindowFlat(IntPtr memDc, IntPtr bitmap, int w, int h)
    {
        var pixels = new byte[checked(w * h * 4)];
        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
        bmi.bmiHeader.biWidth = w;
        bmi.bmiHeader.biHeight = -h;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0;

        int lines = NativeMethods.GetDIBits(memDc, bitmap, 0, (uint)h, pixels, ref bmi, NativeMethods.DIB_RGB_COLORS);
        if (lines == 0)
        {
            return true;
        }

        int stride = w * 4;
        var seen = new HashSet<uint>();
        for (int y = 0; y < h; y += 8)
        {
            for (int x = 0; x < w; x += 8)
            {
                int o = (y * stride) + (x * 4);
                uint key = (uint)(pixels[o] | (pixels[o + 1] << 8) | (pixels[o + 2] << 16) | (pixels[o + 3] << 24));
                seen.Add(key);
            }
        }

        return seen.Count <= 1;
    }
}