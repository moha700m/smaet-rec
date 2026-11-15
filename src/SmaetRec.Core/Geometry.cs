namespace SmaetRec.Core;

public readonly record struct Vec2(double X, double Y)
{
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public double Length => Math.Sqrt(X * X + Y * Y);

    public Vec2 Rotate(double degrees)
    {
        double r = degrees * Math.PI / 180.0, c = Math.Cos(r), s = Math.Sin(r);
        return new Vec2(X * c - Y * s, X * s + Y * c);
    }

    public static double DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        double len2 = ab.X * ab.X + ab.Y * ab.Y;
        if (len2 < 1e-9) return (p - a).Length;
        double t = Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2, 0, 1);
        return (p - (a + ab * t)).Length;
    }
}

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public Vec2 Center => new(X + Width / 2, Y + Height / 2);
    public bool Contains(Vec2 p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;

    /// <summary>Largest rectangle with the given aspect (w/h) centred inside <paramref name="bounds"/>.</summary>
    public static RectD Fit(double aspect, RectD bounds)
    {
        if (aspect <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return bounds;
        double w = bounds.Width, h = w / aspect;
        if (h > bounds.Height) { h = bounds.Height; w = h * aspect; }
        return new RectD(bounds.X + (bounds.Width - w) / 2, bounds.Y + (bounds.Height - h) / 2, w, h);
    }

    public static RectD FromPoints(Vec2 a, Vec2 b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
