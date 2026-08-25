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

    public void Initialize()
    {
        var icon = new TaskbarIcon
        {
            ToolTipText = "Screendrop",
            IconSource = BitmapFrame.Create(new Uri(IconUri, UriKind.Absolute)),
            ContextMenu = BuildMenu(),
            Visibility = Visibility.Visible,
        };

        icon.ForceCreate();

        _icon = icon;
    }

    private static ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        var quit = new MenuItem { Header = "Quit Screendrop" };
        quit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(quit);

        return menu;
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
            using var scaled = new Bitmap(source, new System.Drawing.Size(ThumbnailMaxDimension, ThumbnailMaxDimension));
            return scaled.GetHicon();
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
