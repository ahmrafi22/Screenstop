using System.Threading;
using System.Windows;
using Screendrop.App.Tray;

namespace Screendrop.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\Screendrop.SingleInstance";

    private Mutex? _mutex;
    private TrayController? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

        _tray = new TrayController();
        _tray.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
}
