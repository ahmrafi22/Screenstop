using System.Runtime.InteropServices;
using System.Windows.Interop;
using Screenstop.App.Infrastructure;

namespace Screenstop.App.Hotkeys;

public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModShift = 0x0004;

    private static readonly (CaptureMode Mode, uint VirtualKey)[] Defaults =
    {
        (CaptureMode.Fullscreen, 0x31),
        (CaptureMode.Window, 0x32),
        (CaptureMode.Area, 0x33),
    };

    private readonly HwndSource _source;
    private readonly Dictionary<CaptureMode, int> _registered = new();

    public event EventHandler<CaptureMode>? HotkeyPressed;

    private HotkeyService(HwndSource source)
    {
        _source = source;
        source.AddHook(WndProc);
    }

    public static string Describe(CaptureMode mode)
    {
        return mode switch
        {
            CaptureMode.Fullscreen => "Alt+Shift+1",
            CaptureMode.Window => "Alt+Shift+2",
            CaptureMode.Area => "Alt+Shift+3",
            _ => $"Alt+Shift+{(int)mode}",
        };
    }

    public static HotkeyService Start(out IReadOnlyList<CaptureMode> conflicts)
    {
        var parameters = new HwndSourceParameters("ScreenstopHotkeyWindow")
        {
            ParentWindow = new IntPtr(-3),
            PositionX = 0,
            PositionY = 0,
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        };

        var service = new HotkeyService(new HwndSource(parameters));
        var failed = new List<CaptureMode>();

        TraceLog.Write($"hotkey window handle={service._source.Handle}");

        foreach (var (mode, virtualKey) in Defaults)
        {
            int id = (int)mode;
            if (RegisterHotKey(service._source.Handle, id, ModAlt | ModShift, virtualKey))
            {
                service._registered.Add(mode, id);
                TraceLog.Write($"registered {mode} vk=0x{virtualKey:X}");
            }
            else
            {
                failed.Add(mode);
                TraceLog.Write($"conflict {mode} vk=0x{virtualKey:X} error={Marshal.GetLastWin32Error()}");
            }
        }

        conflicts = failed;
        return service;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey || !Enum.IsDefined(typeof(CaptureMode), wParam.ToInt32()))
        {
            return IntPtr.Zero;
        }

        handled = true;
        TraceLog.Write($"WM_HOTKEY received mode={wParam.ToInt32()}");
        HotkeyPressed?.Invoke(this, (CaptureMode)wParam.ToInt32());
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (int id in _registered.Values)
        {
            UnregisterHotKey(_source.Handle, id);
        }

        _registered.Clear();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
