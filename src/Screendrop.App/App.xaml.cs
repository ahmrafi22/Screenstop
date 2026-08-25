using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Screendrop.App.Capture;
using Screendrop.App.Hotkeys;
using Screendrop.App.Infrastructure;
using Screendrop.App.Preview;
using Screendrop.App.Tray;

namespace Screendrop.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\Screendrop.SingleInstance";

    private Mutex? _mutex;
    private TrayController? _tray;
    private HotkeyService? _hotkeys;
    private CaptureCoordinator? _coordinator;
    private PreviewPanelPresenter? _preview;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            TraceLog.Write($"unhandled appdomain exception: {args.ExceptionObject}");

        _mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

        _tray = new TrayController();
        _tray.Initialize();

        _preview = new PreviewPanelPresenter(_tray.Notify);
        _coordinator = new CaptureCoordinator(_tray.Notify, _preview.OnCapture);
        _hotkeys = HotkeyService.Start(out var conflicts);
        _hotkeys.HotkeyPressed += _coordinator.HandleHotkey;

        if (conflicts.Count > 0)
        {
            string combos = string.Join(", ", conflicts.Select(HotkeyService.Describe));
            _tray.Notify("Hotkey conflict", $"{combos} could not be registered and are ignored.");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_hotkeys is not null)
        {
            if (_coordinator is not null)
            {
                _hotkeys.HotkeyPressed -= _coordinator.HandleHotkey;
            }

            _hotkeys.Dispose();
            _hotkeys = null;
        }

        _coordinator = null;

        _preview?.Shutdown();
        _preview = null;

        _tray?.Dispose();
        _tray = null;

        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }

        _mutex?.Dispose();
        _mutex = null;

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        TraceLog.Write($"unhandled ui exception: {e.Exception}");
        e.Handled = true;
    }
}
