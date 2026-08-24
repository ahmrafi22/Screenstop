using Screenstop.Core.Geometry;

namespace Screenstop.Capture;

public sealed record MonitorInfo(
    IntPtr Handle,
    string DeviceName,
    PixelRect PhysicalBounds,
    bool IsPrimary);
