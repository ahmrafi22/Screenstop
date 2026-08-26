namespace Screendrop.Core.Annotations;

/// Annotation tools. Mirrors the mac AnnotationTool set scoped to the
/// Windows v1 parity matrix (select, rectangle, ellipse, arrow, freehand,
/// text, numbered circles, pixelate, blur).
public enum AnnotationTool
{
    Select,
    Rectangle,
    Ellipse,
    Arrow,
    Freehand,
    Text,
    NumberedCircle,
    Pixelate,
    Blur,
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
        _ => tool.ToString(),
    };

    public static bool CreatesAnnotation(this AnnotationTool tool) => tool != AnnotationTool.Select;

    public static bool IsRedactionTool(this AnnotationTool tool) =>
        tool is AnnotationTool.Pixelate or AnnotationTool.Blur;

    public static bool SupportsColor(this AnnotationTool tool) => tool switch
    {
        AnnotationTool.Rectangle or AnnotationTool.Ellipse or AnnotationTool.Arrow
            or AnnotationTool.Freehand or AnnotationTool.Text or AnnotationTool.NumberedCircle => true,
        _ => false,
    };
}
