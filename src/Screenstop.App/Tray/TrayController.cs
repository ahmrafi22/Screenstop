using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using H.NotifyIcon;

namespace Screenstop.App.Tray;

internal sealed class TrayController : IDisposable
{
    private const string IconUri = "pack://application:,,,/Assets/screenstop.ico";

    private TaskbarIcon? _icon;

    public void Initialize()
    {
        var icon = new TaskbarIcon
        {
            ToolTipText = "Screenstop",
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

        var quit = new MenuItem { Header = "Quit Screenstop" };
        quit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(quit);

        return menu;
    }

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }
}
