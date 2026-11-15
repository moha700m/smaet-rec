namespace SmaetRec.Core;

/// <summary>Smoothly animated zoom of the whole picture around a focus point (canvas units).</summary>
public sealed class ZoomController
{
    public const double MinZoom = 1.0, MaxZoom = 4.0;
    private readonly object _lock = new();
    private double _target = 1, _current = 1;
    private Vec2 _targetFocus = new(Scene.CanvasWidth / 2, Scene.CanvasHeight / 2);
    private Vec2 _focus = new(Scene.CanvasWidth / 2, Scene.CanvasHeight / 2);

    public double Target { get { lock (_lock) return _target; } }
    public double Current { get { lock (_lock) return _current; } }
    public Vec2 Focus { get { lock (_lock) return _focus; } }
    public bool IsAnimating { get { lock (_lock) return Math.Abs(_target - _current) > 0.001 || (_targetFocus - _focus).Length > 0.5; } }

    public void ZoomBy(double factor, Vec2? focus = null)
    {
        lock (_lock)
        {
            _target = Math.Clamp(_target * factor, MinZoom, MaxZoom);
            if (focus is { } f) _targetFocus = f;
            if (_target <= MinZoom + 1e-6) _targetFocus = new(Scene.CanvasWidth / 2, Scene.CanvasHeight / 2);
        }
    }

    public void Reset() { lock (_lock) { _target = 1; _targetFocus = new(Scene.CanvasWidth / 2, Scene.CanvasHeight / 2); } }

    /// <summary>Advances the animation. Returns true while the picture is still moving.</summary>
    public bool Step(double seconds)
    {
        lock (_lock)
        {
            double k = 1 - Math.Exp(-seconds * 9);
            _current += (_target - _current) * k;
            _focus = _focus + (_targetFocus - _focus) * k;
            if (Math.Abs(_target - _current) < 0.001) _current = _target;
            if ((_targetFocus - _focus).Length < 0.5) _focus = _targetFocus;
            return Math.Abs(_target - _current) > 0.001 || (_targetFocus - _focus).Length > 0.5;
        }
    }

    /// <summary>Top-left of the visible canvas region for the current zoom, clamped so no empty border shows.</summary>
    public RectD VisibleRegion()
    {
        lock (_lock)
        {
            double w = Scene.CanvasWidth / _current, h = Scene.CanvasHeight / _current;
            double x = Math.Clamp(_focus.X - w / 2, 0, Scene.CanvasWidth - w);
            double y = Math.Clamp(_focus.Y - h / 2, 0, Scene.CanvasHeight - h);
            return new RectD(x, y, w, h);
        }
    }

    /// <summary>Maps a point on the final picture (canvas units) back to the un-zoomed canvas.</summary>
    public Vec2 OutputToCanvas(Vec2 p)
    {
        var r = VisibleRegion();
        return new Vec2(r.X + p.X * r.Width / Scene.CanvasWidth, r.Y + p.Y * r.Height / Scene.CanvasHeight);
    }
}
