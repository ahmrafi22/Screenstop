namespace Screenstop.Core.Annotations;

/// The annotation editing state: an ordered annotation list plus undo/redo.
/// Pure logic, no UI dependencies (mac model-layer discipline).
///
/// Undo/redo is snapshot-based over the annotation list. Snapshots are
/// taken at mutation boundaries (BeginBatch/EndBatch) so a multi-step
/// gesture (drag-to-move) collapses into one undo step.
public sealed class AnnotationEditorModel
{
    private readonly List<Annotation> _annotations = new();
    private readonly List<List<Annotation>> _undo = new();
    private readonly List<List<Annotation>> _redo = new();
    private List<Annotation>? _batchSnapshot;

    public const int MaxUndoDepth = 100;

    public event Action? Changed;

    public IReadOnlyList<Annotation> Annotations => _annotations;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public Annotation? Selected { get; private set; }

    public void Select(Annotation? annotation)
    {
        if (Selected == annotation)
        {
            return;
        }

        Selected = annotation;
        Changed?.Invoke();
    }

    /// Starts a mutation batch (e.g. a drag). Call EndBatch when done;
    /// the whole batch becomes a single undo step.
    public void BeginBatch()
    {
        _batchSnapshot ??= Snapshot();
    }

    public void EndBatch()
    {
        if (_batchSnapshot is null)
        {
            return;
        }

        PushUndo(_batchSnapshot);
        _batchSnapshot = null;
        _redo.Clear();
        Changed?.Invoke();
    }

    public void Add(Annotation annotation)
    {
        PushUndo(Snapshot());
        _redo.Clear();
        _annotations.Add(annotation);
        Selected = annotation;
        Changed?.Invoke();
    }

    public void Remove(Annotation annotation)
    {
        if (!_annotations.Contains(annotation))
        {
            return;
        }

        PushUndo(Snapshot());
        _redo.Clear();
        _annotations.Remove(annotation);
        if (Selected == annotation)
        {
            Selected = null;
        }

        Changed?.Invoke();
    }

    /// Moves an annotation by a normalized delta. Wrap the gesture in
    /// BeginBatch/EndBatch to make it one undo step.
    public void Translate(Annotation annotation, double dx, double dy)
    {
        annotation.Translate(dx, dy);
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Add(Snapshot());
        Restore(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Add(Snapshot());
        Restore(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_annotations.Count == 0)
        {
            return;
        }

        PushUndo(Snapshot());
        _redo.Clear();
        _annotations.Clear();
        Selected = null;
        Changed?.Invoke();
    }

    /// Loads a document's annotations, resetting history.
    public void Load(AnnotationDocument? document)
    {
        _annotations.Clear();
        if (document is not null)
        {
            _annotations.AddRange(document.Annotations.Select(a => a.Clone()));
        }

        _undo.Clear();
        _redo.Clear();
        _batchSnapshot = null;
        Selected = null;
        Changed?.Invoke();
    }

    public AnnotationDocument ToDocument() => new()
    {
        Version = AnnotationDocument.CurrentVersion,
        Annotations = _annotations.Select(a => a.Clone()).ToList(),
    };

    /// Topmost annotation under the point, or null.
    public Annotation? HitTest(double nx, double ny, double tolerance)
    {
        for (int i = _annotations.Count - 1; i >= 0; i--)
        {
            if (_annotations[i].HitTest(nx, ny, tolerance))
            {
                return _annotations[i];
            }
        }

        return null;
    }

    /// Assigns 1..N numbers to NumberedCircle annotations in creation order.
    public void RenumberMarkers()
    {
        int next = 1;
        foreach (var annotation in _annotations)
        {
            if (annotation.Tool == AnnotationTool.NumberedCircle)
            {
                annotation.Number = next++;
            }
        }
    }

    private List<Annotation> Snapshot() => _annotations.Select(a => a.Clone()).ToList();

    private void Restore(List<Annotation> snapshot)
    {
        _annotations.Clear();
        _annotations.AddRange(snapshot.Select(a => a.Clone()));
        Selected = null;
    }

    private void PushUndo(List<Annotation> snapshot)
    {
        _undo.Add(snapshot);
        if (_undo.Count > MaxUndoDepth)
        {
            _undo.RemoveAt(0);
        }
    }
}
