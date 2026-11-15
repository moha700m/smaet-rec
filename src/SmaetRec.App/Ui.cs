using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SmaetRec.App;

/// <summary>Attached properties used by the button templates: an icon (font glyph or vector path) above a short label.</summary>
public static class Ui
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached("Glyph", typeof(string), typeof(Ui), new PropertyMetadata(""));
    public static string GetGlyph(DependencyObject o) => (string)o.GetValue(GlyphProperty);
    public static void SetGlyph(DependencyObject o, string v) => o.SetValue(GlyphProperty, v);

    public static readonly DependencyProperty GeometryProperty = DependencyProperty.RegisterAttached("Geometry", typeof(Geometry), typeof(Ui), new PropertyMetadata(null));
    public static Geometry? GetGeometry(DependencyObject o) => (Geometry?)o.GetValue(GeometryProperty);
    public static void SetGeometry(DependencyObject o, Geometry? v) => o.SetValue(GeometryProperty, v);

    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached("Icon", typeof(object), typeof(Ui), new PropertyMetadata(null));
    public static object? GetIcon(DependencyObject o) => o.GetValue(IconProperty);
    public static void SetIcon(DependencyObject o, object? v) => o.SetValue(IconProperty, v);

    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached("Key", typeof(string), typeof(Ui), new PropertyMetadata(""));
    public static string GetKey(DependencyObject o) => (string)o.GetValue(KeyProperty);
    public static void SetKey(DependencyObject o, string v) => o.SetValue(KeyProperty, v);

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.RegisterAttached("IconSize", typeof(double), typeof(Ui), new PropertyMetadata(22.0));
    public static double GetIconSize(DependencyObject o) => (double)o.GetValue(IconSizeProperty);
    public static void SetIconSize(DependencyObject o, double v) => o.SetValue(IconSizeProperty, v);

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    /// <summary>Dark title bar to match the app.</summary>
    public static void UseDarkTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        int on = 1;
        DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
    }

    /// <summary>Keeps this window out of screen captures, so recording the screen does not show the studio inside itself.</summary>
    public static void ExcludeFromCapture(Window window, bool exclude)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        SetWindowDisplayAffinity(hwnd, exclude ? 0x11u : 0u);
    }

    public static SolidColorBrush Brush(uint argb)
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        b.Freeze();
        return b;
    }
}
