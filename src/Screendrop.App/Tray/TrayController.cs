using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace Screendrop.App.Tray;

internal sealed class TrayController : IDisposable
{
    private const string IconUri = "pack://application:,,,/Assets/screendrop.ico";
    private const int ThumbnailMaxDimension = 32;
    private static readonly TimeSpan IconRetention = TimeSpan.FromSeconds(15);

    private TaskbarIcon? _icon;

    /// Tray menu callbacks (mac MenuBarView parity, minus recording/cloud).
    public sealed record TrayActions(
        Action? OpenSettings,
        Action? CaptureFullscreen,
        Action? CaptureWindow,
        Action? CaptureArea,
        Action? OpenScreenshotsFolder);

    public void Initialize(TrayActions actions)
    {
        var icon = new TaskbarIcon
        {
            ToolTipText = "Screendrop",
            IconSource = BitmapFrame.Create(new Uri(IconUri, UriKind.Absolute)),
            ContextMenu = BuildMenu(actions),
            Visibility = Visibility.Visible,
        };

        icon.ForceCreate();

        _icon = icon;
    }

    /// Shows or hides the tray icon (mac showMenuBarIcon parity). Hotkeys keep
    /// working while hidden; relaunching Screendrop reopens Settings.
    public void SetVisible(bool visible)
    {
        if (_icon is null)
        {
            return;
        }

        if (visible && !_icon.IsCreated)
        {
            _icon.ForceCreate();
        }

        _icon.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static ContextMenu BuildMenu(TrayActions actions)
    {
        var menu = new ContextMenu();

        AddItem(menu, "Capture Fullscreen", actions.CaptureFullscreen);
        AddItem(menu, "Capture Window", actions.CaptureWindow);
        AddItem(menu, "Capture Area", actions.CaptureArea);
        menu.Items.Add(new Separator());
        AddItem(menu, "Open Screenshots Folder", actions.OpenScreenshotsFolder);

        if (actions.OpenSettings is not null)
        {
            var settings = new MenuItem { Header = "Settings…" };
            settings.Click += (_, _) => actions.OpenSettings();
            menu.Items.Add(settings);
        }

        menu.Items.Add(new Separator());

        var quit = new MenuItem { Header = "Quit Screendrop" };
        quit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(quit);

        return menu;
    }

    private static void AddItem(ContextMenu menu, string header, Action? action)
    {
        if (action is null)
        {
            return;
        }

        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    public void Notify(string title, string message, string? thumbnailPath = null)
    {
        if (_icon is null)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => Notify(title, message, thumbnailPath));
            return;
        }

        IntPtr customIcon = thumbnailPath is null ? IntPtr.Zero : LoadThumbnailIcon(thumbnailPath);
        _icon.ShowNotification(title, message, NotificationIcon.None, customIcon, largeIcon: true, sound: false, respectQuietTime: false, realtime: false, timeout: null);
        if (customIcon != IntPtr.Zero)
        {
            ScheduleIconRelease(customIcon);
        }
    }

    private static IntPtr LoadThumbnailIcon(string imagePath)
    {
        try
        {
            using var source = new Bitmap(imagePath);

            // The notification icon contract is exactly 32x32 (anything else
            // throws), so the capture is aspect-fit inside a transparent
            // 32x32 canvas instead of being squashed into a square.
            using var canvas = new Bitmap(ThumbnailMaxDimension, ThumbnailMaxDimension);
            double scale = Math.Min(
                (double)ThumbnailMaxDimension / source.Width,
                (double)ThumbnailMaxDimension / source.Height);
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));

            using (var graphics = System.Drawing.Graphics.FromImage(canvas))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(
                    source,
                    (ThumbnailMaxDimension - width) / 2,
                    (ThumbnailMaxDimension - height) / 2,
                    width,
                    height);
            }

            return canvas.GetHicon();
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static void ScheduleIconRelease(IntPtr hIcon)
    {
        Task.Run(async () =>
        {
            await Task.Delay(IconRetention);
            DestroyIcon(hIcon);
        });
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }
}
