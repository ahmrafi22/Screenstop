using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Screenstop.App.Capture;
using Screenstop.App.Infrastructure;
using Screenstop.Capture;
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

    public PreviewPanelPresenter(CaptureCoordinator.NotifyHandler notify)
    {
        _notify = notify;
        _stack.Evicted += OnEvicted;
    }

    public void OnCapture(AfterCaptureResult result, string captureType)
    {
        var entry = new PreviewEntry(result.StagingPath, result.SavedPath, captureType, DateTimeOffset.Now);
        _stack.Push(entry);
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
        var monitor = MonitorEnumerator.GetFocusedMonitor();
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
            cards.Add((entry, BitmapSourceConverter.FromFile(entry.ImagePath, 320)));
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
                using var bitmap = SKBitmap.Decode(entry.ImagePath);
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
                using var bitmap = SKBitmap.Decode(entry.ImagePath);
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
        _notify("Screenstop", "Annotation editor arrives with Phase 5.");
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

    /// Deletes the staged temporary PNG backing a card. The saved export (if
    /// any) is never touched - mac's deleteScreenshot has the same split.
    private static void DeleteStagingFile(PreviewEntry entry)
    {
        try
        {
            if (File.Exists(entry.StagingPath))
            {
                File.Delete(entry.StagingPath);
                TraceLog.Write($"panel: deleted staging file {entry.StagingPath}");
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
