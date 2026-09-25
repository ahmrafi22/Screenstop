using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Screenstop.App.Infrastructure;
using Screenstop.Capture;
using Screenstop.Core.Settings;

namespace Screenstop.App.Notifications;

/// <summary>
/// Screenstop's own notification surface: a borderless, non-activating card
/// stack anchored to the top-right of the monitor the user is working on.
///
/// This replaces the shell toast/balloon entirely. A screenshot tool that pops
/// a system notification steals focus and lands in the Action Center, which is
/// exactly the wrong behaviour for a hotkey the user fires mid-task, so every
/// message routes through here instead and fades itself away.
/// </summary>
internal sealed class ToastPresenter : IDisposable
{
    private const double CardWidth = 296;
    private const double EdgeMargin = 16;
    private const double CardSpacing = 10;
    private const int MaxVisible = 3;
    private const int LifetimeMilliseconds = 3600;
    private const int SweepMilliseconds = 250;

    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private readonly List<ToastCard> _cards = new();
    private readonly StackPanel _stack = new() { Margin = new Thickness(0) };
    private readonly DispatcherTimer _sweep;
    private Window? _window;
    private bool _disposed;

    public ToastPresenter()
    {
        _sweep = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(SweepMilliseconds),
        };
        _sweep.Tick += (_, _) => ExpireOldCards();
    }

    /// <summary>
    /// Shows a card in the top-right corner. <paramref name="thumbnailPath"/>
    /// adds a small preview of the capture; failures tint the card's status
    /// dot red so they read differently at a glance.
    /// </summary>
    public void Show(string title, string message, string? thumbnailPath = null)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || _disposed)
        {
            return;
        }

        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(DispatcherPriority.Normal, () => Show(title, message, thumbnailPath));
            return;
        }

        try
        {
            EnsureWindow();
            var card = BuildCard(title, message, thumbnailPath);
            _cards.Insert(0, card);
            _stack.Children.Insert(0, card.Root);

            while (_cards.Count > MaxVisible)
            {
                Dismiss(_cards[^1], immediate: true);
            }

            Position();
            AnimateIn(card.Root);
            _sweep.Start();
        }
        catch (Exception ex)
        {
            // A notification must never take the app down with it.
            TraceLog.Write($"toast failed: {ex}");
        }
    }

    private ToastCard BuildCard(string title, string message, string? thumbnailPath)
    {
        bool failed = LooksLikeFailure(title);
        var accent = Brush(failed ? "Sd.Danger" : "Sd.Accent");

        var dot = new Border
        {
            Width = 7,
            Height = 7,
            CornerRadius = new CornerRadius(4),
            Background = accent,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 5, 0, 0),
        };

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("Sd.Text"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (!string.IsNullOrWhiteSpace(message))
        {
            text.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 11.5,
                Foreground = Brush("Sd.TextMuted"),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 34,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(dot, 0);
        Grid.SetColumn(text, 1);
        row.Children.Add(dot);
        row.Children.Add(text);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var image = LoadThumbnail(thumbnailPath);
        if (image is not null)
        {
            var frame = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(7),
                ClipToBounds = true,
                Background = Brush("Sd.Panel"),
                Child = new Image
                {
                    Source = image,
                    Stretch = Stretch.UniformToFill,
                    SnapsToDevicePixels = true,
                },
                Margin = new Thickness(0, 0, 10, 0),
            };
            Grid.SetColumn(frame, 0);
            content.Children.Add(frame);
        }

        Grid.SetColumn(row, image is null ? 0 : 1);
        if (image is null)
        {
            row.Margin = new Thickness(0);
            content.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        }

        content.Children.Add(row);

        var card = new Border
        {
            Width = CardWidth,
            Background = Brush("Sd.BgRaised"),
            BorderBrush = Brush("Sd.Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(12, 10, 12, 11),
            Margin = new Thickness(0, 0, 0, CardSpacing),
            Effect = new DropShadowEffect
            {
                BlurRadius = 26,
                ShadowDepth = 5,
                Direction = 270,
                Opacity = 0.22,
                Color = Colors.Black,
            },
            RenderTransform = new TranslateTransform(18, 0),
            RenderTransformOrigin = new Point(1, 0.5),
            Child = content,
        };

        return new ToastCard(card, DateTime.UtcNow);
    }

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            return;
        }

        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Height,
            Width = CardWidth,
            Focusable = false,
            IsHitTestVisible = false,
            Title = "Screenstop notification",
            Content = new Grid
            {
                // Room for the drop shadow so it is not clipped by the window.
                Margin = new Thickness(18, 14, 18, 18),
                Children = { _stack },
            },
        };

        _window.SourceInitialized += OnWindowSourceInitialized;
        _window.Closed += (_, _) =>
        {
            _window = null;
        };
        _window.Show();
    }

    private void OnWindowSourceInitialized(object? sender, EventArgs e)
    {
        if (_window is null)
        {
            return;
        }

        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // Never steal focus, never show in Alt+Tab, and never swallow a click
        // meant for the app underneath - a screenshot toast must be purely
        // informational, never a click target.
        int exStyle = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, exStyle | WsExNoActivate | WsExToolWindow | WsExTransparent);

        ApplyCaptureExclusion(handle);
    }

    private static void ApplyCaptureExclusion(IntPtr handle)
    {
        try
        {
            if (SettingsStore.Load().IncludeAppWindowsInCaptures)
            {
                DisplayAffinity.IncludeInCapture(handle);
            }
            else
            {
                DisplayAffinity.ExcludeFromCapture(handle);
            }
        }
        catch (Exception ex)
        {
            TraceLog.Write($"toast capture exclusion failed: {ex.Message}");
        }
    }

    private void Position()
    {
        if (_window is null)
        {
            return;
        }

        var monitor = MonitorEnumerator.GetFocusedMonitor();
        if (monitor is null)
        {
            _window.Left = SystemParameters.WorkArea.Right - CardWidth - EdgeMargin;
            _window.Top = SystemParameters.WorkArea.Top + EdgeMargin;
            return;
        }

        var (scaleX, scaleY) = MonitorGeometry.GetScale(monitor);
        if (scaleX <= 0 || scaleY <= 0)
        {
            scaleX = 1;
            scaleY = 1;
        }

        double right = monitor.PhysicalBounds.Right / scaleX;
        double top = monitor.PhysicalBounds.Y / scaleY;
        _window.Left = right - CardWidth - EdgeMargin;
        _window.Top = top + EdgeMargin;
    }

    private static void AnimateIn(UIElement card)
    {
        if (card.RenderTransform is not TranslateTransform slide)
        {
            return;
        }

        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(18, 0, duration) { EasingFunction = ease });

        card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
    }

    private void ExpireOldCards()
    {
        var cutoff = DateTime.UtcNow.AddMilliseconds(-LifetimeMilliseconds);
        for (int i = _cards.Count - 1; i >= 0; i--)
        {
            if (_cards[i].ShownAt <= cutoff)
            {
                Dismiss(_cards[i], immediate: false);
            }
        }

        if (_cards.Count == 0)
        {
            _sweep.Stop();
        }
    }

    private void Dismiss(ToastCard? card, bool immediate)
    {
        if (card is null || !_cards.Remove(card))
        {
            return;
        }

        if (immediate || SystemParameters.ClientAreaAnimation == false)
        {
            Remove(card);
            return;
        }

        var duration = TimeSpan.FromMilliseconds(180);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        if (card.Root.RenderTransform is TranslateTransform slide)
        {
            slide.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(slide.X, 18, duration) { EasingFunction = ease });
        }

        var fade = new DoubleAnimation(1, 0, duration) { EasingFunction = ease };
        fade.Completed += (_, _) => Remove(card);
        card.Root.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void Remove(ToastCard card)
    {
        _stack.Children.Remove(card.Root);
        if (_cards.Count == 0)
        {
            _sweep.Stop();
            _window?.Hide();
        }
    }

    private static bool LooksLikeFailure(string title) =>
        title.Contains("fail", StringComparison.OrdinalIgnoreCase)
        || title.Contains("error", StringComparison.OrdinalIgnoreCase)
        || title.Contains("conflict", StringComparison.OrdinalIgnoreCase);

    private static ImageSource? LoadThumbnail(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.DecodePixelWidth = 84;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            TraceLog.Write($"toast thumbnail failed: {ex.Message}");
            return null;
        }
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    private static int GetWindowLong(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr(hWnd, nIndex).ToInt32() : GetWindowLong32(hWnd, nIndex);

    private static void SetWindowLong(IntPtr hWnd, int nIndex, int value)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtr(hWnd, nIndex, new IntPtr(value));
        }
        else
        {
            SetWindowLong32(hWnd, nIndex, value);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sweep.Stop();
        _cards.Clear();
        _window?.Close();
        _window = null;
    }

    private sealed record ToastCard(UIElement Root, DateTime ShownAt);
}
