using Screenstop.Core.Geometry;

namespace Screenstop.Capture;

public static class MonitorGeometry
{
    public static (double ScaleX, double ScaleY) GetScale(MonitorInfo monitor)
    {
        IntPtr dc = NativeMethods.CreateDC(null, monitor.DeviceName, null, IntPtr.Zero);
        if (dc == IntPtr.Zero)
        {
            return (1.0, 1.0);
        }

        try
        {
            int dpiX = NativeMethods.GetDeviceCaps(dc, NativeMethods.LOGPIXELSX);
            int dpiY = NativeMethods.GetDeviceCaps(dc, NativeMethods.LOGPIXELSY);
            return (dpiX / 96.0, dpiY / 96.0);
        }
        finally
        {
            NativeMethods.DeleteDC(dc);
        }
    }

    public static PixelRect DipToPhysical(MonitorInfo monitor, double scaleX, double scaleY, double dipX, double dipY, double dipW, double dipH)
    {
        int x = (int)Math.Round(dipX * scaleX) + monitor.PhysicalBounds.X;
        int y = (int)Math.Round(dipY * scaleY) + monitor.PhysicalBounds.Y;
        int w = (int)Math.Round(dipW * scaleX);
        int h = (int)Math.Round(dipH * scaleY);
        return new PixelRect(x, y, w, h);
    }
}