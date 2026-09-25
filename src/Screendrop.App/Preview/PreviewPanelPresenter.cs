using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Screendrop.App.Capture;
using Screendrop.App.Editor;
using Screendrop.App.Infrastructure;
using Screendrop.Capture;
using Screendrop.Core.Annotations;
using Screendrop.Core.Background;
using Screendrop.Core.Geometry;
using Screendrop.Core.History;
using Screendrop.Core.Preview;
using Screendrop.Core.Settings;
using Screendrop.Rendering;
using SkiaSharp;

namespace Screendrop.App.Preview;

internal sealed class PreviewPanelPresenter
{
    // Only the newest capture is ever relevant. Capacity 1 means a new
    // screenshot evicts the previous card, which routes through OnEvicted so
    // the old staging file is still cleaned up.
    private readonly PreviewStack _stack = new(maxCount: 1);
    private readonly OverlayCardLayoutStore _layoutStore = new();
    private PreviewPanelWindow? _window;
    private MonitorInfo? _targetMonitor;
    private DispatcherTimer? _autoCloseTimer;

    public PreviewPanelPresenter()
    {
        _stack.Evicted += OnEvicted;
    }

    public void OnCapture(AfterCaptureResult result, string captureType)
    {
        var settings = SettingsStore.Load();
        if (!settings.AfterCaptureShowOverlay)
        {
            return;
        }

        var entry = new PreviewEntry(result.StagingPath, result.SavedPath, captureType, DateTimeOffset.Now);
        _stack.Push(entry);

        // Mac parity (PreviewWindowPlacement.setTargetDisplayID): the panel
        // appears on the display the capture came from, not whichever monitor
        // happens to hold the foreground window.
        _targetMonitor = MonitorEnumerator.GetMonitorForPoint(result.OriginX, result.OriginY);
        Show();
        RestartAutoClose(settings);
    }

    /// Opens the annotation editor directly (after-capture "annotate" action).
    public void OpenEditor(AfterCaptureResult result)
    {
        var entry = _stack.Items.FirstOrDefault(e => e.StagingPath == result.StagingPath);
        if (entry is not null)
        {
            Edit(entry);
        }
    }

    public void Show()
    {
        EnsureWindow();
        _window!.SetCollapsed(false);
        PositionWindow();
        Refresh();
        _window.ShowPanel();
    }

    public void Shutdown()
    {
        _autoCloseTimer?.Stop();
        _window?.Close();
        _window = null;
    }

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            ApplyCaptureExclusion(_window);
            return;
        }

        var window = new PreviewPanelWindow();
        window.ActionRequested += OnAction;
        window.DraggedOut += OnDraggedOut;
        window.CollapsedChanged += PositionWindow;
        window.Show();

        AcrylicHelper.TryEnableAcrylic(window, 0x00000000);
        ApplyCaptureExclusion(window);

        _window = window;
    }

    private static void ApplyCaptureExclusion(PreviewPanelWindow window)
    {
        IntPtr handle = window.WindowHandle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        bool includeInCaptures = SettingsStore.Load().IncludeAppWindowsInCaptures;
        if (includeInCaptures)
        {
            DisplayAffinity.IncludeInCapture(handle);
        }
        else
        {
            DisplayAffinity.ExcludeFromCapture(handle);
        }
    }

    private void PositionWindow()
    {
        var monitor = _targetMonitor ?? MonitorEnumerator.GetFocusedMonitor();
        if (monitor is null || _window is null)
        {
            return;
        }

        var settings = SettingsStore.Load();
        var (scaleX, scaleY) = MonitorGeometry.GetScale(monitor);
        bool dockRight = settings.PreviewPosition == PreviewPosition.Right;

        double widthDip;
        double heightDip;
        if (_window.IsCollapsed)
        {
            widthDip = 140;
            heightDip = 40;
        }
        else
        {
            int cardCount = Math.Min(_stack.Items.Count, VisibleCapacity(monitor, scaleY));
            cardCount = Math.Max(cardCount, 1);
            widthDip = PreviewPanelWindow.CardWidthDip + 16;
            heightDip = cardCount * PreviewPanelWindow.CardHeightDip
                + Math.Max(0, cardCount - 1) * PreviewPanelWindow.CardSpacingDip
                + 16;
        }

        int widthPhys = (int)Math.Round(widthDip * scaleX);
        int heightPhys = (int)Math.Round(heightDip * scaleY);
        var placement = PlacementResolver.ResolveTopCorner(
            monitor.PhysicalBounds, widthPhys, heightPhys, dockRight);

        _window.Width = placement.Width / scaleX;
        _window.Height = placement.Height / scaleY;
        _window.Left = placement.X / scaleX;
        _window.Top = placement.Y / scaleY;
    }

    private static int VisibleCapacity(MonitorInfo monitor, double scaleY)
    {
        double monitorHeightDip = monitor.PhysicalBounds.Height / scaleY;
        int capacity = (int)((monitorHeightDip - 120) / (PreviewPanelWindow.CardHeightDip + PreviewPanelWindow.CardSpacingDip));
        return Math.Clamp(capacity, 1, 6);
    }

    private void Refresh()
    {
        if (_window is null)
        {
            return;
        }

        var monitor = _targetMonitor ?? MonitorEnumerator.GetFocusedMonitor();
        var settings = SettingsStore.Load();
        var (scaleX, scaleY) = monitor is null ? (1.0, 1.0) : MonitorGeometry.GetScale(monitor);
        int capacity = monitor is null ? 6 : VisibleCapacity(monitor, scaleY);

        var cards = new List<(PreviewEntry Entry, BitmapSource? Thumbnail)>();
        foreach (var entry in _stack.Items)
        {
            // Composited so the card reflects any saved annotation edits.
            cards.Add((entry, BitmapSourceConverter.FromFileComposited(entry.ImagePath, 320)));
        }

        _window.SetPeekCount(_stack.Items.Count);
        _window.SetCards(cards, _layoutStore.Layout, settings.PreviewPosition == PreviewPosition.Right, capacity);
    }

    private void RestartAutoClose(ScreendropSettings settings)
    {
        _autoCloseTimer?.Stop();
        if (settings.PreviewAutoCloseSeconds <= 0)
        {
            _autoCloseTimer = null;
            return;
        }

        _autoCloseTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(settings.PreviewAutoCloseSeconds),
        };
        _autoCloseTimer.Interval = TimeSpan.FromSeconds(settings.PreviewAutoCloseSeconds);
        _autoCloseTimer.Tick -= OnAutoCloseTick;
        _autoCloseTimer.Tick += OnAutoCloseTick;
        _autoCloseTimer.Start();
    }

    private void OnAutoCloseTick(object? sender, EventArgs e)
    {
        // Mac parity: the overlay stays while it is being used.
        if (_window is { IsMouseOver: true })
        {
            return;
        }

        _autoCloseTimer?.Stop();
        _window?.HidePanel();
    }

    private void OnDraggedOut()
    {
        var settings = SettingsStore.Load();
        if (settings.PreviewCloseAfterDragging)
        {
            _window?.HidePanel();
        }
    }

    private void OnAction(PreviewEntry entry, CardAction action)
    {
        _window?.ClearError();

        switch (action)
        {
            case CardAction.Save:
                Save(entry);
                break;
            case CardAction.Copy:
                Copy(entry);
                break;
            case CardAction.Compress:
                CopyCompressed(entry);
                break;
            case CardAction.Annotate:
                Edit(entry);
                DismissPreview();
                break;
            case CardAction.View:
                Reveal(entry);
                DismissPreview();
                break;
            case CardAction.Delete:
                Remove(entry);
                break;
            case CardAction.Close:
                DismissPreview();
                break;
        }
    }

    /// <summary>
    /// Hides the card once the user is done with a capture. The entry stays in
    /// the stack (and its staging file on disk) because Annotate and View still
    /// need the file; the next capture evicts it and cleans up.
    /// </summary>
    private void DismissPreview()
    {
        _autoCloseTimer?.Stop();
        _window?.ClearError();
        _window?.HidePanel();
    }

    /// <summary>
    /// Reports a failure on the card. The app raises no toasts, and silently
    /// keeping the card up would be indistinguishable from the action simply
    /// doing nothing.
    /// </summary>
    private void ReportFailure(string message)
    {
        TraceLog.Write($"preview action failed: {message}");
        _window?.ShowError(message);
    }

    private void Save(PreviewEntry entry)
    {
        if (entry.SavedPath is not null)
        {
            TraceLog.Write("panel: save button on an already-saved capture");
            Remove(entry);
            DismissPreview();
            return;
        }

        var settings = SettingsStore.Load();
        if (!settings.EffectiveSaveButtonUsesFolder)
        {
            SaveWithDialog(entry, settings);
            return;
        }

        Task.Run(() =>
        {
            try
            {
                // Composited: the saved file carries any annotation edits.
                using var bitmap = AnnotationExport.LoadComposited(entry.ImagePath);
                if (bitmap is null)
                {
                    throw new InvalidOperationException("Could not load the image.");
                }

                string directory = string.IsNullOrWhiteSpace(settings.ExportDirectoryPath)
                    ? SettingsStore.DefaultExportDirectoryPath
                    : settings.ExportDirectoryPath;
                Directory.CreateDirectory(directory);

                bool useJpeg = settings.ExportFormat == ExportFormat.Jpeg;
                string extension = useJpeg ? "jpg" : "png";
                string fileName = FileNaming.BuildFileName(settings.FileNamePattern, entry.CapturedAt, entry.CaptureType, extension);
                string path = FileNaming.ResolveUnique(directory, fileName);

                byte[] bytes = useJpeg
                    ? JpegCompressor.Encode(bitmap, settings.CompressionQuality)
                    : JpegCompressor.EncodePng(bitmap);
                File.WriteAllBytes(path, bytes);

                TraceLog.Write($"panel: saved {path}");
                Remove(entry);
                DismissPreview();
            }
            catch (Exception ex)
            {
                // Mac parity: a failed save keeps the card so the user can retry.
                ReportFailure($"Save failed: {ex.Message}");
            }
        });
    }

    private void SaveWithDialog(PreviewEntry entry, ScreendropSettings settings)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save screenshot",
            Filter = "PNG image|*.png|JPEG image|*.jpg",
            FileName = FileNaming.BuildFileName(settings.FileNamePattern, entry.CapturedAt, entry.CaptureType, "png"),
            InitialDirectory = Directory.Exists(settings.ExportDirectoryPath)
                ? settings.ExportDirectoryPath
                : SettingsStore.DefaultExportDirectoryPath,
        };

        if (_window is not null && dialog.ShowDialog(_window) != true)
        {
            return;
        }

        string destination = dialog.FileName;
        Task.Run(() =>
        {
            try
            {
                using var bitmap = AnnotationExport.LoadComposited(entry.ImagePath);
                if (bitmap is null)
                {
                    throw new InvalidOperationException("Could not load the image.");
                }

                byte[] bytes = destination.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    || destination.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                    ? JpegCompressor.Encode(bitmap, settings.CompressionQuality)
                    : JpegCompressor.EncodePng(bitmap);
                File.WriteAllBytes(destination, bytes);

                TraceLog.Write($"panel: saved {destination}");
                Remove(entry);
                DismissPreview();
            }
            catch (Exception ex)
            {
                ReportFailure($"Save failed: {ex.Message}");
            }
        });
    }

    private void Copy(PreviewEntry entry)
    {
        Task.Run(() =>
        {
            try
            {
                // Composited: the clipboard carries any annotation edits.
                using var bitmap = AnnotationExport.LoadComposited(entry.ImagePath);
                if (bitmap is null)
                {
                    throw new InvalidOperationException("Could not load the image.");
                }

                ClipboardService.SetImage(bitmap);
                TraceLog.Write("panel: copied to clipboard");
                Remove(entry);
                DismissPreview();
            }
            catch (Exception ex)
            {
                // Mac parity: a failed copy keeps the card so the user can retry.
                ReportFailure($"Copy failed: {ex.Message}");
            }
        });
    }

    /// Copies a compressed JPEG of the capture (mac "Compress" card action).
    private void CopyCompressed(PreviewEntry entry)
    {
        Task.Run(() =>
        {
            try
            {
                using var bitmap = AnnotationExport.LoadComposited(entry.ImagePath);
                if (bitmap is null)
                {
                    throw new InvalidOperationException("Could not load the image.");
                }

                var settings = SettingsStore.Load();
                using var jpeg = new SKBitmap(bitmap.Info);
                byte[] bytes = JpegCompressor.Encode(bitmap, settings.CompressionQuality);

                var copy = SKBitmap.Decode(bytes);
                if (copy is null)
                {
                    throw new InvalidOperationException("Could not compress the image.");
                }

                using (copy)
                {
                    ClipboardService.SetImage(copy);
                }

                TraceLog.Write("panel: copied compressed JPG");
                DismissPreview();
            }
            catch (Exception ex)
            {
                ReportFailure($"Compress failed: {ex.Message}");
            }
        });
    }

    private static void Reveal(PreviewEntry entry)
    {
        try
        {
            if (File.Exists(entry.ImagePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "explorer.exe", $"/select,\"{entry.ImagePath}\"")
                { UseShellExecute = true });
            }
        }
        catch (Exception)
        {
        }
    }

    private void Edit(PreviewEntry entry)
    {
        string imagePath = entry.ImagePath;
        if (!File.Exists(imagePath))
        {
            ReportFailure("The image file is missing.");
            return;
        }

        var editor = new AnnotationEditorWindow(imagePath);
        editor.Saved += _ =>
        {
            // Edits live in the sidecar now; refresh so the card shows them.
            Refresh();
            TraceLog.Write($"panel: annotations saved for {imagePath}");
        };
        editor.Show();
    }

    private void OnEvicted(PreviewEntry entry)
    {
        DeleteStagingFile(entry);
    }

    private void Remove(PreviewEntry entry)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            RemoveOnUiThread(entry);
        }
        else
        {
            dispatcher.BeginInvoke(() => RemoveOnUiThread(entry));
        }
    }

    private void RemoveOnUiThread(PreviewEntry entry)
    {
        if (!_stack.Remove(entry))
        {
            return;
        }

        DeleteStagingFile(entry);
        Refresh();
        PositionWindow();

        if (_stack.Items.Count == 0)
        {
            _window?.HidePanel();
        }
    }

    /// Deletes the staged temporary PNG backing a card plus its annotation
    /// sidecar (if any). The saved export (if any) is never touched - mac's
    /// deleteScreenshot has the same split.
    private static void DeleteStagingFile(PreviewEntry entry)
    {
        try
        {
            if (File.Exists(entry.StagingPath))
            {
                File.Delete(entry.StagingPath);
                TraceLog.Write($"panel: deleted staging file {entry.StagingPath}");
            }

            string sidecar = AnnotationDocument.SidecarPathFor(entry.StagingPath);
            if (File.Exists(sidecar))
            {
                File.Delete(sidecar);
                TraceLog.Write($"panel: deleted sidecar {sidecar}");
            }
        }
        catch (Exception ex)
        {
            TraceLog.Write($"panel: failed to delete staging file: {ex.Message}");
        }
    }

}
