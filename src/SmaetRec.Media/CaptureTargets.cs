using System.Text;
using System.Diagnostics;
using static SmaetRec.Media.Native;

namespace SmaetRec.Media;

public enum CaptureKind { Monitor, Window }

/// <summary>Something on this computer that can be shown: a whole screen or one window.</summary>
public sealed record CaptureTarget(CaptureKind Kind, IntPtr Handle, string Title, int Left, int Top, int Width, int Height, bool IsPrimary)
{
    /// <summary>Owning process name for window targets; empty for monitor targets.</summary>
    public string ProcessName { get; init; } = "";

    public bool IsLikely3uToolsWindow =>
        Kind == CaptureKind.Window &&
        (Title.Contains("Realtime Screen", StringComparison.OrdinalIgnoreCase) ||
         Title.Contains("Real-time Screen", StringComparison.OrdinalIgnoreCase) ||
         Title.Contains("3uTools", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(ProcessName, "3uTools", StringComparison.OrdinalIgnoreCase));

    /// <summary>3uTools windows are ordered with a titled Realtime Screen viewer first.</summary>
    public static List<CaptureTarget> ThreeUToolsWindows() =>
        Windows().Where(window => window.IsLikely3uToolsWindow)
            .OrderByDescending(window =>
                window.Title.Contains("Realtime Screen", StringComparison.OrdinalIgnoreCase) ||
                window.Title.Contains("Real-time Screen", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(window => string.Equals(window.ProcessName, "3uTools", StringComparison.OrdinalIgnoreCase))
            .ToList();
    public static List<CaptureTarget> Monitors()
    {
        var list = new List<CaptureTarget>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, _, _, _) =>
        {
            var info = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(h, ref info))
                list.Add(new CaptureTarget(CaptureKind.Monitor, h, info.szDevice, info.rcMonitor.Left, info.rcMonitor.Top,
                    info.rcMonitor.Width, info.rcMonitor.Height, (info.dwFlags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        return list.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Left).ToList();
    }

    public static CaptureTarget? PrimaryMonitor() => Monitors().FirstOrDefault();

    /// <summary>Ordinary visible application windows, front to back, without this app's own windows.</summary>
    public static List<CaptureTarget> Windows()
    {
        var list = new List<CaptureTarget>();
        uint self = (uint)Environment.ProcessId;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || GetWindowTextLength(hwnd) == 0) return true;
            long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            if ((ex & WS_EX_TOOLWINDOW) != 0 || (ex & WS_EX_NOACTIVATE) != 0) return true;
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return true;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == self) return true;
            var bounds = WindowBounds(hwnd);
            if (bounds.Width < 80 || bounds.Height < 60) return true;
            var sb = new StringBuilder(260);
            GetWindowText(hwnd, sb, sb.Capacity);
            string processName = "";
            try
            {
                using var process = Process.GetProcessById((int)pid);
                processName = process.ProcessName;
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
            list.Add(new CaptureTarget(CaptureKind.Window, hwnd, sb.ToString(), bounds.Left, bounds.Top, bounds.Width, bounds.Height, false)
            {
                ProcessName = processName
            });
            return true;
        }, IntPtr.Zero);
        return list;
    }

    internal static RECT WindowBounds(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, 16) != 0) GetWindowRect(hwnd, out r);
        return r;
    }

    /// <summary>Current position on the desktop (windows move; monitors do not).</summary>
    public (int left, int top, int width, int height) CurrentBounds()
    {
        if (Kind == CaptureKind.Monitor) return (Left, Top, Width, Height);
        var r = WindowBounds(Handle);
        return (r.Left, r.Top, r.Width, r.Height);
    }

    public bool StillExists => Kind == CaptureKind.Monitor ? Monitors().Any(m => m.Handle == Handle) : IsWindow(Handle);
}
