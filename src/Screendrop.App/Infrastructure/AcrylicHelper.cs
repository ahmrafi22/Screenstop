using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Screendrop.App.Infrastructure;

/// Native frosted-glass backdrop for borderless WPF windows (the Windows
/// answer to SwiftUI's `.ultraThinMaterial`). Uses DWM acrylic where the OS
/// supports it; callers paint a translucent background so the blur shows
/// through. Falls back silently to an opaque look on unsupported builds.
internal static class AcrylicHelper
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// Enables acrylic blur behind the window. <paramref name="tintArgb"/> is
    /// the scrim color blended over the blur (AARRGGBB); its alpha controls
    /// how much of the blurred desktop shows through.
    public static bool TryEnableAcrylic(Window window, uint tintArgb)
    {
        try
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            TryEnableRoundedCorners(handle);

            var accent = new AccentPolicy
            {
                AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 2,
                GradientColor = unchecked((int)tintArgb),
            };

            int size = Marshal.SizeOf<AccentPolicy>();
            IntPtr accentPtr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = accentPtr,
                    SizeOfData = size,
                };
                int result = SetWindowCompositionAttribute(handle, ref data);
                return result != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }
        catch (Exception ex)
        {
            TraceLog.Write($"acrylic enable failed: {ex.Message}");
            return false;
        }
    }

    /// Win11 DWM rounded corners; a no-op on Win10 (callers keep their own
    /// corner treatment there).
    public static void TryEnableRoundedCorners(IntPtr handle)
    {
        try
        {
            int preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }
        catch (Exception)
        {
        }
    }

    /// True on Windows 11 build 22000+ where DWM rounds corners natively.
    public static bool IsWindows11OrLater => Environment.OSVersion.Version.Build >= 22000;

    public static uint Argb(Color color) =>
        ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
}
