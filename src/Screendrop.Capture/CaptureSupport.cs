using Windows.Foundation.Metadata;

namespace Screendrop.Capture;

public static class CaptureSupport
{
    public static bool IsGraphicsCaptureAvailable =>
        ApiInformation.IsMethodPresent(
            "Windows.Graphics.Capture.GraphicsCaptureItem",
            "TryCreateFromWindow");
}
