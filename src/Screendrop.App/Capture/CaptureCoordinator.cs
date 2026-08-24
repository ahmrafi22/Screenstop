using System.IO;
using System.Windows;
using System.Windows.Threading;
using Screendrop.App.Hotkeys;
using Screendrop.App.Infrastructure;
using Screendrop.Capture;

namespace Screendrop.App.Capture;

internal sealed class CaptureCoordinator
{
    public delegate void NotifyHandler(string title, string message);

    private readonly NotifyHandler _notify;

    public CaptureCoordinator(NotifyHandler notify)
    {
        _notify = notify;
    }

    public void HandleHotkey(object? sender, CaptureMode mode)
    {
        switch (mode)
        {
            case CaptureMode.Fullscreen:
                CaptureFocusedDisplay();
                break;
            case CaptureMode.Window:
                _notify("Screendrop", "Window capture arrives with the area/window phase.");
                break;
            case CaptureMode.Area:
                _notify("Screendrop", "Area selection arrives with the area/window phase.");
                break;
        }
    }

    private void CaptureFocusedDisplay()
    {
        Task.Run(() =>
        {
            try
            {
                var monitor = MonitorEnumerator.GetFocusedMonitor();
                if (monitor is null)
                {
                    NotifyUi("Capture failed", "No display found to capture.");
                    Infrastructure.TraceLog.Write("capture aborted: no monitor");
                    return;
                }

                using var capture = GDICapturer.CaptureMonitor(monitor);
                string path = TempScreenshotStore.SavePng(capture.Bitmap);
                Infrastructure.TraceLog.Write($"saved {path} ({capture.Bitmap.Width}x{capture.Bitmap.Height})");
                NotifyUi("Screenshot captured", Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                Infrastructure.TraceLog.Write($"capture exception: {ex}");
                NotifyUi("Capture failed", ex.Message);
            }
        });
    }

    private void NotifyUi(string title, string message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Normal, () => _notify(title, message));
    }
}
