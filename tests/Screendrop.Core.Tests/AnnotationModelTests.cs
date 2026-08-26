using Screendrop.Core.Annotations;
using Xunit;

namespace Screendrop.Core.Tests;

public class AnnotationModelTests
{
    private static Annotation Rect(double x, double y, double w, double h) => new()
    {
        Tool = AnnotationTool.Rectangle,
        Rect = new NormalizedRect(x, y, w, h),
    };

    [Fact]
    public void Add_selects_and_raises_changed()
    {
        var model = new AnnotationEditorModel();
        int changes = 0;
        model.Changed += () => changes++;

        var annotation = Rect(0.1, 0.1, 0.2, 0.2);
        model.Add(annotation);

        Assert.Single(model.Annotations);
        Assert.Same(annotation, model.Selected);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Undo_redo_roundtrip_restores_state()
    {
        var model = new AnnotationEditorModel();
        var a = Rect(0.1, 0.1, 0.2, 0.2);
        var b = Rect(0.5, 0.5, 0.1, 0.1);

        model.Add(a);
        model.Add(b);
        Assert.Equal(2, model.Annotations.Count);

        model.Undo();
        Assert.Single(model.Annotations);
        Assert.True(model.CanRedo);

        model.Redo();
        Assert.Equal(2, model.Annotations.Count);
        Assert.False(model.CanRedo);
    }

    [Fact]
    public void Undo_on_empty_is_noop()
    {
        var model = new AnnotationEditorModel();
        model.Undo();
        Assert.Empty(model.Annotations);
        Assert.False(model.CanUndo);
    }

    [Fact]
    public void New_mutation_clears_redo_stack()
    {
        var model = new AnnotationEditorModel();
        model.Add(Rect(0, 0, 0.1, 0.1));
        model.Add(Rect(0.2, 0.2, 0.1, 0.1));
        model.Undo();
        Assert.True(model.CanRedo);

        model.Add(Rect(0.4, 0.4, 0.1, 0.1));
        Assert.False(model.CanRedo);
    }

    [Fact]
    public void Batched_translate_is_single_undo_step()
    {
        var model = new AnnotationEditorModel();
        var annotation = Rect(0.1, 0.1, 0.2, 0.2);
        model.Add(annotation);

        model.BeginBatch();
        model.Translate(annotation, 0.05, 0);
        model.Translate(annotation, 0.05, 0);
        model.EndBatch();

        Assert.Equal(0.2, annotation.Rect.X, 6);

        model.Undo();
        Assert.Equal(0.1, model.Annotations[0].Rect.X, 6);
    }

    [Fact]
    public void Remove_deletes_and_undoes()
    {
        var model = new AnnotationEditorModel();
        var annotation = Rect(0.1, 0.1, 0.2, 0.2);
        model.Add(annotation);

        model.Remove(annotation);
        Assert.Empty(model.Annotations);
        Assert.Null(model.Selected);

        model.Undo();
        Assert.Single(model.Annotations);
    }

    [Fact]
    public void Remove_unknown_annotation_is_noop()
    {
        var model = new AnnotationEditorModel();
        model.Add(Rect(0, 0, 0.1, 0.1));

        model.Remove(Rect(0.9, 0.9, 0.05, 0.05));
        Assert.Single(model.Annotations);

        // The only undo step is the Add; removing the unknown annotation
        // added none. Undoing once returns to empty.
        model.Undo();
        Assert.Empty(model.Annotations);
        Assert.False(model.CanUndo);
    }

    [Fact]
    public void HitTest_returns_topmost_match()
    {
        var model = new AnnotationEditorModel();
        var bottom = Rect(0.1, 0.1, 0.5, 0.5);
        var top = Rect(0.2, 0.2, 0.5, 0.5);
        model.Add(bottom);
        model.Add(top);

        var hit = model.HitTest(0.3, 0.3, 0.001);
        Assert.Same(top, hit);
    }

    [Fact]
    public void HitTest_miss_returns_null()
    {
        var model = new AnnotationEditorModel();
        model.Add(Rect(0.1, 0.1, 0.1, 0.1));

        Assert.Null(model.HitTest(0.9, 0.9, 0.001));
    }

    [Fact]
    public void Arrow_hit_test_uses_segment_distance()
    {
        var arrow = new Annotation
        {
            Tool = AnnotationTool.Arrow,
            Start = new NormalizedPoint(0.1, 0.1),
            End = new NormalizedPoint(0.9, 0.1),
            StrokeWidth = 0.006,
        };

        Assert.True(arrow.HitTest(0.5, 0.105, 0.01));
        Assert.False(arrow.HitTest(0.5, 0.3, 0.01));
    }

    [Fact]
    public void Freehand_hit_test_checks_each_segment()
    {
        var ink = new Annotation
        {
            Tool = AnnotationTool.Freehand,
            Points = new List<NormalizedPoint>
            {
                new(0.1, 0.1),
                new(0.2, 0.2),
                new(0.3, 0.1),
            },
            StrokeWidth = 0.006,
        };

        Assert.True(ink.HitTest(0.15, 0.15, 0.01));
        Assert.False(ink.HitTest(0.25, 0.5, 0.01));
    }

    [Fact]
    public void RenumberMarkers_numbers_in_creation_order()
    {
        var model = new AnnotationEditorModel();
        var first = new Annotation { Tool = AnnotationTool.NumberedCircle, Rect = new NormalizedRect(0.1, 0.1, 0.05, 0.05) };
        var second = new Annotation { Tool = AnnotationTool.NumberedCircle, Rect = new NormalizedRect(0.5, 0.5, 0.05, 0.05) };
        model.Add(first);
        model.Add(Rect(0.3, 0.3, 0.1, 0.1));
        model.Add(second);

        model.RenumberMarkers();

        Assert.Equal(1, first.Number);
        Assert.Equal(2, second.Number);
    }

    [Fact]
    public void Document_roundtrip_preserves_annotations()
    {
        var model = new AnnotationEditorModel();
        var rect = Rect(0.1, 0.2, 0.3, 0.4);
        rect.Color = AnnotationColor.Blue;
        var arrow = new Annotation
        {
            Tool = AnnotationTool.Arrow,
            Start = new NormalizedPoint(0.1, 0.9),
            End = new NormalizedPoint(0.8, 0.2),
        };
        var ink = new Annotation
        {
            Tool = AnnotationTool.Freehand,
            Points = new List<NormalizedPoint> { new(0.1, 0.1), new(0.2, 0.15) },
        };
        var text = new Annotation
        {
            Tool = AnnotationTool.Text,
            Text = "hello",
            Rect = new NormalizedRect(0.4, 0.4, 0.2, 0.05),
        };
        model.Add(rect);
        model.Add(arrow);
        model.Add(ink);
        model.Add(text);

        string json = model.ToDocument().ToJson();
        var restored = AnnotationDocument.FromJson(json);

        Assert.NotNull(restored);
        Assert.Equal(4, restored!.Annotations.Count);
        Assert.Equal(AnnotationTool.Rectangle, restored.Annotations[0].Tool);
        Assert.Equal(0.1, restored.Annotations[0].Rect.X, 6);
        Assert.Equal(AnnotationColor.Blue, restored.Annotations[0].Color);
        Assert.Equal(0.8, restored.Annotations[1].End.X, 6);
        Assert.Equal(2, restored.Annotations[2].Points.Count);
        Assert.Equal("hello", restored.Annotations[3].Text);
    }

    [Fact]
    public void Document_from_invalid_json_returns_null()
    {
        Assert.Null(AnnotationDocument.FromJson("{ not json"));
    }

    [Fact]
    public void Load_replaces_state_and_resets_history()
    {
        var model = new AnnotationEditorModel();
        model.Add(Rect(0, 0, 0.1, 0.1));

        var document = new AnnotationDocument();
        document.Annotations.Add(Rect(0.5, 0.5, 0.2, 0.2));

        model.Load(document);

        Assert.Single(model.Annotations);
        Assert.Equal(0.5, model.Annotations[0].Rect.X, 6);
        Assert.False(model.CanUndo);
    }

    [Fact]
    public void Undo_depth_is_bounded()
    {
        var model = new AnnotationEditorModel();
        for (int i = 0; i < AnnotationEditorModel.MaxUndoDepth + 25; i++)
        {
            model.Add(Rect(0, 0, 0.01, 0.01));
        }

        int undos = 0;
        while (model.CanUndo)
        {
            model.Undo();
            undos++;
        }

        Assert.Equal(AnnotationEditorModel.MaxUndoDepth, undos);
    }

    [Fact]
    public void NormalizedRect_from_points_orders_coordinates()
    {
        var rect = NormalizedRect.FromPoints(0.7, 0.8, 0.2, 0.3);
        Assert.Equal(0.2, rect.X, 6);
        Assert.Equal(0.3, rect.Y, 6);
        Assert.Equal(0.5, rect.Width, 6);
        Assert.Equal(0.5, rect.Height, 6);
    }

    [Fact]
    public void NormalizedRect_clamp_keeps_rect_inside_unit_square()
    {
        var rect = new NormalizedRect(-0.1, -0.1, 1.5, 1.5).ClampToUnit();
        Assert.Equal(0, rect.X, 6);
        Assert.Equal(0, rect.Y, 6);
        Assert.Equal(1, rect.Width, 6);
        Assert.Equal(1, rect.Height, 6);
    }

    [Fact]
    public void Translate_moves_all_geometry_kinds()
    {
        var arrow = new Annotation
        {
            Tool = AnnotationTool.Arrow,
            Start = new NormalizedPoint(0.1, 0.1),
            End = new NormalizedPoint(0.2, 0.2),
        };
        arrow.Translate(0.1, 0.2);
        Assert.Equal(0.2, arrow.Start.X, 6);
        Assert.Equal(0.3, arrow.Start.Y, 6);
        Assert.Equal(0.3, arrow.End.X, 6);

        var ink = new Annotation
        {
            Tool = AnnotationTool.Freehand,
            Points = new List<NormalizedPoint> { new(0.1, 0.1) },
        };
        ink.Translate(0.05, 0.05);
        Assert.Equal(0.15, ink.Points[0].X, 6);
    }
}
