using System.Runtime.InteropServices;

namespace Screenstop.Capture;

public static class DisplayAffinity
{
    public const uint WdaNone = 0x0;
    public const uint WdaMonitor = 0x1;
    public const uint WdaExcludedFromCapture = 0x11;

    public static bool ExcludeFromCapture(IntPtr hwnd)
    {
        return SetWindowDisplayAffinity(hwnd, WdaExcludedFromCapture);
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
