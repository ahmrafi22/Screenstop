using System.Runtime.InteropServices;
using System.Windows.Interop;
using Screenstop.App.Infrastructure;
using Screenstop.Core.Settings;

namespace Screenstop.App.Hotkeys;

public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

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
        var settings = SettingsStore.Load();
        var text = mode switch
        {
            CaptureMode.Fullscreen => settings.FullscreenHotkey,
            CaptureMode.Window => settings.WindowHotkey,
            CaptureMode.Area => settings.AreaHotkey,
            _ => null,
        };

        return text ?? $"Alt+Shift+{(int)mode}";
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
        TraceLog.Write($"hotkey window handle={service._source.Handle}");
        service.Reload(out conflicts);
        return service;
    }

    /// <summary>
    /// Re-reads hotkey bindings from settings and re-registers them in place
    /// (same message window). Used after the settings window saves changes,
    /// so new combos take effect without restarting the app.
    /// </summary>
    public void Reload(out IReadOnlyList<CaptureMode> conflicts)
    {
        foreach (int id in _registered.Values)
        {
            UnregisterHotKey(_source.Handle, id);
        }

        _registered.Clear();

        var failed = new List<CaptureMode>();

        var settings = SettingsStore.Load();
        var bindings = new (CaptureMode Mode, string Combo)[]
        {
            (CaptureMode.Fullscreen, settings.FullscreenHotkey),
            (CaptureMode.Window, settings.WindowHotkey),
            (CaptureMode.Area, settings.AreaHotkey),
        };

        foreach (var (mode, comboText) in bindings)
        {
            if (!HotkeyCombo.TryParse(comboText, out var combo))
            {
                // Normalize() already guarantees parseable combos; this is a
                // belt-and-braces guard against a hand-edited settings file.
                failed.Add(mode);
                TraceLog.Write($"unparseable hotkey {mode} '{comboText}'");
                continue;
            }

            uint modifiers = 0;
            if (combo.Alt) modifiers |= ModAlt;
            if (combo.Control) modifiers |= ModControl;
            if (combo.Shift) modifiers |= ModShift;
            if (combo.Windows) modifiers |= ModWin;

            int id = (int)mode;
            if (RegisterHotKey(_source.Handle, id, modifiers, (uint)combo.VirtualKey))
            {
                _registered.Add(mode, id);
                TraceLog.Write($"registered {mode} {combo.Format()}");
            }
            else
            {
                failed.Add(mode);
                TraceLog.Write($"conflict {mode} {combo.Format()} error={Marshal.GetLastWin32Error()}");
            }
        }

        conflicts = failed;
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
