using System.Windows;
using System.Windows.Input;
using Screenstop.Core.Annotations;
using Screenstop.Rendering;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace Screenstop.App.Editor;

/// Interactive annotation surface. Renders the capture at display resolution
/// and draws annotations through the shared AnnotationRenderer, so what the
/// user sees is exactly what the full-res export produces (normalized
/// coordinates are converted in one place only).
internal sealed class AnnotationCanvas : SKElement
{
    private const double DefaultMarkerDiameter = 0.05; // relative to max image dim
    private const double MinDragPx = 3;
    private const double FreehandMinStepPx = 2;
    private const double HandleSizePx = 8;

    private enum DragMode { None, Drawing, Freehand, Moving, Resizing, ArrowStart, ArrowEnd }

    private SKBitmap? _fullBitmap;
    private SKImage? _previewImage;
    private int _previewWidth;
    private int _previewHeight;

    // Fit mapping: canvas px = offset + normalized * displaySize.
    private double _offsetX;
    private double _offsetY;
    private double _dispW;
    private double _dispH;
    private double _ratioX = 1;
    private double _ratioY = 1;

    private DragMode _mode;
    private Annotation? _draft;
    private NormalizedPoint _dragStart;
    private NormalizedPoint _lastNorm;
    private NormalizedRect _resizeOrigin;

    public AnnotationEditorModel Model { get; } = new();

    public AnnotationTool ActiveTool { get; private set; } = AnnotationTool.Rectangle;

    public AnnotationColor ActiveColor { get; set; } = AnnotationColor.Red;

    public double ActiveStroke { get; set; } = 0.006;

    public double ActiveFontSize { get; set; } = 0.03;

    public bool HasImage => _fullBitmap is not null;

    public SKBitmap? FullBitmap => _fullBitmap;

    /// Raised when the text tool completes a click: the window should open an
    /// inline text box at the canvas point (DIPs) for the normalized anchor.
    public event Action<Point, NormalizedPoint>? TextSessionRequested;

    public AnnotationCanvas()
    {
        Focusable = true;
        Model.Changed += InvalidateVisual;
    }

    public bool LoadImage(string path)
    {
        try
        {
            var bitmap = SKBitmap.Decode(path);
            if (bitmap is null || bitmap.Width == 0 || bitmap.Height == 0)
            {
                bitmap?.Dispose();
                return false;
            }

            _fullBitmap?.Dispose();
            _fullBitmap = bitmap;
            _previewImage?.Dispose();
            _previewImage = null;
            InvalidateVisual();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void SetTool(AnnotationTool tool)
    {
        ActiveTool = tool;
        _draft = null;
        _mode = DragMode.None;
        if (tool != AnnotationTool.Select)
        {
            Model.Select(null);
        }

        Cursor = tool == AnnotationTool.Select ? Cursors.Arrow : Cursors.Cross;
        InvalidateVisual();
    }

    /// Commits a text annotation anchored at the given normalized point.
    public void AddText(NormalizedPoint anchor, string text)
    {
        if (_fullBitmap is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var annotation = new Annotation
        {
            Tool = AnnotationTool.Text,
            Color = ActiveColor,
            FontSize = ActiveFontSize,
            Text = text.Replace("\r", string.Empty),
        };
        annotation.Rect = MeasureTextRect(annotation);
        annotation.Rect = annotation.Rect.Translate(anchor.X, anchor.Y).ClampToUnit();

        Model.Add(annotation);
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(17, 24, 39));

        if (_fullBitmap is null)
        {
            return;
        }

        int canvasW = e.Info.Width;
        int canvasH = e.Info.Height;
        if (canvasW < 4 || canvasH < 4)
        {
            return;
        }

        UpdateDpiRatio(canvasW, canvasH);
        UpdateFit(canvasW, canvasH);
        EnsurePreviewImage();
        if (_previewImage is null)
        {
            return;
        }

        canvas.Translate((float)_offsetX, (float)_offsetY);
        canvas.DrawImage(_previewImage, 0, 0);

        var annotations = new List<Annotation>(Model.Annotations);
        if (_draft is not null)
        {
            annotations.Add(_draft);
        }

        AnnotationRenderer.Draw(canvas, _previewImage, annotations, _previewWidth, _previewHeight);
        DrawSelection(canvas);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        CaptureMouse();

        var norm = ToNorm(ToCanvasPx(e));
        _dragStart = norm;
        _lastNorm = norm;

        if (ActiveTool == AnnotationTool.Select)
        {
            BeginSelectInteraction(norm);
            return;
        }

        if (ActiveTool == AnnotationTool.Text)
        {
            // The text box opens on mouse-up so a click-drag can position it.
            _mode = DragMode.Drawing;
            return;
        }

        if (ActiveTool == AnnotationTool.Freehand)
        {
            _draft = new Annotation
            {
                Tool = AnnotationTool.Freehand,
                Color = ActiveColor,
                StrokeWidth = ActiveStroke,
            };
            _draft.Points.Add(norm);
            _mode = DragMode.Freehand;
            InvalidateVisual();
            return;
        }

        _draft = new Annotation
        {
            Tool = ActiveTool,
            Color = ActiveColor,
            StrokeWidth = ActiveStroke,
            FontSize = ActiveFontSize,
        };
        if (ActiveTool == AnnotationTool.Arrow)
        {
            _draft.Start = norm;
            _draft.End = norm;
        }
        else
        {
            _draft.Rect = new NormalizedRect(norm.X, norm.Y, 0, 0);
        }

        _mode = DragMode.Drawing;
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_mode == DragMode.None)
        {
            return;
        }

        var norm = ToNorm(ToCanvasPx(e));

        switch (_mode)
        {
            case DragMode.Drawing when _draft is not null:
                if (_draft.Tool == AnnotationTool.Arrow)
                {
                    _draft.End = norm;
                }
                else
                {
                    _draft.Rect = NormalizedRect.FromPoints(_dragStart.X, _dragStart.Y, norm.X, norm.Y);
                }

                break;

            case DragMode.Freehand when _draft is not null:
                var lastCanvas = ToCanvasPx(_draft.Points[^1]);
                var curCanvas = ToCanvasPx(norm);
                if (Distance(lastCanvas, curCanvas) >= FreehandMinStepPx)
                {
                    _draft.Points.Add(norm);
                }

                break;

            case DragMode.Moving when Model.Selected is not null:
                MoveClamped(Model.Selected, norm.X - _lastNorm.X, norm.Y - _lastNorm.Y);
                break;

            case DragMode.Resizing when Model.Selected is not null:
                Model.Selected.Rect = NormalizedRect
                    .FromPoints(_resizeOrigin.X, _resizeOrigin.Y, norm.X, norm.Y)
                    .ClampToUnit();
                break;

            case DragMode.ArrowStart when Model.Selected is not null:
                Model.Selected.Start = norm;
                break;

            case DragMode.ArrowEnd when Model.Selected is not null:
                Model.Selected.End = norm;
                break;
        }

        _lastNorm = norm;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        var norm = ToNorm(ToCanvasPx(e));

        // The text tool has no draft shape: a click opens the inline text box.
        if (ActiveTool == AnnotationTool.Text && _mode == DragMode.Drawing)
        {
            _mode = DragMode.None;
            TextSessionRequested?.Invoke(e.GetPosition(this), norm);
            return;
        }

        switch (_mode)
        {
            case DragMode.Drawing when _draft is not null:
                CommitDrawing(norm);
                break;

            case DragMode.Freehand when _draft is not null:
                if (_draft.Points.Count >= 2)
                {
                    Model.Add(_draft);
                }

                _draft = null;
                break;

            case DragMode.Moving:
            case DragMode.Resizing:
            case DragMode.ArrowStart:
            case DragMode.ArrowEnd:
                Model.EndBatch();
                break;
        }

        _mode = DragMode.None;
        InvalidateVisual();
    }

    private void BeginSelectInteraction(NormalizedPoint norm)
    {
        var selected = Model.Selected;
        double tolerance = PxToNorm(HandleSizePx * 1.5);

        if (selected is not null)
        {
            if (selected.Tool == AnnotationTool.Arrow)
            {
                if (NearPoint(norm, selected.Start, tolerance))
                {
                    Model.BeginBatch();
                    _mode = DragMode.ArrowStart;
                    return;
                }

                if (NearPoint(norm, selected.End, tolerance))
                {
                    Model.BeginBatch();
                    _mode = DragMode.ArrowEnd;
                    return;
                }
            }
            else if (NearResizeHandle(norm, selected, tolerance))
            {
                Model.BeginBatch();
                _resizeOrigin = selected.Rect;
                _mode = DragMode.Resizing;
                return;
            }
        }

        var hit = Model.HitTest(norm.X, norm.Y, PxToNorm(6));
        Model.Select(hit);
        if (hit is not null)
        {
            Model.BeginBatch();
            _mode = DragMode.Moving;
        }
    }

    private void CommitDrawing(NormalizedPoint norm)
    {
        if (_draft is null)
        {
            return;
        }

        double minNorm = PxToNorm(MinDragPx);
        bool isClick = Math.Abs(norm.X - _dragStart.X) < minNorm && Math.Abs(norm.Y - _dragStart.Y) < minNorm;

        if (_draft.Tool == AnnotationTool.NumberedCircle && isClick)
        {
            // A plain click places a default-sized marker centered on it.
            _draft.Rect = DefaultMarkerRect(_dragStart);
            isClick = false;
        }

        if (!isClick)
        {
            if (_draft.Tool is AnnotationTool.Rectangle or AnnotationTool.Ellipse
                or AnnotationTool.Pixelate or AnnotationTool.Blur or AnnotationTool.NumberedCircle)
            {
                _draft.Rect = _draft.Rect.ClampToUnit();
            }

            if (_draft.Tool == AnnotationTool.NumberedCircle)
            {
                Model.Add(_draft);
                Model.RenumberMarkers();
            }
            else
            {
                Model.Add(_draft);
            }
        }

        _draft = null;
    }

    private NormalizedRect DefaultMarkerRect(NormalizedPoint center)
    {
        if (_fullBitmap is null)
        {
            return new NormalizedRect(center.X, center.Y, 0, 0);
        }

        double maxDim = Math.Max(_fullBitmap.Width, _fullBitmap.Height);
        double diameterPx = DefaultMarkerDiameter * maxDim;
        double w = diameterPx / _fullBitmap.Width;
        double h = diameterPx / _fullBitmap.Height;
        return new NormalizedRect(center.X - (w / 2), center.Y - (h / 2), w, h).ClampToUnit();
    }

    /// Estimates the text annotation's bounds in normalized space by measuring
    /// glyphs at full-res scale (kept in sync with the renderer's text sizing).
    private NormalizedRect MeasureTextRect(Annotation annotation)
    {
        if (_fullBitmap is null)
        {
            return new NormalizedRect(0, 0, 0.1, 0.05);
        }

        double maxDim = Math.Max(_fullBitmap.Width, _fullBitmap.Height);
        float fontSize = (float)Math.Max(8, annotation.FontSize * maxDim);

        using var paint = new SKPaint { TextSize = fontSize, Typeface = SKTypeface.Default };
        string[] lines = annotation.Text.Split('\n');
        float widest = 0;
        foreach (var line in lines)
        {
            widest = Math.Max(widest, paint.MeasureText(line));
        }

        double heightPx = lines.Length * fontSize * 1.25;
        return new NormalizedRect(
            0,
            0,
            widest / _fullBitmap.Width,
            heightPx / _fullBitmap.Height);
    }

    private static bool NearPoint(NormalizedPoint a, NormalizedPoint b, double tolerance) =>
        Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance;

    private bool NearResizeHandle(NormalizedPoint norm, Annotation annotation, double tolerance)
    {
        var handle = new NormalizedPoint(annotation.Rect.Right, annotation.Rect.Bottom);
        return NearPoint(norm, handle, tolerance);
    }

    private static void MoveClamped(Annotation annotation, double dx, double dy)
    {
        if (dx == 0 && dy == 0)
        {
            return;
        }

        annotation.Translate(dx, dy);
        var bounds = annotation.Bounds();
        double fixX = 0, fixY = 0;
        if (bounds.X < 0)
        {
            fixX = -bounds.X;
        }
        else if (bounds.Right > 1)
        {
            fixX = 1 - bounds.Right;
        }

        if (bounds.Y < 0)
        {
            fixY = -bounds.Y;
        }
        else if (bounds.Bottom > 1)
        {
            fixY = 1 - bounds.Bottom;
        }

        if (fixX != 0 || fixY != 0)
        {
            annotation.Translate(fixX, fixY);
        }
    }

    private void DrawSelection(SKCanvas canvas)
    {
        var selected = Model.Selected;
        if (selected is null || _dispW <= 0 || _dispH <= 0)
        {
            return;
        }

        var bounds = selected.Bounds();
        var rect = new SKRect(
            (float)(bounds.X * _dispW),
            (float)(bounds.Y * _dispH),
            (float)(bounds.Right * _dispW),
            (float)(bounds.Bottom * _dispH));

        using var dashPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(59, 130, 246),
            StrokeWidth = 1.5f,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new[] { 6f, 4f }, 0),
        };
        canvas.DrawRect(rect, dashPaint);

        using var handlePaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.White,
            IsAntialias = true,
        };
        using var handleBorder = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(59, 130, 246),
            StrokeWidth = 1.5f,
            IsAntialias = true,
        };

        if (selected.Tool == AnnotationTool.Arrow)
        {
            foreach (var p in new[] { selected.Start, selected.End })
            {
                canvas.DrawCircle((float)(p.X * _dispW), (float)(p.Y * _dispH), (float)HandleSizePx / 2, handlePaint);
                canvas.DrawCircle((float)(p.X * _dispW), (float)(p.Y * _dispH), (float)HandleSizePx / 2, handleBorder);
            }
        }
        else
        {
            var handle = SKRect.Create(rect.Right - (float)HandleSizePx / 2, rect.Bottom - (float)HandleSizePx / 2, (float)HandleSizePx, (float)HandleSizePx);
            canvas.DrawRect(handle, handlePaint);
            canvas.DrawRect(handle, handleBorder);
        }
    }

    private void UpdateDpiRatio(int canvasW, int canvasH)
    {
        if (ActualWidth > 0 && ActualHeight > 0)
        {
            _ratioX = canvasW / ActualWidth;
            _ratioY = canvasH / ActualHeight;
        }
    }

    private void UpdateFit(int canvasW, int canvasH)
    {
        if (_fullBitmap is null)
        {
            return;
        }

        double fit = Math.Min(canvasW / (double)_fullBitmap.Width, canvasH / (double)_fullBitmap.Height);
        _dispW = _fullBitmap.Width * fit;
        _dispH = _fullBitmap.Height * fit;
        _offsetX = (canvasW - _dispW) / 2;
        _offsetY = (canvasH - _dispH) / 2;
    }

    /// Builds (or rebuilds) the display-resolution image the annotations are
    /// composited against. Pixelate/blur sample from this image, so it must
    /// match the coordinate space passed to AnnotationRenderer.Draw.
    private void EnsurePreviewImage()
    {
        if (_fullBitmap is null || _dispW < 1 || _dispH < 1)
        {
            return;
        }

        int targetW = Math.Max(1, (int)Math.Round(_dispW));
        int targetH = Math.Max(1, (int)Math.Round(_dispH));
        if (_previewImage is not null && _previewWidth == targetW && _previewHeight == targetH)
        {
            return;
        }

        // Never upscale: if the canvas is larger than the source, draw 1:1.
        if (targetW >= _fullBitmap.Width && targetH >= _fullBitmap.Height)
        {
            _previewImage?.Dispose();
            _previewImage = SKImage.FromBitmap(_fullBitmap);
            _previewWidth = _fullBitmap.Width;
            _previewHeight = _fullBitmap.Height;
            return;
        }

        var info = new SKImageInfo(targetW, targetH, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var resized = _fullBitmap.Resize(info, SKFilterQuality.High);
        if (resized is null)
        {
            return;
        }

        _previewImage?.Dispose();
        _previewImage = SKImage.FromBitmap(resized);
        resized.Dispose();
        _previewWidth = targetW;
        _previewHeight = targetH;
    }

    private Point ToCanvasPx(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        return new Point(p.X * _ratioX, p.Y * _ratioY);
    }

    private Point ToCanvasPx(NormalizedPoint n) => new(_offsetX + (n.X * _dispW), _offsetY + (n.Y * _dispH));

    private NormalizedPoint ToNorm(Point canvasPx)
    {
        if (_dispW <= 0 || _dispH <= 0)
        {
            return new NormalizedPoint(0, 0);
        }

        return new NormalizedPoint(
            Math.Clamp((canvasPx.X - _offsetX) / _dispW, 0, 1),
            Math.Clamp((canvasPx.Y - _offsetY) / _dispH, 0, 1));
    }

    /// Converts a canvas-pixel distance to normalized units (for tolerances).
    private double PxToNorm(double px)
    {
        if (_dispW <= 0 || _fullBitmap is null)
        {
            return 0.01;
        }

        double maxDim = Math.Max(_fullBitmap.Width, _fullBitmap.Height);
        return (px * _ratioX) / (_dispW * (_fullBitmap.Width / maxDim));
    }

    public Point NormalizedToCanvasDip(NormalizedPoint n)
    {
        var px = ToCanvasPx(n);
        return new Point(px.X / _ratioX, px.Y / _ratioY);
    }

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// Releases the bitmaps backing the canvas. Called by the editor window
    /// on close (SKElement.Dispose is not virtual in SkiaSharp.Views.WPF).
    public void ReleaseResources()
    {
        Model.Changed -= InvalidateVisual;
        _previewImage?.Dispose();
        _previewImage = null;
        _fullBitmap?.Dispose();
        _fullBitmap = null;
    }
}
