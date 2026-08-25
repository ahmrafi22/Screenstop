using Screenstop.Capture;
using Screenstop.Core.Geometry;

namespace Screenstop.App.WindowPicker;

internal sealed class PickerLayout
{
    private readonly List<MonitorPlacement> _placements;

    public double Left { get; }

    public double Top { get; }

    public double Width { get; }

    public double Height { get; }

    private PickerLayout(List<MonitorPlacement> placements, double left, double top, double width, double height)
    {
        _placements = placements;
        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    public static PickerLayout Build(IReadOnlyList<MonitorInfo> monitors)
    {
        var placements = new List<MonitorPlacement>();
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var monitor in monitors)
        {
            var (scaleX, scaleY) = MonitorGeometry.GetScale(monitor);
            var bounds = monitor.PhysicalBounds;
            double dipX = bounds.X / scaleX;
            double dipY = bounds.Y / scaleY;
            double dipW = bounds.Width / scaleX;
            double dipH = bounds.Height / scaleY;

            placements.Add(new MonitorPlacement(monitor, scaleX, scaleY, dipX, dipY, dipW, dipH));

            minX = Math.Min(minX, dipX);
            minY = Math.Min(minY, dipY);
            maxX = Math.Max(maxX, dipX + dipW);
            maxY = Math.Max(maxY, dipY + dipH);
        }

        return new PickerLayout(placements, minX, minY, maxX - minX, maxY - minY);
    }

    public PixelRect ToPhysical(double dipX, double dipY)
    {
        var placement = FindPlacement(dipX, dipY);
        int px = (int)Math.Round((dipX - placement.DipX) * placement.ScaleX) + placement.Monitor.PhysicalBounds.X;
        int py = (int)Math.Round((dipY - placement.DipY) * placement.ScaleY) + placement.Monitor.PhysicalBounds.Y;
        return new PixelRect(px, py, 0, 0);
    }

    public (double X, double Y, double Width, double Height) ToDip(PixelRect physical)
    {
        var placement = FindPlacementForRect(physical);
        double dipX = ((physical.X - placement.Monitor.PhysicalBounds.X) / placement.ScaleX) + placement.DipX;
        double dipY = ((physical.Y - placement.Monitor.PhysicalBounds.Y) / placement.ScaleY) + placement.DipY;
        double dipW = physical.Width / placement.ScaleX;
        double dipH = physical.Height / placement.ScaleY;
        return (dipX - Left, dipY - Top, dipW, dipH);
    }

    private MonitorPlacement FindPlacement(double dipX, double dipY)
    {
        foreach (var placement in _placements)
        {
            if (dipX >= placement.DipX && dipX < placement.DipX + placement.DipW
                && dipY >= placement.DipY && dipY < placement.DipY + placement.DipH)
            {
                return placement;
            }
        }

        return _placements[0];
    }

    private MonitorPlacement FindPlacementForRect(PixelRect physical)
    {
        foreach (var placement in _placements)
        {
            if (placement.Monitor.PhysicalBounds.Intersects(physical))
            {
                return placement;
            }
        }

        return _placements[0];
    }

    private sealed record MonitorPlacement(
        MonitorInfo Monitor,
        double ScaleX,
        double ScaleY,
        double DipX,
        double DipY,
        double DipW,
        double DipH);
}
