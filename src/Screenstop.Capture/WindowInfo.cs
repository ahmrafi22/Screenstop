using Screenstop.Core.Geometry;

namespace Screenstop.Capture;

public sealed record WindowInfo(
    IntPtr Handle,
    string Title,
    PixelRect Bounds);
