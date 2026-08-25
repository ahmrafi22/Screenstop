using System.IO;
using System.Windows;
using System.Windows.Threading;
using Screenstop.App.AreaSelect;
using Screenstop.App.Hotkeys;
using Screenstop.App.Infrastructure;
using Screenstop.Capture;
using Screenstop.Core.Geometry;
using SkiaSharp;

namespace Screenstop.App.Capture;

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
                _notify("Screenstop", "Window capture arrives with the next phase.");
                break;
            case CaptureMode.Area:
                RunAreaSelection();
                break;
        }
    }

    private void RunAreaSelection()
    {
        var monitor = MonitorEnumerator.GetFocusedMonitor();
        if (monitor is null)
        {
            NotifyUi("Capture failed", "No display found.");
            return;
        }

        TraceLog.Write($"area: monitor {monitor.DeviceName} {monitor.PhysicalBounds}");

        var dispatcher = Application.Current.Dispatcher;

        Task.Run(() =>
        {
            try
            {
                TraceLog.Write("area: capturing full monitor on background");
                using var full = GDICapturer.CaptureMonitor(monitor);
                TraceLog.Write("area: full capture done, invoking dispatcher");
                var captured = full.Bitmap;

                dispatcher.Invoke(() =>
                {
                    TraceLog.Write("area: on UI thread, showing overlay");
                    var rect = AreaSelectionController.Pick(monitor);
                    TraceLog.Write("area: overlay closed");
                    if (rect is null)
                    {
                        TraceLog.Write("area selection cancelled");
                        return;
                    }

                    var region = PixelRect.Intersect(rect.Value, monitor.PhysicalBounds);
                    if (region.IsEmpty)
                    {
                        NotifyUi("Capture failed", "Invalid selection region.");
                        return;
                    }

                    var offsetX = region.X - monitor.PhysicalBounds.X;
                    var offsetY = region.Y - monitor.PhysicalBounds.Y;

                    using var crop = new SKBitmap(new SKImageInfo(region.Width, region.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
                    if (!captured.ExtractSubset(crop, new SKRectI(offsetX, offsetY, offsetX + region.Width, offsetY + region.Height)))
                    {
                        NotifyUi("Capture failed", "Could not extract selection.");
                        return;
                    }

                    string path = TempScreenshotStore.SavePng(crop);
                    TraceLog.Write($"saved area {path} ({crop.Width}x{crop.Height})");
                    NotifyUi("Screenshot captured", Path.GetFileName(path));
                });
            }
            catch (Exception ex)
            {
                TraceLog.Write($"area capture exception: {ex}");
                NotifyUi("Capture failed", ex.Message);
            }
        });
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
