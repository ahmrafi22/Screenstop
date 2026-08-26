namespace Screendrop.Core.Annotations;

/// A single annotation in normalized image space.
///
/// Geometry fields are tool-specific:
/// - Rectangle / Ellipse / Pixelate / Blur / NumberedCircle / Text: Rect
/// - Arrow: Start + End
/// - Freehand: Points
///
/// StrokeWidth and FontSize are normalized against the image's largest
/// dimension so annotations keep their visual weight at any resolution.
public sealed class Annotation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public AnnotationTool Tool { get; set; } = AnnotationTool.Rectangle;

    public AnnotationColor Color { get; set; } = AnnotationColor.Red;

    /// Stroke width normalized to the image's largest dimension (0..1).
    public double StrokeWidth { get; set; } = 0.006;

    public NormalizedRect Rect { get; set; }

    public NormalizedPoint Start { get; set; }

    public NormalizedPoint End { get; set; }

    public List<NormalizedPoint> Points { get; set; } = new();

    public string Text { get; set; } = string.Empty;

    /// Font size normalized to the image's largest dimension (0..1).
    public double FontSize { get; set; } = 0.03;

    /// 1-based marker index for NumberedCircle, assigned by the editor.
    public int Number { get; set; }

    public Annotation Clone() => new()
    {
        Id = Id,
        Tool = Tool,
        Color = Color,
        StrokeWidth = StrokeWidth,
        Rect = Rect,
        Start = Start,
        End = End,
        Points = new List<NormalizedPoint>(Points),
        Text = Text,
        FontSize = FontSize,
        Number = Number,
    };

    /// The annotation's bounding box in normalized space.
    public NormalizedRect Bounds()
    {
        switch (Tool)
        {
            case AnnotationTool.Arrow:
                return NormalizedRect.FromPoints(Start.X, Start.Y, End.X, End.Y);

            case AnnotationTool.Freehand:
                if (Points.Count == 0)
                {
                    return default;
                }

                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                foreach (var p in Points)
                {
                    minX = Math.Min(minX, p.X);
                    minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X);
                    maxY = Math.Max(maxY, p.Y);
                }

                return NormalizedRect.FromPoints(minX, minY, maxX, maxY);

            default:
                return Rect;
        }
    }

    /// Hit-test in normalized space. Tolerance is normalized against the
    /// image's largest dimension.
    public bool HitTest(double nx, double ny, double tolerance)
    {
        switch (Tool)
        {
            case AnnotationTool.Arrow:
                return DistanceToSegment(nx, ny, Start, End) <= Math.Max(tolerance, StrokeWidth);

            case AnnotationTool.Freehand:
                double limit = Math.Max(tolerance, StrokeWidth);
                for (int i = 1; i < Points.Count; i++)
                {
                    if (DistanceToSegment(nx, ny, Points[i - 1], Points[i]) <= limit)
                    {
                        return true;
                    }
                }

                return Points.Count == 1
                    && Math.Abs(Points[0].X - nx) <= limit
                    && Math.Abs(Points[0].Y - ny) <= limit;

            case AnnotationTool.Text:
                return Rect.Inflate(tolerance).Contains(nx, ny);

            default:
                return Rect.Inflate(tolerance).Contains(nx, ny);
        }
    }

    public void Translate(double dx, double dy)
    {
        Rect = Rect.Translate(dx, dy);
        Start = Start.Translate(dx, dy);
        End = End.Translate(dx, dy);
        for (int i = 0; i < Points.Count; i++)
        {
            Points[i] = Points[i].Translate(dx, dy);
        }
    }

    private static double DistanceToSegment(double px, double py, NormalizedPoint a, NormalizedPoint b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared <= double.Epsilon)
        {
            return Math.Sqrt(((px - a.X) * (px - a.X)) + ((py - a.Y) * (py - a.Y)));
        }

        double t = Math.Clamp((((px - a.X) * dx) + ((py - a.Y) * dy)) / lengthSquared, 0, 1);
        double cx = a.X + (t * dx);
        double cy = a.Y + (t * dy);
        return Math.Sqrt(((px - cx) * (px - cx)) + ((py - cy) * (py - cy)));
    }
}
