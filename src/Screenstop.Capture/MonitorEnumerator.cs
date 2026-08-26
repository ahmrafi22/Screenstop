using System.Runtime.InteropServices;
using Screenstop.Core.Geometry;

namespace Screenstop.Capture;

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

    /// Returns the monitor whose bounds contain the point, or the monitor
    /// with the largest overlap with the point's nearest monitor when the
    /// point is off-screen. Null when no displays exist.
    public static MonitorInfo? GetMonitorForPoint(int x, int y)
    {
        var monitors = Enumerate();
        if (monitors.Count == 0)
        {
            return null;
        }

        foreach (var monitor in monitors)
        {
            if (monitor.PhysicalBounds.Contains(x, y))
            {
                return monitor;
            }
        }

        var point = new POINT { X = x, Y = y };
        var hMonitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return monitors.FirstOrDefault(m => m.Handle == hMonitor)
            ?? monitors.FirstOrDefault(m => m.IsPrimary)
            ?? monitors[0];
    }
}
