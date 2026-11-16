namespace SmaetRec.Core;

public enum AnnotationKind { Pen, Highlighter, Arrow, Ellipse, Rectangle, Text, Blur }

/// <summary>One drawing on the canvas, in canvas units.</summary>
public sealed class Annotation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AnnotationKind Kind { get; set; }
    /// <summary>0xAARRGGBB</summary>
    public uint Color { get; set; } = 0xFFFF3B30;
    public double Thickness { get; set; } = 6;
    public List<Vec2> Points { get; set; } = new();
    public string? Text { get; set; }

    public Annotation Clone() => new()
    {
        Id = Id, Kind = Kind, Color = Color, Thickness = Thickness, Text = Text, Points = new List<Vec2>(Points),
    };

    public bool IsFreehand => Kind is AnnotationKind.Pen or AnnotationKind.Highlighter;
    public double FontSize => 18 + Thickness * 4;
    /// <summary>Arabic/Hebrew text is laid out leftwards from the point that was clicked.</summary>
    public bool IsRtlText => Text != null && Text.Any(c => c is >= '\u0590' and <= '\u08FF' or >= '\uFB1D' and <= '\uFEFC');

    public RectD Bounds
    {
        get
        {
            if (Points.Count == 0) return default;
            double x0 = Points.Min(p => p.X), y0 = Points.Min(p => p.Y), x1 = Points.Max(p => p.X), y1 = Points.Max(p => p.Y);
            if (Kind == AnnotationKind.Text)
            {
                double w = Math.Max(40, (Text?.Length ?? 1) * FontSize * 0.6);
                return new RectD(IsRtlText ? x0 - w : x0, y0, w, FontSize * 1.4);
            }
            return new RectD(x0, y0, x1 - x0, y1 - y0);
        }
    }

    /// <summary>True when the drawing is too small to be worth keeping (an accidental click).</summary>
    public bool IsDegenerate => Kind switch
    {
        AnnotationKind.Text => string.IsNullOrWhiteSpace(Text),
        AnnotationKind.Pen or AnnotationKind.Highlighter => Points.Count < 1,
        _ => Points.Count < 2 || (Points[1] - Points[0]).Length < 4,
    };

    public bool HitTest(Vec2 p, double tolerance)
    {
        if (Points.Count == 0) return false;
        double tol = tolerance + Thickness / 2;
        switch (Kind)
        {
            case AnnotationKind.Pen:
            case AnnotationKind.Highlighter:
                if (Points.Count == 1) return (Points[0] - p).Length <= tol;
                for (int i = 1; i < Points.Count; i++)
                    if (Vec2.DistanceToSegment(p, Points[i - 1], Points[i]) <= tol) return true;
                return false;
            case AnnotationKind.Arrow:
                return Points.Count > 1 && Vec2.DistanceToSegment(p, Points[0], Points[1]) <= tol + 4;
            case AnnotationKind.Rectangle:
            {
                if (Points.Count < 2) return false;
                var r = RectD.FromPoints(Points[0], Points[1]);
                var outer = new RectD(r.X - tol, r.Y - tol, r.Width + 2 * tol, r.Height + 2 * tol);
                var inner = new RectD(r.X + tol, r.Y + tol, r.Width - 2 * tol, r.Height - 2 * tol);
                return outer.Contains(p) && !(inner.Width > 0 && inner.Height > 0 && inner.Contains(p));
            }
            case AnnotationKind.Ellipse:
            {
                if (Points.Count < 2) return false;
                var r = RectD.FromPoints(Points[0], Points[1]);
                double rx = r.Width / 2, ry = r.Height / 2;
                if (rx < 1 || ry < 1) return false;
                double dx = p.X - r.Center.X, dy = p.Y - r.Center.Y;
                double d = Math.Sqrt(dx * dx / (rx * rx) + dy * dy / (ry * ry));
                return Math.Abs(d - 1) * Math.Min(rx, ry) <= tol;
            }
            default:
            {
                var b = Kind == AnnotationKind.Blur && Points.Count > 1 ? RectD.FromPoints(Points[0], Points[1]) : Bounds;
                return new RectD(b.X - tolerance, b.Y - tolerance, b.Width + 2 * tolerance, b.Height + 2 * tolerance).Contains(p);
            }
        }
    }
}

/// <summary>The list of drawings with undo/redo. Safe to read from the render thread via <see cref="Snapshot"/>.</summary>
public sealed class AnnotationStore
{
    private readonly object _lock = new();
    private List<Annotation> _items = new();
    private Annotation[] _snapshot = Array.Empty<Annotation>();
    private Annotation? _inProgress;
    /// <summary>How many steps can be taken back. Each step keeps a full list, so an unbounded history grows with the square of the stroke count.</summary>
    public const int MaxHistory = 200;
    private readonly LinkedList<(List<Annotation> before, List<Annotation> after)> _undo = new();
    private readonly Stack<(List<Annotation> before, List<Annotation> after)> _redo = new();

    public event Action? Changed;

    public bool CanUndo { get { lock (_lock) return _undo.Count > 0; } }
    public bool CanRedo { get { lock (_lock) return _redo.Count > 0; } }
    public int Count { get { lock (_lock) return _items.Count; } }

    /// <summary>Finished drawings plus the one currently being drawn, in paint order.</summary>
    public Annotation[] Snapshot() { lock (_lock) return _snapshot; }

    private void Rebuild()
    {
        var list = new List<Annotation>(_items.Count + 1);
        list.AddRange(_items);
        if (_inProgress != null) list.Add(_inProgress.Clone());
        _snapshot = list.ToArray();
    }

    private void Commit(List<Annotation> after)
    {
        _undo.AddLast((_items, after));
        if (_undo.Count > MaxHistory) _undo.RemoveFirst();
        _redo.Clear();
        _items = after;
        Rebuild();
    }

    public void SetInProgress(Annotation? a)
    {
        lock (_lock) { _inProgress = a; Rebuild(); }
        Changed?.Invoke();
    }

    public void Add(Annotation a)
    {
        lock (_lock)
        {
            _inProgress = null;
            if (a.IsDegenerate) { Rebuild(); }
            else Commit(new List<Annotation>(_items) { a.Clone() }); // the caller may keep editing its own object
        }
        Changed?.Invoke();
    }

    /// <summary>Removes the topmost drawing under the point. Returns true when something was erased.</summary>
    public bool EraseAt(Vec2 p, double tolerance)
    {
        lock (_lock)
        {
            int idx = _items.FindLastIndex(a => a.HitTest(p, tolerance));
            if (idx < 0) return false;
            var after = new List<Annotation>(_items);
            after.RemoveAt(idx);
            Commit(after);
        }
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (_items.Count == 0) return;
            Commit(new List<Annotation>());
        }
        Changed?.Invoke();
    }

    public void Undo()
    {
        lock (_lock)
        {
            if (_undo.Count == 0) return;
            var step = _undo.Last!.Value;
            _undo.RemoveLast();
            _redo.Push(step);
            _items = step.before;
            Rebuild();
        }
        Changed?.Invoke();
    }

    public void Redo()
    {
        lock (_lock)
        {
            if (_redo.Count == 0) return;
            var step = _redo.Pop();
            _undo.AddLast(step);
            _items = step.after;
            Rebuild();
        }
        Changed?.Invoke();
    }

    public void ReplaceAll(List<Annotation> items)
    {
        lock (_lock)
        {
            _items = items.Select(a => a.Clone()).ToList();
            _undo.Clear(); _redo.Clear(); _inProgress = null;
            Rebuild();
        }
        Changed?.Invoke();
    }
}
