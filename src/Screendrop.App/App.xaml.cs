using System.Diagnostics;
using System.IO;
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
    private const string SingleInstanceEventName = @"Local\Screendrop.ShowSettings";

    private Mutex? _mutex;
    private EventWaitHandle? _showSettingsSignal;
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
            // Mac parity: relaunching surfaces Settings when the tray icon is
            // hidden (the only way back into the app in that state).
            try
            {
                if (EventWaitHandle.TryOpenExisting(SingleInstanceEventName, out var signal))
                {
                    signal.Set();
                    signal.Dispose();
                }
            }
            catch (Exception)
            {
            }

            Shutdown(0);
            return;
        }

        _showSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, SingleInstanceEventName);
        _ = Task.Run(WaitForShowSettingsSignal);

        base.OnStartup(e);

        var initialSettings = SettingsStore.Load();

        _tray = new TrayController();
        _tray.Initialize(new TrayController.TrayActions(
            OpenSettings: OpenSettings,
            CaptureFullscreen: () => _coordinator?.HandleHotkey(null, CaptureMode.Fullscreen),
            CaptureWindow: () => _coordinator?.HandleHotkey(null, CaptureMode.Window),
            CaptureArea: () => _coordinator?.HandleHotkey(null, CaptureMode.Area),
            OpenScreenshotsFolder: OpenScreenshotsFolder));
        _tray.SetVisible(initialSettings.ShowTrayIcon);

        _preview = new PreviewPanelPresenter(_tray.Notify);
        _coordinator = new CaptureCoordinator(_tray.Notify, OnCaptureCompleted);
        _hotkeys = HotkeyService.Start(out var conflicts);
        _hotkeys.HotkeyPressed += _coordinator.HandleHotkey;

        if (conflicts.Count > 0)
        {
            string combos = string.Join(", ", conflicts.Select(HotkeyService.Describe));
            _tray.Notify("Hotkey conflict", $"{combos} could not be registered and are ignored.");
        }
    }

    private void OnCaptureCompleted(AfterCaptureResult result, string captureType)
    {
        _preview?.OnCapture(result, captureType);

        var settings = SettingsStore.Load();
        if (settings.AfterCaptureAnnotate)
        {
            _preview?.OpenEditor(result);
        }
    }

    private static void OpenScreenshotsFolder()
    {
        try
        {
            var settings = SettingsStore.Load();
            string directory = string.IsNullOrWhiteSpace(settings.ExportDirectoryPath)
                ? SettingsStore.DefaultExportDirectoryPath
                : settings.ExportDirectoryPath;
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            TraceLog.Write($"could not open screenshots folder: {ex.Message}");
        }
    }

    private void WaitForShowSettingsSignal()
    {
        while (_showSettingsSignal is not null && _showSettingsSignal.WaitOne())
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (!SettingsStore.Load().ShowTrayIcon)
                {
                    OpenSettings();
                }
            });
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

        var signal = _showSettingsSignal;
        _showSettingsSignal = null;
        try
        {
            signal?.Set();
            signal?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

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
        _tray?.SetVisible(settings.ShowTrayIcon);

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
