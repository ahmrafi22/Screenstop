using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Screendrop.App.AreaSelect;
using Screendrop.App.Hotkeys;
using Screendrop.App.Infrastructure;
using Screendrop.App.WindowPicker;
using Screendrop.Capture;
using Screendrop.Core.Geometry;
using Screendrop.Core.Settings;
using SkiaSharp;

namespace Screendrop.App.Capture;

internal sealed class CaptureCoordinator
{
    public delegate void NotifyHandler(string title, string message, string? thumbnailPath = null);

    private readonly NotifyHandler _notify;
    private readonly Action<AfterCaptureResult, string>? _onCapture;
    private int _busy;

    public CaptureCoordinator(NotifyHandler notify, Action<AfterCaptureResult, string>? onCapture = null)
    {
        _notify = notify;
        _onCapture = onCapture;
    }

    /// Runs the self-timer countdown on the UI thread (mac captureDelaySeconds
    /// parity). Returns immediately when the delay is off.
    private Task RunCountdownAsync()
    {
        int delay = SettingsStore.Load().CaptureDelaySeconds;
        if (delay <= 0)
        {
            return Task.CompletedTask;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return Task.CompletedTask;
        }

        return dispatcher.Invoke(() => CountdownOverlay.RunAsync(delay));
    }

    public void HandleHotkey(object? sender, CaptureMode mode)
    {
        switch (mode)
        {
            case CaptureMode.Fullscreen:
                CaptureFocusedDisplay();
                break;
            case CaptureMode.Window:
                RunWindowPick();
                break;
            case CaptureMode.Area:
                RunAreaSelection();
                break;
        }
    }

    private bool TryBegin()
    {
        return Interlocked.CompareExchange(ref _busy, 1, 0) == 0;
    }

    private void EndCapture()
    {
        Interlocked.Exchange(ref _busy, 0);
    }

    private void RunWindowPick()
    {
        var monitors = MonitorEnumerator.Enumerate();
        if (monitors.Count == 0)
        {
            NotifyUi("Capture failed", "No display found.");
            return;
        }

        if (!TryBegin())
        {
            TraceLog.Write("capture ignored: busy");
            return;
        }

        var dispatcher = Application.Current.Dispatcher;

        Task.Run(async () =>
        {
            try
            {
                await RunCountdownAsync();

                WindowInfo? picked = null;
                dispatcher.Invoke(() => { picked = WindowPickerController.Pick(monitors); });
                if (picked is null)
                {
                    TraceLog.Write("window pick cancelled");
                    return;
                }

                TraceLog.Write($"picked {picked.Title} {picked.Bounds}");

                using var bitmap = WindowCapturer.CaptureWindow(picked);
                var result = AfterCapturePipeline.Run(bitmap, "window", picked.Bounds.X, picked.Bounds.Y);
                TraceLog.Write($"pipeline window: {result.DisplaySummary}");
                NotifyUi("Screenshot captured", result.DisplaySummary, result.ThumbnailPath);
                InvokeCaptureComplete(result, "window");
            }
            catch (Exception ex)
            {
                TraceLog.Write($"window capture exception: {ex}");
                NotifyUi("Capture failed", ex.Message);
            }
            finally
            {
                EndCapture();
            }
        });
    }

    private void RunAreaSelection()
    {
        var monitor = MonitorEnumerator.GetFocusedMonitor();
        if (monitor is null)
        {
            NotifyUi("Capture failed", "No display found.");
            return;
        }

        if (!TryBegin())
        {
            TraceLog.Write("capture ignored: busy");
            return;
        }

        TraceLog.Write($"area: monitor {monitor.DeviceName} {monitor.PhysicalBounds}");

        var dispatcher = Application.Current.Dispatcher;

        Task.Run(async () =>
        {
            try
            {
                await RunCountdownAsync();

                TraceLog.Write("area: capturing full monitor on background");
                using var full = GDICapturer.CaptureMonitor(monitor);
                TraceLog.Write("area: full capture done, invoking dispatcher");
                var captured = full.Bitmap;

                PixelRect? rect = null;
                dispatcher.Invoke(() =>
                {
                    TraceLog.Write("area: on UI thread, showing overlay");
                    rect = AreaSelectionController.Pick(monitor);
                    TraceLog.Write("area: overlay closed");
                });

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

                // Crop + pipeline (PNG encode, file writes, clipboard) run on
                // this background thread; only the overlay needed the UI thread.
                var result = AfterCapturePipeline.Run(crop, "area", region.X, region.Y);
                TraceLog.Write($"pipeline area: {result.DisplaySummary}");
                NotifyUi("Screenshot captured", result.DisplaySummary, result.ThumbnailPath);
                InvokeCaptureComplete(result, "area");
            }
            catch (Exception ex)
            {
                TraceLog.Write($"area capture exception: {ex}");
                NotifyUi("Capture failed", ex.Message);
            }
            finally
            {
                EndCapture();
            }
        });
    }

    private void CaptureFocusedDisplay()
    {
        if (!TryBegin())
        {
            TraceLog.Write("capture ignored: busy");
            return;
        }

        Task.Run(async () =>
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

                await RunCountdownAsync();

                using var capture = GDICapturer.CaptureMonitor(monitor);
                var result = AfterCapturePipeline.Run(capture.Bitmap, "fullscreen", monitor.PhysicalBounds.X, monitor.PhysicalBounds.Y);
                Infrastructure.TraceLog.Write($"pipeline fullscreen: {result.DisplaySummary}");
                NotifyUi("Screenshot captured", result.DisplaySummary, result.ThumbnailPath);
                InvokeCaptureComplete(result, "fullscreen");
            }
            catch (Exception ex)
            {
                Infrastructure.TraceLog.Write($"capture exception: {ex}");
                NotifyUi("Capture failed", ex.Message);
            }
            finally
            {
                EndCapture();
            }
        });
    }

    private void InvokeCaptureComplete(AfterCaptureResult result, string captureType)
    {
        if (_onCapture is null)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Normal, () => _onCapture(result, captureType));
    }

    private void NotifyUi(string title, string message, string? thumbnailPath = null)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Normal, () => _notify(title, message, thumbnailPath));
    }
}
