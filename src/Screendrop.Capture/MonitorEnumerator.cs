using System.Runtime.InteropServices;
using Screendrop.Core.Geometry;

namespace Screendrop.Capture;

public static class MonitorEnumerator
{
    public static IReadOnlyList<MonitorInfo> Enumerate()
    {
        var monitors = new List<MonitorInfo>();

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref RECT clipBounds, IntPtr data) =>
        {
            var info = new MONITORINFOEXW();
            info.cbSize = Marshal.SizeOf<MONITORINFOEXW>();
            if (!NativeMethods.GetMonitorInfoW(hMonitor, ref info))
            {
                return true;
            }

            monitors.Add(new MonitorInfo(
                hMonitor,
                info.szDevice,
                new PixelRect(
                    info.rcMonitor.Left,
                    info.rcMonitor.Top,
                    info.rcMonitor.Right - info.rcMonitor.Left,
                    info.rcMonitor.Bottom - info.rcMonitor.Top),
                (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0));

            return true;
        }, IntPtr.Zero);

        return monitors;
    }

    public static MonitorInfo? GetFocusedMonitor()
    {
        var monitors = Enumerate();
        if (monitors.Count == 0)
        {
            return null;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground != IntPtr.Zero)
        {
            var hMonitor = NativeMethods.MonitorFromWindow(foreground, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var match = monitors.FirstOrDefault(m => m.Handle == hMonitor);
            if (match is not null)
            {
                return match;
            }
        }

        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
    }
}
