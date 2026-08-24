using Windows.Foundation.Metadata;

namespace Screenstop.Capture;

public static class CaptureSupport
{
    public static bool IsGraphicsCaptureAvailable =>
        ApiInformation.IsMethodPresent(
            "Windows.Graphics.Capture.GraphicsCaptureItem",
            "TryCreateFromWindow");
}
