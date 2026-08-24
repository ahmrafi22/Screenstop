using Screendrop.Core.Geometry;

namespace Screendrop.Capture;

public sealed record MonitorInfo(
    IntPtr Handle,
    string DeviceName,
    PixelRect PhysicalBounds,
    bool IsPrimary);
