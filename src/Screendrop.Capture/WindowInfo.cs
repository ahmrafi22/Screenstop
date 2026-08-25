using Screendrop.Core.Geometry;

namespace Screendrop.Capture;

public sealed record WindowInfo(
    IntPtr Handle,
    string Title,
    PixelRect Bounds);
