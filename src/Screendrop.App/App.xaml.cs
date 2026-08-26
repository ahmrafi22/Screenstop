using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Screendrop.App.Capture;
using Screendrop.App.Hotkeys;
using Screendrop.App.Infrastructure;
using Screendrop.App.Preview;
using Screendrop.App.Settings;
using Screendrop.App.Tray;
using Screendrop.Core.Diagnostics;
using Screendrop.Core.Settings;

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
        {
            TraceLog.Write($"unhandled appdomain exception: {args.ExceptionObject}");
            WriteCrashReport(args.ExceptionObject as Exception, args.IsTerminating);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            TraceLog.Write($"unobserved task exception: {args.Exception}");
            WriteCrashReport(args.Exception, isTerminating: false);
            args.SetObserved();
        };

        _mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

        _tray = new TrayController();
        _tray.Initialize(OpenSettings);

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
        WriteCrashReport(e.Exception, isTerminating: false);
        e.Handled = true;
    }

    private SettingsWindow? _settingsWindow;

    private void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        var settings = SettingsStore.Load();
        _settingsWindow = new SettingsWindow(settings, ApplySettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ApplySettings(ScreendropSettings settings)
    {
        settings.Normalize();
        SettingsStore.Save(settings);
        TraceLog.Write("settings saved from settings window");

        LaunchAtLogin.SetEnabled(settings.LaunchAtLogin);

        if (_hotkeys is not null)
        {
            _hotkeys.Reload(out var conflicts);
            if (conflicts.Count > 0)
            {
                string combos = string.Join(", ", conflicts.Select(HotkeyService.Describe));
                _tray?.Notify("Hotkey conflict", $"{combos} could not be registered and are ignored.");
            }
        }
    }

    private static void WriteCrashReport(Exception? exception, bool isTerminating)
    {
        if (exception is null)
        {
            return;
        }

        var report = new CrashReport(
            DateTimeOffset.Now,
            AppVersion,
            RuntimeInformation.OSDescription,
            exception.ToString(),
            isTerminating);

        CrashLog.Write(report);
    }

    private static string AppVersion =>
        typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown";
}
