namespace Screendrop.Core.Annotations;

/// <summary>
/// Annotation tools. Mirrors the mac AnnotationTool set scoped to the
/// Windows parity matrix. Values are serialized into sidecar files, so new
/// tools may ONLY be appended (never reordered or inserted) to keep older
/// documents loading correctly.
/// </summary>
public enum AnnotationTool
{
    Select = 0,
    Rectangle = 1,
    Ellipse = 2,
    Arrow = 3,
    Freehand = 4,
    Text = 5,
    NumberedCircle = 6,
    Pixelate = 7,
    Blur = 8,

    // Appended after v1: keep numeric order stable for saved documents.
    Line = 9,
    Highlight = 10,
    FilledRectangle = 11,
}

public static class AnnotationToolInfo
{
    public static string Title(this AnnotationTool tool) => tool switch
    {
        AnnotationTool.Select => "Select",
        AnnotationTool.Rectangle => "Rectangle",
        AnnotationTool.Ellipse => "Circle",
        AnnotationTool.Arrow => "Arrow",
        AnnotationTool.Freehand => "Freehand",
        AnnotationTool.Text => "Text",
        AnnotationTool.NumberedCircle => "Numbered circle",
        AnnotationTool.Pixelate => "Pixelate",
        AnnotationTool.Blur => "Blur",
        AnnotationTool.Line => "Line",
        AnnotationTool.Highlight => "Highlight",
        AnnotationTool.FilledRectangle => "Solid rectangle",
        _ => tool.ToString(),
    };

    public static bool CreatesAnnotation(this AnnotationTool tool) => tool != AnnotationTool.Select;

    public static bool IsRedactionTool(this AnnotationTool tool) =>
        tool is AnnotationTool.Pixelate or AnnotationTool.Blur;

    /// Tools whose geometry lives in Start/End rather than Rect.
    public static bool UsesEndPoints(this AnnotationTool tool) =>
        tool is AnnotationTool.Arrow or AnnotationTool.Line;

    public static bool SupportsColor(this AnnotationTool tool) => tool switch
    {
        AnnotationTool.Rectangle or AnnotationTool.FilledRectangle or AnnotationTool.Ellipse
            or AnnotationTool.Line or AnnotationTool.Arrow or AnnotationTool.Freehand
            or AnnotationTool.Text or AnnotationTool.NumberedCircle or AnnotationTool.Highlight => true,
        _ => false,
    };
}
