using System.Windows;
using System.Windows.Input;
using Screenstop.Core.Annotations;
using Screenstop.Core.Geometry;
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

    private enum DragMode { None, Drawing, Freehand, Moving, Resizing, ArrowStart, ArrowEnd, CropDragging }

    private SKBitmap? _fullBitmap;
    private SKImage? _previewImage;
    private int _previewWidth;
    private int _previewHeight;

    // Zoom/pan mapping (pure math lives in Core's ZoomPanTransform):
    // canvas px = Offset + normalized * DisplaySize.
    private readonly ZoomPanTransform _viewport = new();
    private double _ratioX = 1;
    private double _ratioY = 1;

    private DragMode _mode;
    private Annotation? _draft;
    private NormalizedPoint _dragStart;
    private NormalizedPoint _lastNorm;
    private NormalizedRect _resizeOrigin;

    // Crop mode: drag selects the region to keep; the committed rect is in
    // original-image normalized space.
    public bool IsCropping { get; private set; }

    private NormalizedRect? _cropDraft;

    /// Raised when the user finishes dragging a valid crop region.
    public event Action<NormalizedRect>? CropCommitted;

    public void EnterCropMode()
    {
        IsCropping = true;
        _cropDraft = null;
        Model.Select(null);
        Cursor = Cursors.Cross;
        InvalidateVisual();
    }

    public void CancelCrop()
    {
        if (!IsCropping)
        {
            return;
        }

        IsCropping = false;
        _cropDraft = null;
        Cursor = ActiveTool == AnnotationTool.Select ? Cursors.Arrow : Cursors.Cross;
        InvalidateVisual();
    }

    /// Cuts the bitmap to the crop rect and remaps all annotations into the
    /// cropped space, so existing edits keep their position relative to the
    /// picture. Resets undo history (the pre-crop image no longer exists).
    public void ApplyCrop(NormalizedRect rect)
    {
        if (_fullBitmap is null)
        {
            return;
        }

        int oldWidth = _fullBitmap.Width;
        int oldHeight = _fullBitmap.Height;
        var document = Model.ToDocument();

        var cropped = global::Screenstop.Rendering.Crop.Apply(_fullBitmap, rect);
        _fullBitmap.Dispose();
        _fullBitmap = cropped;
        Model.Load(global::Screenstop.Rendering.Crop.TransformDocument(document, rect, oldWidth, oldHeight));
        _previewImage?.Dispose();
        _previewImage = null;
        _viewport.Reset();
        InvalidateVisual();
    }

    // Middle-button pan state (canvas pixels).
    private bool _panning;
    private Point _panLast;

    public AnnotationEditorModel Model { get; } = new();

    public AnnotationTool ActiveTool { get; private set; } = AnnotationTool.Rectangle;

    public AnnotationColor ActiveColor { get; set; } = AnnotationColor.Red;

    public double ActiveStroke { get; set; } = 0.006;

    public double ActiveFontSize { get; set; } = 0.03;

    /// Redaction strength applied to new pixelate/blur annotations (mac inspector default 23%).
    public double ActiveDensity { get; set; } = 0.23;

    /// Raised when the active tool changes (sidebar refresh hook).
    public event Action? ToolChanged;

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
            _viewport.Reset();
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
        ToolChanged?.Invoke();
        InvalidateVisual();
    }

    /// Applies an updated strength to the selected redaction annotation as
    /// one undo step. No-op unless a pixelate/blur annotation is selected.
    private bool _selectedStyleEditActive;

    public void BeginSelectedStyleEdit()
    {
        if (Model.Selected is null) return;
        Model.BeginBatch();
        _selectedStyleEditActive = true;
    }

    public void EndSelectedStyleEdit()
    {
        if (!_selectedStyleEditActive) return;
        _selectedStyleEditActive = false;
        Model.EndBatch();
    }

    public void SetSelectedDensity(double density, bool isCoalesced = false)
    {
        if (Model.Selected is not { } selected || !selected.Tool.IsRedactionTool())
        {
            return;
        }

        if (!isCoalesced) Model.BeginBatch();
        selected.Density = Math.Clamp(density, 0.02, 1);
        if (isCoalesced) Model.NotifyChanged(); else Model.EndBatch();
    }

    /// Applies a font size to the selected text annotation as one undo step.
    public void SetSelectedFontSize(double fontSize, bool isCoalesced = false)
    {
        if (Model.Selected is not { } selected || selected.Tool != AnnotationTool.Text)
        {
            return;
        }

        if (!isCoalesced) Model.BeginBatch();
        selected.FontSize = Math.Clamp(fontSize, 0.008, 0.12);
        if (isCoalesced) Model.NotifyChanged(); else Model.EndBatch();
    }

    public void SetSelectedStroke(double stroke, bool isCoalesced = false)
    {
        if (Model.Selected is not { } selected || !selected.Tool.SupportsColor() || selected.Tool.IsRedactionTool()) return;
        if (!isCoalesced) Model.BeginBatch();
        selected.StrokeWidth = Math.Clamp(stroke, 0.002, 0.02);
        if (isCoalesced) Model.NotifyChanged(); else Model.EndBatch();
    }

    public void SetSelectedColor(AnnotationColor color)
    {
        ActiveColor = color;
        if (Model.Selected is not { } selected || !selected.Tool.SupportsColor()) return;
        Model.BeginBatch();
        selected.Color = color;
        Model.EndBatch();
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
        // Neutral desaturated ground (mac AnnoTheme.neutralSolid dark: white*0.16).
        canvas.Clear(new SKColor(41, 41, 41));

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
        _viewport.CanvasWidth = canvasW;
        _viewport.CanvasHeight = canvasH;
        _viewport.ImageWidth = _fullBitmap.Width;
        _viewport.ImageHeight = _fullBitmap.Height;
        _viewport.Recompute();
        EnsurePreviewImage();
        if (_previewImage is null)
        {
            return;
        }

        canvas.Translate((float)_viewport.OffsetX, (float)_viewport.OffsetY);
        canvas.DrawImage(_previewImage, 0, 0);

        var annotations = new List<Annotation>(Model.Annotations);
        if (_draft is not null)
        {
            annotations.Add(_draft);
        }

        AnnotationRenderer.Draw(canvas, _previewImage, annotations, _previewWidth, _previewHeight);
        DrawCropOverlay(canvas);
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

        if (IsCropping)
        {
            _cropDraft = null;
            _mode = DragMode.CropDragging;
            InvalidateVisual();
            return;
        }

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
        if (ActiveTool.IsRedactionTool())
        {
            _draft.Density = ActiveDensity;
        }
        if (ActiveTool.UsesEndPoints())
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

        if (_panning)
        {
            var p = ToCanvasPx(e);
            _viewport.PanBy(p.X - _panLast.X, p.Y - _panLast.Y);
            _panLast = p;
            InvalidateVisual();
            return;
        }

        if (_mode == DragMode.None)
        {
            return;
        }

        var norm = ToNorm(ToCanvasPx(e));

        switch (_mode)
        {
            case DragMode.CropDragging:
                _cropDraft = CropTransform.Validate(NormalizedRect.FromPoints(_dragStart.X, _dragStart.Y, norm.X, norm.Y));
                break;

            case DragMode.Drawing when _draft is not null:
                if (_draft.Tool.UsesEndPoints())
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
            case DragMode.CropDragging:
                _mode = DragMode.None;
                IsCropping = false;
                Cursor = ActiveTool == AnnotationTool.Select ? Cursors.Arrow : Cursors.Cross;
                var finalRect = CropTransform.Validate(NormalizedRect.FromPoints(_dragStart.X, _dragStart.Y, norm.X, norm.Y));
                _cropDraft = null;
                if (finalRect is not null)
                {
                    CropCommitted?.Invoke(finalRect.Value);
                }

                InvalidateVisual();
                break;

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

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_fullBitmap is null)
        {
            return;
        }

        var p = ToCanvasPx(e);
        double factor = e.Delta > 0 ? 1.2 : 1 / 1.2;
        _viewport.ZoomAt(p.X, p.Y, _viewport.Zoom * factor);
        RaiseZoomChanged();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton != MouseButton.Middle || _fullBitmap is null)
        {
            return;
        }

        _panning = true;
        _panLast = ToCanvasPx(e);
        CaptureMouse();
        Cursor = Cursors.ScrollAll;
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton != MouseButton.Middle || !_panning)
        {
            return;
        }

        _panning = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        Cursor = ActiveTool == AnnotationTool.Select ? Cursors.Arrow : Cursors.Cross;
        e.Handled = true;
    }

    /// Current zoom level (1.0 = fit to window).
    public double ZoomLevel => _viewport.Zoom;

    /// Raised whenever the zoom level changes (wheel or toolbar).
    public event Action<double>? ZoomChanged;

    public void ZoomIn() => ZoomByFactor(1.25);

    public void ZoomOut() => ZoomByFactor(1 / 1.25);

    public void ResetZoom()
    {
        _viewport.Reset();
        RaiseZoomChanged();
        InvalidateVisual();
    }

    private void ZoomByFactor(double factor)
    {
        if (_fullBitmap is null)
        {
            return;
        }

        _viewport.SetZoom(_viewport.Zoom * factor);
        RaiseZoomChanged();
        InvalidateVisual();
    }

    private void RaiseZoomChanged()
    {
        ZoomChanged?.Invoke(_viewport.Zoom);
    }

    private void BeginSelectInteraction(NormalizedPoint norm)
    {
        var selected = Model.Selected;
        double tolerance = PxToNorm(HandleSizePx * 1.5);

        if (selected is not null)
        {
            if (selected.Tool.UsesEndPoints())
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
                or AnnotationTool.Pixelate or AnnotationTool.Blur or AnnotationTool.NumberedCircle
                or AnnotationTool.Highlight or AnnotationTool.FilledRectangle)
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

    private void DrawCropOverlay(SKCanvas canvas)
    {
        if (!IsCropping)
        {
            return;
        }

        if (_viewport.DisplayWidth <= 0 || _viewport.DisplayHeight <= 0)
        {
            return;
        }

        double dispW = _viewport.DisplayWidth;
        double dispH = _viewport.DisplayHeight;

        var cropRect = _cropDraft ?? new NormalizedRect(0, 0, 1, 1);

        var rect = new SKRect(
            (float)(cropRect.X * dispW),
            (float)(cropRect.Y * dispH),
            (float)(cropRect.Right * dispW),
            (float)(cropRect.Bottom * dispH));

        // Dim everything outside the selection.
        using var dimPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 140),
        };
        using var full = new SKPath { FillType = SKPathFillType.EvenOdd };
        full.AddRect(new SKRect(0, 0, (float)dispW, (float)dispH));
        full.AddRect(rect);
        canvas.DrawPath(full, dimPaint);

        // Bright border around the kept region + rule-of-thirds guides.
        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(0x31, 0x82, 0xED), // mac selectionStroke #3182ED
            StrokeWidth = 2f,
            IsAntialias = true,
        };
        canvas.DrawRect(rect, borderPaint);

        using var guidePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(255, 255, 255, 70),
            StrokeWidth = 1f,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new[] { 6f, 6f }, 0),
        };
        float thirdW = rect.Width / 3f;
        float thirdH = rect.Height / 3f;
        for (int i = 1; i <= 2; i++)
        {
            float x = rect.Left + (thirdW * i);
            float y = rect.Top + (thirdH * i);
            canvas.DrawLine(x, rect.Top, x, rect.Bottom, guidePaint);
            canvas.DrawLine(rect.Left, y, rect.Right, y, guidePaint);
        }
    }

    private void DrawSelection(SKCanvas canvas)
    {
        var selected = Model.Selected;
        if (selected is null || _viewport.DisplayWidth <= 0 || _viewport.DisplayHeight <= 0)
        {
            return;
        }

        double dispW = _viewport.DisplayWidth;
        double dispH = _viewport.DisplayHeight;

        var bounds = selected.Bounds();
        var rect = new SKRect(
            (float)(bounds.X * dispW),
            (float)(bounds.Y * dispH),
            (float)(bounds.Right * dispW),
            (float)(bounds.Bottom * dispH));

        using var dashPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(0x31, 0x82, 0xED), // mac selectionStroke #3182ED
            StrokeWidth = 1.5f,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new[] { 6f, 4f }, 0),
        };
        canvas.DrawRect(rect, dashPaint);

        // Handles read as holes punched in the frame (mac handleFill dark #2A2A2C).
        using var handlePaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = new SKColor(0x2A, 0x2A, 0x2C),
            IsAntialias = true,
        };
        using var handleBorder = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(0x31, 0x82, 0xED),
            StrokeWidth = 1.5f,
            IsAntialias = true,
        };

        if (selected.Tool.UsesEndPoints())
        {
            foreach (var p in new[] { selected.Start, selected.End })
            {
                canvas.DrawCircle((float)(p.X * dispW), (float)(p.Y * dispH), (float)HandleSizePx / 2, handlePaint);
                canvas.DrawCircle((float)(p.X * dispW), (float)(p.Y * dispH), (float)HandleSizePx / 2, handleBorder);
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

    /// Builds (or rebuilds) the display-resolution image the annotations are
    /// composited against. Pixelate/blur sample from this image, so it must
    /// match the coordinate space passed to AnnotationRenderer.Draw.
    private void EnsurePreviewImage()
    {
        double dispW = _viewport.DisplayWidth;
        double dispH = _viewport.DisplayHeight;
        if (_fullBitmap is null || dispW < 1 || dispH < 1)
        {
            return;
        }

        int targetW = Math.Max(1, (int)Math.Round(dispW));
        int targetH = Math.Max(1, (int)Math.Round(dispH));
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

    private Point ToCanvasPx(NormalizedPoint n)
    {
        var (x, y) = _viewport.ToCanvas(n.X, n.Y);
        return new Point(x, y);
    }

    private NormalizedPoint ToNorm(Point canvasPx)
    {
        var (x, y) = _viewport.ToNormalized(canvasPx.X, canvasPx.Y);
        return new NormalizedPoint(x, y);
    }

    /// Converts a canvas-pixel distance to normalized units (for tolerances).
    private double PxToNorm(double px)
    {
        if (_viewport.DisplayWidth <= 0 || _fullBitmap is null)
        {
            return 0.01;
        }

        double maxDim = Math.Max(_fullBitmap.Width, _fullBitmap.Height);
        return (px * _ratioX) / (_viewport.DisplayWidth * (_fullBitmap.Width / maxDim));
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
