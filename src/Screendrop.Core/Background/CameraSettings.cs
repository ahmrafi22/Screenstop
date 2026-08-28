namespace Screendrop.Core.Background;

/// 3D camera transform for the screenshot card (mac `AnnotationCameraSettings`
/// parity). Translations are fractions of the final canvas; angles in degrees.
public sealed class CameraSettings : IEquatable<CameraSettings>
{
    public double PanX { get; set; }

    public double PanY { get; set; }

    public double TiltXDegrees { get; set; }

    public double TiltYDegrees { get; set; }

    public double RotationXDegrees { get; set; }

    public double RotationYDegrees { get; set; }

    public double RollDegrees { get; set; }

    public double FieldOfViewDegrees { get; set; } = 24;

    public double Zoom { get; set; } = 1;

    /// Version 1 used shear-based tilt and hidden angle-dependent auto-fit.
    /// Version 2 uses center-origin camera orbit and explicit-only zoom.
    public int ProjectionVersion { get; set; } = 2;

    public bool IsDefault
    {
        get
        {
            double defaultFieldOfView = ProjectionVersion < 2 ? 45 : 24;
            return ApproximatelyZero(PanX)
                && ApproximatelyZero(PanY)
                && ApproximatelyZero(TiltXDegrees)
                && ApproximatelyZero(TiltYDegrees)
                && ApproximatelyZero(RotationXDegrees)
                && ApproximatelyZero(RotationYDegrees)
                && ApproximatelyZero(RollDegrees)
                && Math.Abs(FieldOfViewDegrees - defaultFieldOfView) <= 0.0001
                && Math.Abs(Zoom - 1) <= 0.0001;
        }
    }

    public bool HasEffect =>
        !ApproximatelyZero(PanX)
        || !ApproximatelyZero(PanY)
        || !ApproximatelyZero(TiltXDegrees)
        || !ApproximatelyZero(TiltYDegrees)
        || !ApproximatelyZero(RotationXDegrees)
        || !ApproximatelyZero(RotationYDegrees)
        || !ApproximatelyZero(RollDegrees)
        || Math.Abs(Zoom - 1) > 0.0001;

    public void UpgradeProjectionIfNeeded()
    {
        if (ProjectionVersion >= 2)
        {
            return;
        }

        ProjectionVersion = 2;
        if (Math.Abs(FieldOfViewDegrees - 45) <= 0.0001)
        {
            FieldOfViewDegrees = 24;
        }
    }

    private static bool ApproximatelyZero(double value) => Math.Abs(value) <= 0.0001;

    public bool Equals(CameraSettings? other) =>
        other is not null
        && PanX == other.PanX
        && PanY == other.PanY
        && TiltXDegrees == other.TiltXDegrees
        && TiltYDegrees == other.TiltYDegrees
        && RotationXDegrees == other.RotationXDegrees
        && RotationYDegrees == other.RotationYDegrees
        && RollDegrees == other.RollDegrees
        && FieldOfViewDegrees == other.FieldOfViewDegrees
        && Zoom == other.Zoom
        && ProjectionVersion == other.ProjectionVersion;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PanX);
        hash.Add(PanY);
        hash.Add(TiltXDegrees);
        hash.Add(TiltYDegrees);
        hash.Add(RotationXDegrees);
        hash.Add(RotationYDegrees);
        hash.Add(RollDegrees);
        hash.Add(FieldOfViewDegrees);
        hash.Add(Zoom);
        hash.Add(ProjectionVersion);
        return hash.ToHashCode();
    }

    public CameraSettings Clone() => new()
    {
        PanX = PanX,
        PanY = PanY,
        TiltXDegrees = TiltXDegrees,
        TiltYDegrees = TiltYDegrees,
        RotationXDegrees = RotationXDegrees,
        RotationYDegrees = RotationYDegrees,
        RollDegrees = RollDegrees,
        FieldOfViewDegrees = FieldOfViewDegrees,
        Zoom = Zoom,
        ProjectionVersion = ProjectionVersion,
    };
}
