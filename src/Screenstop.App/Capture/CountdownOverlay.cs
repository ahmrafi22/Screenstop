using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Screenstop.Capture;

namespace Screenstop.App.Capture;

/// Fullscreen self-timer countdown (mac CaptureCountdownPresenter parity).
/// A click-through transparent overlay shows a large number that ticks down
/// each second; the capture fires when it reaches zero.
internal sealed class CountdownOverlay : Window
{
    private readonly TextBlock _number;

    private CountdownOverlay(MonitorInfo monitor, double scaleX, double scaleY)
    {
        var bounds = monitor.PhysicalBounds;
        double widthDip = bounds.Width / scaleX;
        double heightDip = bounds.Height / scaleY;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        Title = "ScreenstopCountdown";
        Left = bounds.X / scaleX;
        Top = bounds.Y / scaleY;
        Width = widthDip;
        Height = heightDip;

        _number = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 160,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _number.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 30,
            ShadowDepth = 0,
            Opacity = 0.6,
        };

        Content = new Grid { Children = { _number } };
    }

    /// Runs a countdown of <paramref name="seconds"/> on the focused monitor.
    /// Returns a task that completes when the countdown finishes. The caller
    /// should perform the capture after awaiting it.
    public static Task RunAsync(int seconds)
    {
        if (seconds <= 0)
        {
            return Task.CompletedTask;
        }

        var monitor = MonitorEnumerator.GetFocusedMonitor();
        if (monitor is null)
        {
            return Task.CompletedTask;
        }

        var (scaleX, scaleY) = MonitorGeometry.GetScale(monitor);
        var overlay = new CountdownOverlay(monitor, scaleX, scaleY);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        overlay.Show();
        Tick(overlay, seconds, completion);
        return completion.Task;
    }

    private static async void Tick(CountdownOverlay overlay, int remaining, TaskCompletionSource completion)
    {
        try
        {
            while (remaining > 0)
            {
                overlay._number.Text = remaining.ToString();
                overlay.AnimatePulse();
                await Task.Delay(1000);
                remaining--;
            }
        }
        finally
        {
            overlay.Close();
            completion.TrySetResult();
        }
    }

    private void AnimatePulse()
    {
        var scale = new ScaleTransform(1.25, 1.25);
        _number.RenderTransform = scale;
        _number.RenderTransformOrigin = new Point(0.5, 0.5);

        var animation = new System.Windows.Media.Animation.DoubleAnimation(1.0, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }
}
