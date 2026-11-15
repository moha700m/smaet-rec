using System.Runtime.InteropServices;
using static SmaetRec.Media.Native;

namespace SmaetRec.Media;

/// <summary>Reports mouse clicks anywhere on the desktop. Only installed while click effects are switched on.</summary>
public sealed class MouseWatcher : IDisposable
{
    private IntPtr _hook;
    private readonly HookProc _proc; // keep the delegate alive
    public event Action<int, int>? Clicked;

    public MouseWatcher() => _proc = Callback;

    public bool IsActive => _hook != IntPtr.Zero;

    /// <summary>Must be called on a thread with a message loop (the UI thread).</summary>
    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && ((int)wParam == WM_LBUTTONDOWN || (int)wParam == WM_RBUTTONDOWN))
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            Clicked?.Invoke(data.pt.X, data.pt.Y);
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public static (int x, int y)? CursorPosition() => GetCursorPos(out var p) ? (p.X, p.Y) : null;

    public void Dispose() => Stop();
}
