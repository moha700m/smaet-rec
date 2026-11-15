using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SmaetRec.App;

/// <summary>
/// A native child window inside the WPF layout. The engine draws the live preview straight into it with the GPU,
/// and mouse input on it is passed on as simple events (coordinates in physical pixels).
/// </summary>
public sealed class PreviewHost : HwndHost
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize, style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm;
    }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct TRACKMOUSEEVENT { public int cbSize, dwFlags; public IntPtr hwndTrack; public int dwHoverTime; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string title, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instance, int id);
    [DllImport("user32.dll")] private static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("user32.dll")] private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT e);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT p);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref POINT p);
    [DllImport("user32.dll")] private static extern bool ValidateRect(IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);

    private const string ClassName = "SmaetRecPreview";
    private static bool _registered;
    private IntPtr _hwnd;
    private bool _tracking;

    public IntPtr Hwnd => _hwnd;
    public bool Crosshair { get; set; }
    public event Action<int, int>? PointerDown, PointerMove, PointerUp;
    public event Action<int, int, int>? Wheel;
    public event Action? PointerLeave, Repaint;
    public event Action<int, int>? PixelSizeChanged;

    public (int width, int height) PixelSize
    {
        get { if (_hwnd == IntPtr.Zero || !GetClientRect(_hwnd, out var r)) return (1, 1); return (Math.Max(1, r.Right), Math.Max(1, r.Bottom)); }
    }

    public (int x, int y) ToScreen(int x, int y)
    {
        var p = new POINT { X = x, Y = y };
        ClientToScreen(_hwnd, ref p);
        return (p.X, p.Y);
    }

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        if (!_registered)
        {
            var wc = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(), style = 0x0003,
                lpfnWndProc = GetProcAddress(GetModuleHandle("user32.dll"), "DefWindowProcW"),
                hInstance = GetModuleHandle(null), hCursor = LoadCursor(IntPtr.Zero, 32512), lpszClassName = ClassName,
            };
            if (RegisterClassEx(ref wc) == 0 && Marshal.GetLastWin32Error() != 1410) throw new InvalidOperationException("Could not create the preview window");
            _registered = true;
        }
        _hwnd = CreateWindowEx(0, ClassName, "", 0x40000000 | 0x10000000 | 0x02000000, 0, 0, 16, 16, parent.Handle, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("Could not create the preview window");
        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DestroyWindow(hwnd.Handle);
        _hwnd = IntPtr.Zero;
    }

    private static int X(IntPtr lParam) => (short)(lParam.ToInt64() & 0xFFFF);
    private static int Y(IntPtr lParam) => (short)((lParam.ToInt64() >> 16) & 0xFFFF);

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case 0x0014: // WM_ERASEBKGND
                handled = true;
                return new IntPtr(1);
            case 0x000F: // WM_PAINT
                ValidateRect(hwnd, IntPtr.Zero);
                Repaint?.Invoke();
                handled = true;
                return IntPtr.Zero;
            case 0x0005: // WM_SIZE
                PixelSizeChanged?.Invoke(Math.Max(1, X(lParam)), Math.Max(1, Y(lParam)));
                break;
            case 0x0020: // WM_SETCURSOR
                if ((lParam.ToInt64() & 0xFFFF) == 1)
                {
                    SetCursor(LoadCursor(IntPtr.Zero, Crosshair ? 32515 : 32512));
                    handled = true;
                    return new IntPtr(1);
                }
                break;
            case 0x0201: // WM_LBUTTONDOWN
                SetCapture(hwnd);
                PointerDown?.Invoke(X(lParam), Y(lParam));
                handled = true;
                break;
            case 0x0200: // WM_MOUSEMOVE
                if (!_tracking)
                {
                    var t = new TRACKMOUSEEVENT { cbSize = Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = 2, hwndTrack = hwnd };
                    _tracking = TrackMouseEvent(ref t);
                }
                PointerMove?.Invoke(X(lParam), Y(lParam));
                handled = true;
                break;
            case 0x0202: // WM_LBUTTONUP
                ReleaseCapture();
                PointerUp?.Invoke(X(lParam), Y(lParam));
                handled = true;
                break;
            case 0x02A3: // WM_MOUSELEAVE
                _tracking = false;
                PointerLeave?.Invoke();
                break;
            case 0x020A: // WM_MOUSEWHEEL (screen coordinates)
            {
                var p = new POINT { X = X(lParam), Y = Y(lParam) };
                ScreenToClient(hwnd, ref p);
                Wheel?.Invoke(p.X, p.Y, (short)((wParam.ToInt64() >> 16) & 0xFFFF));
                handled = true;
                break;
            }
        }
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }
}
