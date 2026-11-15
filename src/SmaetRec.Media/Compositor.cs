using System.Numerics;
using SmaetRec.Core;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace SmaetRec.Media;

public readonly record struct Ripple(Vec2 Position, double Age);

/// <summary>Everything the compositor needs to draw one frame, copied out of the live scene.</summary>
public sealed class FrameState
{
    public Layer Screen = new();
    public Layer Phone = new();
    public Annotation[] Annotations = Array.Empty<Annotation>();
    public EffectSettings Effects = new();
    public RectD ZoomRegion = new(0, 0, Scene.CanvasWidth, Scene.CanvasHeight);
    /// <summary>Preview pointer on the final picture, canvas units. Null when the pointer is elsewhere.</summary>
    public Vec2? Pointer;
    public bool Spotlight, Magnifier;
    /// <summary>Desktop cursor mapped onto the canvas, when cursor highlighting is on.</summary>
    public Vec2? CursorHighlight;
    public List<Ripple> Ripples = new();
    public LayerKind? Selected;
    public bool Recording;
    public string PhoneHint = "", ScreenHint = "", SimulationLabel = "";
    public bool PhoneIsSimulated, ScreenIsSimulated;
    public bool IsZoomed => ZoomRegion.Width < Scene.CanvasWidth - 0.5;
}

/// <summary>
/// Draws the scene with Direct2D into one output texture. The same texture feeds the preview and the recorder,
/// so the video is exactly what is shown.
/// </summary>
public sealed class Compositor : IDisposable
{
    public const int BackgroundCount = 7;
    private readonly GraphicsDevice _gd;
    private readonly ID2D1DeviceContext _dc;
    private readonly float _scale;
    private readonly ID3D11Texture2D _layersTex, _contentTex, _outputTex;
    private readonly ID2D1Bitmap1 _layers, _content, _output;
    private readonly ID2D1SolidColorBrush _brush;
    private readonly ID2D1BitmapBrush1 _bitmapBrush;
    private readonly ID2D1StrokeStyle _round;
    private readonly GaussianBlur _blur;
    private readonly ID2D1RadialGradientBrush _spotBrush;
    private readonly ID2D1Brush?[] _backgrounds = new ID2D1Brush?[BackgroundCount];
    private readonly ID2D1LinearGradientBrush _bodyBrush;
    private readonly IDWriteTextFormat _hintFormat;
    private readonly Dictionary<Guid, (int count, ID2D1PathGeometry geometry)> _strokeCache = new();
    private readonly Dictionary<int, IDWriteTextFormat> _textFormats = new();

    public int Width { get; }
    public int Height { get; }
    public ID3D11Texture2D OutputTexture => _outputTex;
    public ID2D1Bitmap1 OutputBitmap => _output;

    public Compositor(GraphicsDevice gd, int width, int height)
    {
        _gd = gd; _dc = gd.DC; Width = width; Height = height;
        _scale = (float)(width / Scene.CanvasWidth);
        (_layersTex, _layers) = gd.CreateTarget(width, height);
        (_contentTex, _content) = gd.CreateTarget(width, height);
        (_outputTex, _output) = gd.CreateTarget(width, height);
        _brush = _dc.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        _bitmapBrush = _dc.CreateBitmapBrush(_layers, new BitmapBrushProperties1(ExtendMode.Clamp, ExtendMode.Clamp, InterpolationMode.Linear));
        _round = gd.D2DFactory.CreateStrokeStyle(new StrokeStyleProperties { StartCap = CapStyle.Round, EndCap = CapStyle.Round, LineJoin = LineJoin.Round, DashCap = CapStyle.Round });
        _blur = new GaussianBlur(_dc) { StandardDeviation = 16 * _scale, BorderMode = BorderMode.Hard };
        using (var stops = _dc.CreateGradientStopCollection(new[]
               {
                   new GradientStop(0f, new Color4(0, 0, 0, 0)), new GradientStop(0.78f, new Color4(0, 0, 0, 0)),
                   new GradientStop(1f, new Color4(0, 0, 0, 0.68f)),
               }))
            _spotBrush = _dc.CreateRadialGradientBrush(new RadialGradientBrushProperties { RadiusX = 230, RadiusY = 230 }, stops);
        using (var stops = _dc.CreateGradientStopCollection(new[]
               {
                   new GradientStop(0f, Rgb(0x8E8E93)), new GradientStop(0.5f, Rgb(0x5A5A5F)), new GradientStop(1f, Rgb(0x3A3A3E)),
               }))
            _bodyBrush = _dc.CreateLinearGradientBrush(new LinearGradientBrushProperties { StartPoint = new Vector2(-0.5f, -0.5f), EndPoint = new Vector2(0.5f, 0.5f) }, stops);
        _hintFormat = gd.DWrite.CreateTextFormat("Segoe UI", FontWeight.SemiBold, FontStyle.Normal, 34);
        _hintFormat.TextAlignment = TextAlignment.Center;
        _hintFormat.ParagraphAlignment = ParagraphAlignment.Center;
    }

    private static Color4 Rgb(uint rgb, float a = 1) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);
    private static Color4 Argb(uint argb, float alphaScale = 1) => Rgb(argb & 0xFFFFFF, (argb >> 24) / 255f * alphaScale);
    private static Vector2 V(Vec2 p) => new((float)p.X, (float)p.Y);
    private static Rect R(RectD r) => new((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height);
    private static System.Drawing.RectangleF RF(RectD r) => new((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height);
    private ID2D1SolidColorBrush Solid(Color4 c) { _brush.Color = c; return _brush; }

    /// <summary>Colours of the background choices, for the UI swatches (top-left, bottom-right).</summary>
    public static (uint from, uint to) BackgroundColors(int index) => index switch
    {
        1 => (0x1E3A8A, 0x0B1026),
        2 => (0x7C3AED, 0xDB2777),
        3 => (0x0F766E, 0x14532D),
        4 => (0xEA580C, 0x7C2D12),
        5 => (0xE5E7EB, 0x9CA3AF),
        6 => (0x000000, 0x000000),
        _ => (0x1B1E26, 0x12141A),
    };

    private ID2D1Brush Background(int index)
    {
        index = Math.Clamp(index, 0, BackgroundCount - 1);
        if (_backgrounds[index] is { } existing) return existing;
        var (from, to) = BackgroundColors(index);
        using var stops = _dc.CreateGradientStopCollection(new[] { new GradientStop(0f, Rgb(from)), new GradientStop(1f, Rgb(to)) });
        var brush = _dc.CreateLinearGradientBrush(
            new LinearGradientBrushProperties { StartPoint = new Vector2(0, 0), EndPoint = new Vector2((float)Scene.CanvasWidth, (float)Scene.CanvasHeight) }, stops);
        _backgrounds[index] = brush;
        return brush;
    }

    public void Render(FrameState st, ID2D1Bitmap? screen, ID2D1Bitmap? phone)
    {
        var canvas = Matrix3x2.CreateScale(_scale);
        var full = new Rect(0, 0, (float)Scene.CanvasWidth, (float)Scene.CanvasHeight);

        // Pass 1: background and the two layers.
        _dc.Target = _layers;
        _dc.BeginDraw();
        _dc.Transform = canvas;
        _dc.FillRectangle(full, Background(st.Effects.BackgroundIndex));
        if (st.Screen.Visible) DrawScreenLayer(st, screen, canvas);
        if (st.Phone.Visible) DrawPhoneLayer(st, phone, canvas);
        _dc.EndDraw().CheckError();

        // Pass 2: blur regions, drawings, pointer effects.
        _dc.Target = _content;
        _dc.BeginDraw();
        _dc.Transform = Matrix3x2.Identity;
        _dc.DrawBitmap(_layers);
        bool anyBlur = false;
        foreach (var a in st.Annotations)
        {
            if (a.Kind != AnnotationKind.Blur || a.Points.Count < 2) continue;
            if (!anyBlur) { _blur.SetInput(0, _layers, true); anyBlur = true; }
            var r = RectD.FromPoints(a.Points[0], a.Points[1]);
            _dc.PushAxisAlignedClip(new Vortice.RawRectF((float)r.X * _scale, (float)r.Y * _scale, (float)r.Right * _scale, (float)r.Bottom * _scale), AntialiasMode.Aliased);
            _dc.DrawImage(_blur, InterpolationMode.Linear, CompositeMode.SourceOver);
            _dc.PopAxisAlignedClip();
        }
        _dc.Transform = canvas;
        var seen = new HashSet<Guid>();
        foreach (var a in st.Annotations)
            if (a.Kind != AnnotationKind.Blur) DrawAnnotation(a, seen);
        PruneStrokeCache(seen);
        if (st.CursorHighlight is { } cursor)
            _dc.FillEllipse(new Ellipse(V(cursor), 34, 34), Solid(Rgb(0xFFD60A, 0.38f)));
        foreach (var ripple in st.Ripples)
        {
            float t = (float)Math.Clamp(ripple.Age, 0, 1);
            float radius = 14 + 56 * (1 - (1 - t) * (1 - t));
            _dc.DrawEllipse(new Ellipse(V(ripple.Position), radius, radius), Solid(Rgb(0xFFFFFF, 0.9f * (1 - t))), 7 * (1 - t) + 2);
            _dc.DrawEllipse(new Ellipse(V(ripple.Position), radius, radius), Solid(Rgb(0x0A84FF, 0.9f * (1 - t))), 4 * (1 - t) + 1);
        }
        _dc.EndDraw().CheckError();

        // Pass 3: zoom, spotlight, magnifier.
        _dc.Target = _output;
        _dc.BeginDraw();
        _dc.Transform = canvas;
        var z = st.ZoomRegion;
        var source = new Rect((float)z.X * _scale, (float)z.Y * _scale, (float)z.Width * _scale, (float)z.Height * _scale);
        _dc.DrawBitmap(_content, full, 1f, st.IsZoomed ? InterpolationMode.HighQualityCubic : InterpolationMode.Linear, source, null);
        if (st.Pointer is { } pointer)
        {
            if (st.Spotlight)
            {
                _spotBrush.Center = V(pointer);
                _dc.FillRectangle(full, _spotBrush);
            }
            if (st.Magnifier)
            {
                const float radius = 190, power = 2f;
                var centre = V(pointer);
                using var lens = _gd.D2DFactory.CreateEllipseGeometry(new Ellipse(centre, radius, radius));
                _dc.PushLayer(new LayerParameters1
                {
                    ContentBounds = new Vortice.RawRectF(centre.X - radius, centre.Y - radius, centre.X + radius, centre.Y + radius),
                    GeometricMask = lens, MaskAntialiasMode = AntialiasMode.PerPrimitive, MaskTransform = Matrix3x2.Identity, Opacity = 1,
                }, null!);
                // Where the pointer sits on the un-zoomed content, in pixels.
                float sx = (float)(z.X + pointer.X * z.Width / Scene.CanvasWidth) * _scale;
                float sy = (float)(z.Y + pointer.Y * z.Height / Scene.CanvasHeight) * _scale;
                float half = radius / power * (float)(z.Width / Scene.CanvasWidth) * _scale;
                _dc.FillEllipse(new Ellipse(centre, radius, radius), Solid(Rgb(0x000000)));
                _dc.DrawBitmap(_content, new Rect(centre.X - radius, centre.Y - radius, radius * 2, radius * 2), 1f,
                    InterpolationMode.HighQualityCubic, new Rect(sx - half, sy - half, half * 2, half * 2), null);
                _dc.PopLayer();
                _dc.DrawEllipse(new Ellipse(centre, radius, radius), Solid(Rgb(0xFFFFFF, 0.95f)), 5);
                _dc.DrawEllipse(new Ellipse(centre, radius + 4, radius + 4), Solid(Rgb(0x000000, 0.35f)), 3);
            }
        }
        _dc.EndDraw().CheckError();
        _dc.Target = null;
    }

    private void DrawShadow(RectD box, float radius)
    {
        for (int i = 8; i >= 1; i--)
        {
            float grow = i * 5f;
            var r = new RectD(box.X - grow, box.Y - grow + 14, box.Width + 2 * grow, box.Height + 2 * grow);
            _dc.FillRoundedRectangle(new RoundedRectangle(RF(r), radius + grow, radius + grow), Solid(Rgb(0x000000, 0.045f)));
        }
    }

    /// <summary>Fills a rounded rectangle with a picture, scaled to cover exactly that rectangle.</summary>
    private void FillWithBitmap(ID2D1Bitmap bitmap, RectD dest, float radius, Matrix3x2 world)
    {
        var size = bitmap.PixelSize;
        _bitmapBrush.Bitmap = bitmap;
        // Sharper when a big screen is shown small; plain linear when close to 1:1.
        double shrink = size.Width / Math.Max(1.0, dest.Width * _scale);
        _bitmapBrush.InterpolationMode1 = shrink > 1.5 ? InterpolationMode.HighQualityCubic : InterpolationMode.Linear;
        _bitmapBrush.Transform = Matrix3x2.CreateScale((float)(dest.Width / size.Width), (float)(dest.Height / size.Height)) *
                                 Matrix3x2.CreateTranslation((float)dest.X, (float)dest.Y);
        _dc.FillRoundedRectangle(new RoundedRectangle(RF(dest), radius, radius), _bitmapBrush);
    }

    private void DrawHint(string text, RectD box, float fontScale = 1)
    {
        if (string.IsNullOrEmpty(text)) return;
        using var layout = _gd.DWrite.CreateTextLayout(text, _hintFormat, (float)box.Width - 24, (float)box.Height);
        if (fontScale != 1) layout.SetFontSize(34 * fontScale, new TextRange { StartPosition = 0, Length = (uint)text.Length });
        _dc.DrawTextLayout(new Vector2((float)box.X + 12, (float)box.Y), layout, Solid(Rgb(0xFFFFFF, 0.55f)));
    }

    private void DrawSimulationBadge(FrameState st, RectD box, bool atBottom = false)
    {
        double bw = Math.Min(box.Width - 24, 260);
        var badge = atBottom ? new RectD(box.Center.X - bw / 2, box.Bottom - 70, bw, 44) : new RectD(box.X + 12, box.Y + 12, bw, 44);
        _dc.FillRoundedRectangle(new RoundedRectangle(RF(badge), 10, 10), Solid(Rgb(0xFF453A, 0.92f)));
        using var layout = _gd.DWrite.CreateTextLayout(st.SimulationLabel, _hintFormat, (float)badge.Width, (float)badge.Height);
        layout.SetFontSize(22, new TextRange { StartPosition = 0, Length = (uint)st.SimulationLabel.Length });
        _dc.DrawTextLayout(new Vector2((float)badge.X, (float)badge.Y), layout, Solid(Rgb(0xFFFFFF)));
    }

    private void DrawScreenLayer(FrameState st, ID2D1Bitmap? bitmap, Matrix3x2 canvas)
    {
        var l = st.Screen;
        var world = Matrix3x2.CreateRotation((float)(l.RotationDeg * Math.PI / 180), V(l.Center)) * canvas;
        _dc.Transform = world;
        var box = new RectD(l.CenterX - l.Width / 2, l.CenterY - l.Height / 2, l.Width, l.Height);
        const float radius = 14;
        if (st.Effects.Shadows) DrawShadow(box, radius);
        if (bitmap != null)
        {
            _dc.FillRoundedRectangle(new RoundedRectangle(RF(box), radius, radius), Solid(Rgb(0x000000)));
            FillWithBitmap(bitmap, box, radius, world);
            if (st.ScreenIsSimulated) DrawSimulationBadge(st, box);
        }
        else
        {
            _dc.FillRoundedRectangle(new RoundedRectangle(RF(box), radius, radius), Solid(Rgb(0x0B0D12, 0.92f)));
            _dc.DrawRoundedRectangle(new RoundedRectangle(RF(box), radius, radius), Solid(Rgb(0xFFFFFF, 0.14f)), 2);
            if (!st.Recording) DrawHint(st.ScreenHint, box);
        }
        _dc.Transform = canvas;
    }

    private void DrawPhoneLayer(FrameState st, ID2D1Bitmap? bitmap, Matrix3x2 canvas)
    {
        var l = st.Phone;
        var world = Matrix3x2.CreateRotation((float)(l.RotationDeg * Math.PI / 180), V(l.Center)) * canvas;
        _dc.Transform = world;
        var body = new RectD(l.CenterX - l.Width / 2, l.CenterY - l.Height / 2, l.Width, l.Height);
        double shortSide = Math.Min(l.Width, l.Height);
        float bodyRadius = (float)(shortSide * 0.165);
        double bezel = shortSide * 0.0324, rim = shortSide * 0.012;
        var screen = new RectD(body.X + bezel, body.Y + bezel, body.Width - 2 * bezel, body.Height - 2 * bezel);
        float screenRadius = (float)(bodyRadius - bezel);

        if (st.Effects.Shadows) DrawShadow(body, bodyRadius);

        // Side buttons (drawn first so the body covers their inner half).
        double t = shortSide * 0.014, len = shortSide;
        var btn = Solid(Rgb(0x48484C));
        void Button(double along, double length, bool farSide)
        {
            RectD r = l.Landscape
                ? new RectD(body.X + along * len / 0.478, farSide ? body.Y - t * 0.6 : body.Bottom - t * 0.4, length * len, t)
                : new RectD(farSide ? body.Right - t * 0.4 : body.X - t * 0.6, body.Y + along * len / 0.478, t, length * len);
            _dc.FillRoundedRectangle(new RoundedRectangle(RF(r), (float)t / 2, (float)t / 2), btn);
        }
        Button(0.165, 0.075, false); Button(0.235, 0.13, false); Button(0.305, 0.13, false); Button(0.27, 0.21, true);

        // Metal frame, black glass border, screen.
        _bodyBrush.Transform = Matrix3x2.CreateScale((float)body.Width, (float)body.Height) * Matrix3x2.CreateTranslation(V(body.Center));
        _dc.FillRoundedRectangle(new RoundedRectangle(RF(body), bodyRadius, bodyRadius), _bodyBrush);
        var glass = new RectD(body.X + rim, body.Y + rim, body.Width - 2 * rim, body.Height - 2 * rim);
        _dc.FillRoundedRectangle(new RoundedRectangle(RF(glass), (float)(bodyRadius - rim), (float)(bodyRadius - rim)), Solid(Rgb(0x050506)));

        if (bitmap != null)
        {
            var size = bitmap.PixelSize;
            var fitted = RectD.Fit((double)size.Width / size.Height, screen);
            bool exact = Math.Abs(fitted.Width - screen.Width) < screen.Width * 0.03 && Math.Abs(fitted.Height - screen.Height) < screen.Height * 0.03;
            FillWithBitmap(bitmap, exact ? screen : fitted, exact ? screenRadius : 4, world);
            if (st.PhoneIsSimulated) DrawSimulationBadge(st, screen, atBottom: true);
        }
        else
        {
            _dc.FillRoundedRectangle(new RoundedRectangle(RF(screen), screenRadius, screenRadius), Solid(Rgb(0x101216)));
            if (!st.Recording) DrawHint(st.PhoneHint, screen, (float)Math.Clamp(shortSide / 420, 0.45, 1.2));
        }

        // Dynamic Island.
        double iw = shortSide * 0.29, ih = shortSide * 0.085, inset = shortSide * 0.03;
        var island = l.Landscape
            ? new RectD(screen.X + inset, screen.Center.Y - iw / 2, ih, iw)
            : new RectD(screen.Center.X - iw / 2, screen.Y + inset, iw, ih);
        _dc.FillRoundedRectangle(new RoundedRectangle(RF(island), (float)ih / 2, (float)ih / 2), Solid(Rgb(0x000000)));
        // Soft highlight on the glass edge.
        _dc.DrawRoundedRectangle(new RoundedRectangle(RF(glass), (float)(bodyRadius - rim), (float)(bodyRadius - rim)), Solid(Rgb(0xFFFFFF, 0.08f)), 1.5f);
        _dc.Transform = canvas;
    }

    private IDWriteTextFormat TextFormat(double size)
    {
        int key = (int)Math.Round(size);
        if (_textFormats.TryGetValue(key, out var f)) return f;
        f = _gd.DWrite.CreateTextFormat("Segoe UI", FontWeight.Bold, FontStyle.Normal, key);
        f.WordWrapping = WordWrapping.NoWrap;
        _textFormats[key] = f;
        return f;
    }

    private void DrawAnnotation(Annotation a, HashSet<Guid> seen)
    {
        if (a.Points.Count == 0) return;
        float w = (float)a.Thickness;
        switch (a.Kind)
        {
            case AnnotationKind.Pen:
            case AnnotationKind.Highlighter:
            {
                bool hi = a.Kind == AnnotationKind.Highlighter;
                var brush = Solid(Argb(a.Color, hi ? 0.42f : 1f));
                float width = hi ? w * 3.2f : w;
                if (a.Points.Count == 1) { _dc.FillEllipse(new Ellipse(V(a.Points[0]), width / 2, width / 2), brush); break; }
                seen.Add(a.Id);
                if (!_strokeCache.TryGetValue(a.Id, out var cached) || cached.count != a.Points.Count)
                {
                    cached.geometry?.Dispose();
                    var geometry = _gd.D2DFactory.CreatePathGeometry();
                    using (var sink = geometry.Open())
                    {
                        sink.BeginFigure(V(a.Points[0]), FigureBegin.Hollow);
                        for (int i = 1; i < a.Points.Count - 1; i++)
                        {
                            var mid = (a.Points[i] + a.Points[i + 1]) * 0.5;
                            sink.AddQuadraticBezier(new QuadraticBezierSegment { Point1 = V(a.Points[i]), Point2 = V(mid) });
                        }
                        sink.AddLine(V(a.Points[^1]));
                        sink.EndFigure(FigureEnd.Open);
                        sink.Close();
                    }
                    cached = (a.Points.Count, geometry);
                    _strokeCache[a.Id] = cached;
                }
                _dc.DrawGeometry(cached.geometry, brush, width, _round);
                break;
            }
            case AnnotationKind.Arrow when a.Points.Count > 1:
            {
                var from = a.Points[0]; var to = a.Points[1];
                var d = to - from;
                if (d.Length < 1) break;
                var dir = d * (1 / d.Length);
                var normal = new Vec2(-dir.Y, dir.X);
                double head = Math.Min(d.Length, 14 + w * 3.2);
                var neck = to - dir * head;
                var brush = Solid(Argb(a.Color));
                _dc.DrawLine(V(from), V(neck + dir * 2), brush, w, _round);
                using var geometry = _gd.D2DFactory.CreatePathGeometry();
                using (var sink = geometry.Open())
                {
                    sink.BeginFigure(V(to), FigureBegin.Filled);
                    sink.AddLine(V(neck + normal * (head * 0.55)));
                    sink.AddLine(V(neck - normal * (head * 0.55)));
                    sink.EndFigure(FigureEnd.Closed);
                    sink.Close();
                }
                _dc.FillGeometry(geometry, brush);
                break;
            }
            case AnnotationKind.Rectangle when a.Points.Count > 1:
                _dc.DrawRoundedRectangle(new RoundedRectangle(RF(RectD.FromPoints(a.Points[0], a.Points[1])), 6, 6), Solid(Argb(a.Color)), w, _round);
                break;
            case AnnotationKind.Ellipse when a.Points.Count > 1:
            {
                var r = RectD.FromPoints(a.Points[0], a.Points[1]);
                _dc.DrawEllipse(new Ellipse(V(r.Center), (float)r.Width / 2, (float)r.Height / 2), Solid(Argb(a.Color)), w, _round);
                break;
            }
            case AnnotationKind.Text when !string.IsNullOrEmpty(a.Text):
            {
                const float max = 4000;
                using var layout = _gd.DWrite.CreateTextLayout(a.Text, TextFormat(a.FontSize), max, 400);
                bool rtl = a.IsRtlText;
                layout.ReadingDirection = rtl ? ReadingDirection.RightToLeft : ReadingDirection.LeftToRight;
                var origin = new Vector2((float)a.Points[0].X - (rtl ? max : 0), (float)a.Points[0].Y);
                _dc.DrawTextLayout(origin + new Vector2(2, 3), layout, Solid(Rgb(0x000000, 0.55f)));
                _dc.DrawTextLayout(origin, layout, Solid(Argb(a.Color)));
                break;
            }
        }
    }

    private void PruneStrokeCache(HashSet<Guid> seen)
    {
        if (_strokeCache.Count == seen.Count) return;
        foreach (var id in _strokeCache.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _strokeCache[id].geometry.Dispose();
            _strokeCache.Remove(id);
        }
    }

    /// <summary>Where the picture sits inside a preview area of the given size (pixels).</summary>
    public static RectD PreviewRect(double areaWidth, double areaHeight)
    {
        const double margin = 10;
        return RectD.Fit(Scene.CanvasWidth / Scene.CanvasHeight,
            new RectD(margin, margin, Math.Max(1, areaWidth - 2 * margin), Math.Max(1, areaHeight - 2 * margin)));
    }

    /// <summary>Draws the finished picture into the preview window, plus the selection handles (never recorded).</summary>
    public void DrawPreview(ID2D1Bitmap1 backBuffer, int areaWidth, int areaHeight, FrameState st, Color4 windowColor)
    {
        var fit = PreviewRect(areaWidth, areaHeight);
        _dc.Target = backBuffer;
        _dc.BeginDraw();
        _dc.Transform = Matrix3x2.Identity;
        _dc.Clear(windowColor);
        double shrink = Width / Math.Max(1, fit.Width);
        _dc.DrawBitmap(_output, R(fit), 1f, shrink > 1.5 ? InterpolationMode.HighQualityCubic : InterpolationMode.Linear, null, null);
        _dc.DrawRectangle(R(fit), Solid(Rgb(0xFFFFFF, 0.10f)), 1);

        if (st.Selected is { } kind)
        {
            var layer = kind == LayerKind.Screen ? st.Screen : st.Phone;
            if (layer.Visible)
            {
                var z = st.ZoomRegion;
                Vector2 Map(Vec2 p) => new(
                    (float)(fit.X + (p.X - z.X) / z.Width * fit.Width),
                    (float)(fit.Y + (p.Y - z.Y) / z.Height * fit.Height));
                var corners = new[] { LayerHandle.TopLeft, LayerHandle.TopRight, LayerHandle.BottomRight, LayerHandle.BottomLeft }
                    .Select(h => Map(layer.HandlePosition(h))).ToArray();
                var accent = Rgb(0x0A84FF);
                for (int i = 0; i < 4; i++) _dc.DrawLine(corners[i], corners[(i + 1) % 4], Solid(accent), 2f, _round);
                var topMid = (corners[0] + corners[1]) / 2;
                var rotate = Map(layer.HandlePosition(LayerHandle.Rotate));
                _dc.DrawLine(topMid, rotate, Solid(accent), 2f, _round);
                foreach (var c in corners.Append(rotate))
                {
                    _dc.FillEllipse(new Ellipse(c, 8, 8), Solid(Rgb(0xFFFFFF)));
                    _dc.DrawEllipse(new Ellipse(c, 8, 8), Solid(accent), 2.5f);
                }
            }
        }
        _dc.EndDraw().CheckError();
        _dc.Target = null;
    }

    public void Dispose()
    {
        _dc.Target = null;
        foreach (var s in _strokeCache.Values) s.geometry.Dispose();
        foreach (var f in _textFormats.Values) f.Dispose();
        foreach (var b in _backgrounds) b?.Dispose();
        _hintFormat.Dispose(); _bodyBrush.Dispose(); _spotBrush.Dispose(); _blur.Dispose(); _round.Dispose();
        _bitmapBrush.Dispose(); _brush.Dispose();
        _layers.Dispose(); _content.Dispose(); _output.Dispose();
        _layersTex.Dispose(); _contentTex.Dispose(); _outputTex.Dispose();
    }
}
