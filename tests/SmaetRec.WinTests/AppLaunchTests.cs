using System.Diagnostics;
using System.Runtime.InteropServices;
using SmaetRec.Core;
using Xunit.Abstractions;
using static SmaetRec.WinTests.TestSupport;

namespace SmaetRec.WinTests;

/// <summary>Starts the real application, takes a picture of the screen and checks it did not crash.</summary>
[Collection("engine")]
public class AppLaunchTests(ITestOutputHelper output)
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);

    private static string? FindApp()
    {
        // tests/SmaetRec.WinTests/bin/<cfg>/<tfm>/  ->  src/SmaetRec.App/bin/<cfg>/<tfm>/SmaetRec.exe
        var dir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        string tfm = dir.Name, cfg = dir.Parent!.Name;
        var root = dir.Parent.Parent!.Parent!.Parent!.Parent!.FullName;
        var exe = Path.Combine(root, "src", "SmaetRec.App", "bin", cfg, tfm, "SmaetRec.exe");
        return File.Exists(exe) ? exe : null;
    }

    private static (byte[] pixels, int width, int height) GrabScreen()
    {
        int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen);
        var header = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        IntPtr dib = CreateDIBSection(screen, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
        IntPtr old = SelectObject(mem, dib);
        BitBlt(mem, 0, 0, w, h, screen, 0, 0, 0x00CC0020 | 0x40000000);
        var pixels = new byte[w * h * 4];
        Marshal.Copy(bits, pixels, 0, pixels.Length);
        SelectObject(mem, old); DeleteObject(dib); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
        return (pixels, w, h);
    }

    [SkippableTheory]
    [InlineData("app-arabic-arrange", "--dev-simulated-source --lang ar")]
    [InlineData("app-english-draw", "--dev-simulated-source --lang en --dev-mode draw")]
    [InlineData("app-arabic-effects", "--dev-simulated-source --lang ar --dev-mode effects")]
    [InlineData("app-arabic-connect", "--dev-simulated-source --lang ar --dev-open connect")]
    [InlineData("app-english-connect", "--dev-simulated-source --lang en --dev-open connect")]
    [InlineData("app-arabic-settings", "--dev-simulated-source --lang ar --dev-open settings")]
    [InlineData("app-english-screens", "--dev-simulated-source --lang en --dev-open screens")]
    [InlineData("app-real-start", "--lang ar")]
    public void App_starts_and_shows_its_window(string name, string arguments)
    {
        var exe = FindApp();
        Skip.If(exe == null, "The application was not built next to the tests");
        string logBefore = File.Exists(AppPaths.LogFile) ? File.ReadAllText(AppPaths.LogFile) : "";
        using var process = Process.Start(new ProcessStartInfo(exe!, arguments + " --dev-allow-self-capture") { UseShellExecute = false })!;
        try
        {
            Assert.True(WaitFor(() => { process.Refresh(); return process.HasExited || process.MainWindowHandle != IntPtr.Zero; }, 30000), "no window appeared");
            Assert.False(process.HasExited, "the application exited right after starting");
            SetForegroundWindow(process.MainWindowHandle);
            Thread.Sleep(3500); // let the preview, receiver and dialogs settle
            process.Refresh();
            Assert.False(process.HasExited, "the application crashed after starting");
            var (pixels, w, h) = GrabScreen();
            SavePng(name + ".png", pixels, w, h);
            output.WriteLine($"screen {w}x{h}, working set {process.WorkingSet64 / (1024 * 1024)} MB, threads {process.Threads.Count}");
        }
        finally
        {
            if (!process.HasExited) { process.CloseMainWindow(); if (!process.WaitForExit(4000)) process.Kill(true); }
        }
        string log = File.Exists(AppPaths.LogFile) ? File.ReadAllText(AppPaths.LogFile) : "";
        string added = log.Length >= logBefore.Length ? log[logBefore.Length..] : log;
        output.WriteLine(added);
        Assert.DoesNotContain("Unhandled", added);
        Assert.DoesNotContain("FATAL", added);
        Assert.DoesNotContain("ERROR Render", added);
        if (!arguments.Contains("simulated")) Assert.Contains("AirPlay receiver", added);
    }
}
