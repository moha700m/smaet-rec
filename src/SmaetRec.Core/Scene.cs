namespace SmaetRec.Core;

public enum LayerKind { Screen, Phone }

public enum LayerHandle { None, Body, TopLeft, TopRight, BottomLeft, BottomRight, Rotate }

/// <summary>A movable item on the canvas. All units are canvas units (the canvas is always 1920 x 1080).</summary>
public sealed class Layer
{
    public LayerKind Kind { get; set; }
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    /// <summary>Width on the canvas. Height always follows from <see cref="Aspect"/>.</summary>
    public double Width { get; set; }
    /// <summary>Width divided by height of the layer box.</summary>
    public double Aspect { get; set; } = 16.0 / 9.0;
    public double RotationDeg { get; set; }
    public bool Visible { get; set; } = true;
    /// <summary>Phone only: true when the frame is shown sideways.</summary>
    public bool Landscape { get; set; }

    public double Height => Width / Aspect;
    public Vec2 Center => new(CenterX, CenterY);
    public const double RotateHandleOffset = 46;
    public const double MinWidth = 80;

    public Layer Clone() => (Layer)MemberwiseClone();

    /// <summary>Converts a canvas point to the layer's own unrotated space (origin at the layer centre).</summary>
    public Vec2 ToLocal(Vec2 canvasPoint) => (canvasPoint - Center).Rotate(-RotationDeg);
    public Vec2 ToCanvas(Vec2 local) => local.Rotate(RotationDeg) + Center;

    public bool Contains(Vec2 canvasPoint)
    {
        var l = ToLocal(canvasPoint);
        return Math.Abs(l.X) <= Width / 2 && Math.Abs(l.Y) <= Height / 2;
    }

    public Vec2 HandlePosition(LayerHandle handle)
    {
        double hw = Width / 2, hh = Height / 2;
        var local = handle switch
        {
            LayerHandle.TopLeft => new Vec2(-hw, -hh),
            LayerHandle.TopRight => new Vec2(hw, -hh),
            LayerHandle.BottomLeft => new Vec2(-hw, hh),
            LayerHandle.BottomRight => new Vec2(hw, hh),
            LayerHandle.Rotate => new Vec2(0, -hh - RotateHandleOffset),
            _ => new Vec2(0, 0),
        };
        return ToCanvas(local);
    }

    public LayerHandle HitTest(Vec2 canvasPoint, double handleRadius)
    {
        foreach (var h in new[] { LayerHandle.Rotate, LayerHandle.TopLeft, LayerHandle.TopRight, LayerHandle.BottomLeft, LayerHandle.BottomRight })
            if ((HandlePosition(h) - canvasPoint).Length <= handleRadius) return h;
        return Contains(canvasPoint) ? LayerHandle.Body : LayerHandle.None;
    }

    public void MoveBy(Vec2 delta) { CenterX += delta.X; CenterY += delta.Y; }

    /// <summary>Resizes around the centre so the dragged corner follows the pointer.</summary>
    public void ResizeTo(Vec2 canvasPoint)
    {
        var l = ToLocal(canvasPoint);
        double byWidth = Math.Abs(l.X) * 2, byHeight = Math.Abs(l.Y) * 2 * Aspect;
        Width = Math.Clamp(Math.Max(byWidth, byHeight), MinWidth, Scene.CanvasWidth * 3);
    }

    /// <summary>Rotates so the rotate handle points at the pointer. Snaps to right angles when close.</summary>
    public void RotateTo(Vec2 canvasPoint)
    {
        var d = canvasPoint - Center;
        if (d.Length < 1) return;
        double deg = Math.Atan2(d.Y, d.X) * 180 / Math.PI + 90;
        deg = ((deg % 360) + 360) % 360;
        double nearest = Math.Round(deg / 90) * 90;
        RotationDeg = Math.Abs(deg - nearest) < 4 ? nearest % 360 : deg;
    }

    public void SetAspectKeepingArea(double aspect)
    {
        if (aspect <= 0 || Math.Abs(aspect - Aspect) < 1e-4) return;
        double area = Width * Height;
        Aspect = aspect;
        Width = Math.Sqrt(area * aspect);
    }
}

public enum LayoutPreset { SideBySide, PictureInPicture }

public sealed class EffectSettings
{
    public bool CursorHighlight { get; set; }
    public bool ClickRipples { get; set; } = true;
    public bool Shadows { get; set; } = true;
    public int BackgroundIndex { get; set; }
}

/// <summary>Everything that describes the picture: layers, drawings, look. Shared by UI and renderer under <see cref="Sync"/>.</summary>
public sealed class Scene
{
    public const double CanvasWidth = 1920;
    public const double CanvasHeight = 1080;
    /// <summary>Phone body proportions (portrait), width / height, including the bezel.</summary>
    public const double PhoneBodyAspect = 0.4875;

    public object Sync { get; } = new();
    public Layer Screen { get; private set; } = new() { Kind = LayerKind.Screen };
    public Layer Phone { get; private set; } = new() { Kind = LayerKind.Phone, Aspect = PhoneBodyAspect };
    public AnnotationStore Annotations { get; } = new();
    public EffectSettings Effects { get; private set; } = new();
    public LayoutPreset LastPreset { get; private set; } = LayoutPreset.SideBySide;

    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();

    public Scene()
    {
        ApplyPreset(LayoutPreset.SideBySide);
        Annotations.Changed += () => Changed?.Invoke();
    }

    public IEnumerable<Layer> LayersBottomToTop { get { yield return Screen; yield return Phone; } }

    public Layer Get(LayerKind kind) => kind == LayerKind.Screen ? Screen : Phone;

    public void ApplyPreset(LayoutPreset preset)
    {
        lock (Sync)
        {
            LastPreset = preset;
            Screen.RotationDeg = 0;
            Phone.RotationDeg = 0;
            double phoneAspect = Phone.Aspect;
            if (preset == LayoutPreset.SideBySide)
            {
                const double margin = 60, gap = 50;
                double phoneH = CanvasHeight - 2 * margin;
                double phoneW = phoneH * phoneAspect;
                if (phoneW > CanvasWidth * 0.45) { phoneW = CanvasWidth * 0.45; phoneH = phoneW / phoneAspect; }
                double screenMaxW = CanvasWidth - 2 * margin - gap - phoneW;
                var box = RectD.Fit(Screen.Aspect, new RectD(margin, margin, screenMaxW, CanvasHeight - 2 * margin));
                Screen.Width = box.Width; Screen.CenterX = box.Center.X; Screen.CenterY = box.Center.Y;
                Phone.Width = phoneW;
                Phone.CenterX = CanvasWidth - margin - phoneW / 2;
                Phone.CenterY = CanvasHeight / 2;
            }
            else
            {
                const double margin = 40;
                var box = RectD.Fit(Screen.Aspect, new RectD(margin, margin, CanvasWidth - 2 * margin, CanvasHeight - 2 * margin));
                Screen.Width = box.Width; Screen.CenterX = box.Center.X; Screen.CenterY = box.Center.Y;
                double phoneH = Phone.Landscape ? CanvasHeight * 0.30 : CanvasHeight * 0.62;
                Phone.Width = phoneH * phoneAspect;
                Phone.CenterX = CanvasWidth - margin - 50 - Phone.Width / 2;
                Phone.CenterY = CanvasHeight - margin - 40 - phoneH / 2;
            }
        }
        NotifyChanged();
    }

    public void ResetLayout() => ApplyPreset(LastPreset);

    /// <summary>Updates the screen layer shape to match the captured source.</summary>
    public void SetScreenSourceSize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        lock (Sync) Screen.SetAspectKeepingArea((double)width / height);
        NotifyChanged();
    }

    public void SetPhoneLandscape(bool landscape)
    {
        lock (Sync)
        {
            if (Phone.Landscape == landscape) return;
            Phone.Landscape = landscape;
            Phone.SetAspectKeepingArea(landscape ? 1 / PhoneBodyAspect : PhoneBodyAspect);
        }
        NotifyChanged();
    }

    /// <summary>Topmost visible layer under the point, with the part that was hit.</summary>
    public (Layer? layer, LayerHandle handle) HitTest(Vec2 p, Layer? selected, double handleRadius)
    {
        lock (Sync)
        {
            if (selected is { Visible: true })
            {
                var h = selected.HitTest(p, handleRadius);
                if (h != LayerHandle.None && h != LayerHandle.Body) return (selected, h);
            }
            foreach (var layer in LayersBottomToTop.Reverse())
                if (layer.Visible && layer.Contains(p)) return (layer, LayerHandle.Body);
            return (null, LayerHandle.None);
        }
    }

    public SceneData ToData()
    {
        lock (Sync)
            return new SceneData
            {
                Screen = Screen.Clone(), Phone = Phone.Clone(),
                Effects = new EffectSettings { BackgroundIndex = Effects.BackgroundIndex, ClickRipples = Effects.ClickRipples, CursorHighlight = Effects.CursorHighlight, Shadows = Effects.Shadows },
                Preset = LastPreset,
                Annotations = Annotations.Snapshot().Select(a => a.Clone()).ToList(),
            };
    }

    public void Load(SceneData data)
    {
        lock (Sync)
        {
            if (data.Screen is { Width: > 0, Aspect: > 0 }) { Screen = data.Screen.Clone(); Screen.Kind = LayerKind.Screen; }
            if (data.Phone is { Width: > 0, Aspect: > 0 }) { Phone = data.Phone.Clone(); Phone.Kind = LayerKind.Phone; }
            if (data.Effects != null) Effects = data.Effects;
            LastPreset = data.Preset;
        }
        Annotations.ReplaceAll(data.Annotations ?? new());
        NotifyChanged();
    }
}

/// <summary>Plain serialisable copy of a <see cref="Scene"/>.</summary>
public sealed class SceneData
{
    public Layer? Screen { get; set; }
    public Layer? Phone { get; set; }
    public EffectSettings? Effects { get; set; }
    public LayoutPreset Preset { get; set; }
    public List<Annotation>? Annotations { get; set; }
}
