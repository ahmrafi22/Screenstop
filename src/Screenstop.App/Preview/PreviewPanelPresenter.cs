using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Screenstop.App.Capture;
using Screenstop.App.Editor;
using Screenstop.App.Infrastructure;
using Screenstop.Capture;
using Screenstop.Core.Annotations;
using Screenstop.Core.Geometry;
using Screenstop.Core.History;
using Screenstop.Core.Preview;
using Screenstop.Core.Settings;
using Screenstop.Rendering;
using SkiaSharp;

namespace Screenstop.App.Preview;

internal sealed class PreviewPanelPresenter
{
    private readonly PreviewStack _stack = new(maxCount: 6);
    private readonly CaptureCoordinator.NotifyHandler _notify;
    private PreviewPanelWindow? _window;
    private MonitorInfo? _targetMonitor;

    public PreviewPanelPresenter(CaptureCoordinator.NotifyHandler notify)
    {
        _notify = notify;
        _stack.Evicted += OnEvicted;
    }

    public void OnCapture(AfterCaptureResult result, string captureType)
    {
        var entry = new PreviewEntry(result.StagingPath, result.SavedPath, captureType, DateTimeOffset.Now);
        _stack.Push(entry);

        // Mac parity (PreviewWindowPlacement.setTargetDisplayID): the panel
        // appears on the display the capture came from, not whichever monitor
        // happens to hold the foreground window.
        _targetMonitor = MonitorEnumerator.GetMonitorForPoint(result.OriginX, result.OriginY);
        Show();
    }

    public void Show()
    {
        EnsureWindow();
        PositionWindow();
        Refresh();
        _window!.ShowPanel();
    }

    public void Shutdown()
    {
        _window?.Close();
        _window = null;
    }

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            return;
        }

        var window = new PreviewPanelWindow();
        window.ActionRequested += OnAction;
        window.Show();

        IntPtr handle = window.WindowHandle;
        if (handle != IntPtr.Zero)
        {
            DisplayAffinity.ExcludeFromCapture(handle);
        }

        _window = window;
    }

    private void PositionWindow()
    {
        var monitor = _targetMonitor ?? MonitorEnumerator.GetFocusedMonitor();
        if (monitor is null || _window is null)
        {
            return;
        }

        var (scaleX, scaleY) = MonitorGeometry.GetScale(monitor);
        int cardCount = _stack.Items.Count;
        double panelWidthDip = (cardCount * PreviewPanelWindow.CardWidthDip)
            + (Math.Max(0, cardCount - 1) * PreviewPanelWindow.CardPitchDip)
            + (PreviewPanelWindow.PaddingDip * 2);

        double maxWidthDip = (monitor.PhysicalBounds.Width / scaleX) - 20;
        if (panelWidthDip > maxWidthDip)
        {
            panelWidthDip = Math.Max(PreviewPanelWindow.CardWidthDip + (PreviewPanelWindow.PaddingDip * 2), maxWidthDip);
        }

        int panelWidthPhys = (int)Math.Round(panelWidthDip * scaleX);
        int panelHeightPhys = (int)Math.Round(PreviewPanelWindow.PanelHeightDip * scaleY);
        var placement = PlacementResolver.ResolveBottomCenter(monitor.PhysicalBounds, panelWidthPhys, panelHeightPhys);

        _window.Width = placement.Width / scaleX;
        _window.Height = placement.Height / scaleY;
        _window.Left = placement.X / scaleX;
        _window.Top = placement.Y / scaleY;
    }

    private void Refresh()
    {
        var cards = new List<(PreviewEntry Entry, BitmapSource? Thumbnail)>();
        foreach (var entry in _stack.Items)
        {
            // Composited so the card reflects any saved annotation edits.
            cards.Add((entry, BitmapSourceConverter.FromFileComposited(entry.ImagePath, 320)));
        }

        _window?.SetCards(cards);
    }

    private void OnAction(PreviewEntry entry, PreviewAction action)
    {
        switch (action)
        {
            case PreviewAction.Save:
                Save(entry);
                break;
            case PreviewAction.Copy:
                Copy(entry);
                break;
            case PreviewAction.Edit:
                Edit(entry);
                break;
            case PreviewAction.Discard:
                Remove(entry);
                break;
        }
    }

    private void Save(PreviewEntry entry)
    {
        if (entry.SavedPath is not null)
        {
            _notify("Screenshot", "Already saved.");
            Remove(entry);
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

                var settings = SettingsStore.Load();
                string directory = string.IsNullOrWhiteSpace(settings.ExportDirectoryPath)
                    ? SettingsStore.DefaultExportDirectoryPath
                    : settings.ExportDirectoryPath;
                Directory.CreateDirectory(directory);

                string extension = settings.AutoCompress ? "jpg" : "png";
                string fileName = FileNaming.BuildFileName(settings.FileNamePattern, entry.CapturedAt, entry.CaptureType, extension);
                string path = FileNaming.ResolveUnique(directory, fileName);

                byte[] bytes = settings.AutoCompress
                    ? JpegCompressor.Encode(bitmap, settings.CompressionQuality)
                    : JpegCompressor.EncodePng(bitmap);
                File.WriteAllBytes(path, bytes);

                Notify("Saved", Path.GetFileName(path));
                Remove(entry);
            }
            catch (Exception ex)
            {
                // Mac parity: a failed save keeps the card so the user can retry.
                Notify("Save failed", ex.Message);
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
                Notify("Copied to clipboard", string.Empty);
                Remove(entry);
            }
            catch (Exception ex)
            {
                // Mac parity: a failed copy keeps the card so the user can retry.
                Notify("Copy failed", ex.Message);
            }
        });
    }

    private void Edit(PreviewEntry entry)
    {
        string imagePath = entry.ImagePath;
        if (!File.Exists(imagePath))
        {
            _notify("Screenstop", "The image file is missing.");
            return;
        }

        var editor = new AnnotationEditorWindow(imagePath);
        editor.Saved += _ =>
        {
            // Edits live in the sidecar now; refresh so the card shows them.
            Refresh();
            _notify("Annotated", Path.GetFileName(imagePath));
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

    private void Notify(string title, string message)
    {
        _notify(title, message);
    }
}
