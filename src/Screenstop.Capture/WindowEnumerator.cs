using System.Runtime.InteropServices;
using Screenstop.Core.Geometry;

namespace Screenstop.Capture;

public static class WindowEnumerator
{
    private static readonly HashSet<string> ExcludedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Windows.UI.Core.CoreWindow",
        "LockApp",
    };

    public static IReadOnlyList<WindowInfo> Enumerate(int excludeProcessId)
    {
        var windows = new List<WindowInfo>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
            {
                return true;
            }

            uint ownerPid;
            NativeMethods.GetWindowThreadProcessId(hwnd, out ownerPid);
            if (ownerPid == (uint)excludeProcessId)
            {
                return true;
            }

            if ((NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOOLWINDOW) != 0)
            {
                return true;
            }

            string className = NativeMethods.GetWindowClassName(hwnd);
            if (ExcludedClasses.Contains(className))
            {
                return true;
            }

            string title = NativeMethods.GetWindowTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            if (!NativeMethods.GetWindowRect(hwnd, out RECT rect))
            {
                return true;
            }

            var bounds = new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            if (bounds.IsEmpty)
            {
                return true;
            }

            windows.Add(new WindowInfo(hwnd, title, bounds));
            return true;
        }, IntPtr.Zero);

        return windows;
    }
}
