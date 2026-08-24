using SkiaSharp;

namespace Screenstop.Capture;

public sealed record DisplayCapture(
    MonitorInfo Monitor,
    SKBitmap Bitmap,
    DateTimeOffset CapturedAt) : IDisposable
{
    public void Dispose()
    {
        Bitmap.Dispose();
    }
}
