using System.Runtime.InteropServices;

namespace Screendrop.Capture;

public static class DisplayAffinity
{
    public const uint WdaNone = 0x0;
    public const uint WdaMonitor = 0x1;
    public const uint WdaExcludedFromCapture = 0x11;

    public static bool ExcludeFromCapture(IntPtr hwnd)
    {
        return SetWindowDisplayAffinity(hwnd, WdaExcludedFromCapture);
    }

    /// Reverses ExcludeFromCapture so the window appears in captures again
    /// (mac includeAppWindowsInCaptures toggle).
    public static bool IncludeInCapture(IntPtr hwnd)
    {
        return SetWindowDisplayAffinity(hwnd, WdaNone);
    }

    public static uint GetAffinity(IntPtr hwnd)
    {
        return GetWindowDisplayAffinity(hwnd, out uint affinity) ? affinity : WdaNone;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll")]
    private static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint dwAffinity);
}
