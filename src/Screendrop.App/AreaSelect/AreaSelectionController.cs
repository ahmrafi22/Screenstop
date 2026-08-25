using Screendrop.Capture;
using Screendrop.Core.Geometry;

namespace Screendrop.App.AreaSelect;

internal static class AreaSelectionController
{
    public static PixelRect? Pick(MonitorInfo monitor)
    {
        var (scaleX, scaleY) = MonitorGeometry.GetScale(monitor);
        var overlay = new AreaSelectionOverlay(monitor, scaleX, scaleY);
        return overlay.RunModal();
    }
}
