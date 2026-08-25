using Screenstop.Capture;

namespace Screenstop.App.WindowPicker;

internal static class WindowPickerController
{
    public static WindowInfo? Pick(IReadOnlyList<MonitorInfo> monitors)
    {
        int processId = Environment.ProcessId;
        var candidates = WindowEnumerator.Enumerate(processId);
        var layout = PickerLayout.Build(monitors);
        var overlay = new WindowPickerOverlay(layout, candidates);
        return overlay.RunModal();
    }
}
