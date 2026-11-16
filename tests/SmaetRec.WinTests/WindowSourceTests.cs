using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SmaetRec.Core;
using SmaetRec.Media;
using Xunit.Abstractions;
using static SmaetRec.WinTests.TestSupport;

namespace SmaetRec.WinTests;

/// <summary>
/// The external-window path used for 3uTools Realtime Screen, exercised with a real window that this test owns:
/// live pictures reach the scene, drawings land on top, the recording contains both, and minimising or closing the
/// window is noticed. This proves the mechanism; it is not a test of 3uTools itself or of a real iPhone.
/// </summary>
[Collection("engine")]
public class WindowSourceTests(ITestOutputHelper output)
{
    /// <summary>A real top-level window on its own UI thread, with a colour that can be changed (so frames keep coming).</summary>
    private sealed class LiveWindow : IDisposable
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new();
        private Window? _window;
        private Dispatcher? _dispatcher;
        public IntPtr Handle { get; private set; }

        public LiveWindow(string title, Color color)
        {
            _thread = new Thread(() =>
            {
                _window = new Window
                {
                    Title = title, Width = 420, Height = 640, Left = 80, Top = 60, WindowStartupLocation = WindowStartupLocation.Manual,
                    Topmost = true, Background = new SolidColorBrush(color), ResizeMode = ResizeMode.CanResize,
                    Content = new Border { Background = new SolidColorBrush(color) },
                };
                _window.Show();
                Handle = new WindowInteropHelper(_window).Handle;
                _dispatcher = Dispatcher.CurrentDispatcher;
                _ready.Set();
                Dispatcher.Run();
            }) { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            Assert.True(_ready.Wait(15000), "test window did not open");
        }

        public void Do(Action<Window> action) => _dispatcher!.Invoke(() => action(_window!));
        public void SetColor(Color color) => Do(w => { w.Background = new SolidColorBrush(color); ((Border)w.Content).Background = new SolidColorBrush(color); });

        public void Dispose()
        {
            try { _dispatcher?.Invoke(() => { _window?.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }); }
            catch (Exception e) when (e is InvalidOperationException or TaskCanceledException) { }
            _thread.Join(5000);
        }
    }

    private static CaptureTarget TargetFor(LiveWindow window, string title) =>
        new(CaptureKind.Window, window.Handle, title, 80, 60, 420, 640, false) { ProcessName = "3uTools" };

    [SkippableFact]
    public async Task A_live_window_feeds_the_scene_and_the_recording_and_losing_it_is_noticed()
    {
        Skip.If(CaptureTarget.PrimaryMonitor() == null, "This machine has no desktop to show a window on");
        const string title = "Realtime Screen";
        using var window = new LiveWindow(title, Color.FromRgb(20, 200, 60));
        var target = TargetFor(window, title);
        Assert.True(target.IsLikely3uToolsWindow && target.IsRealtimeScreenViewer);
        Assert.True(target.StillExists);

        var scene = new Scene();
        scene.Effects.BackgroundIndex = 6;
        lock (scene.Sync) scene.Phone.Visible = false;
        using var engine = new StudioEngine(scene);
        string? startError = null; bool reported = false;
        CaptureTarget? lost = null;
        engine.ScreenChanged += (_, e) => { startError = e; reported = true; };
        engine.ScreenSourceLost += t => lost = t;
        engine.ShowScreen(target);
        Assert.True(WaitFor(() => reported), "the engine never answered");
        Assert.True(engine.ScreenActive, "window capture did not start: " + startError);

        // The window's own colour must show up in the finished picture (centre of the screen layer).
        (byte b, byte g, byte r) Centre()
        {
            var (px, w, _) = engine.Snapshot();
            return Pixel(px, w, (int)scene.Screen.CenterX, (int)scene.Screen.CenterY);
        }
        bool green = WaitFor(() => { var c = Centre(); return c.g > 150 && c.r < 90 && c.b < 120; }, 10000);
        var first = engine.Snapshot();
        SavePng("window-source-green.png", first.pixels, first.width, first.height);
        Skip.If(!green && Centre() is { b: 0, g: 0, r: 0 }, "Window capture returns only black on this machine (no interactive desktop)");
        Assert.True(green, $"the window's colour did not reach the scene: {Centre()}");

        // Live, not a still: change the window and the scene follows.
        window.SetColor(Color.FromRgb(220, 40, 200));
        Assert.True(WaitFor(() => { var c = Centre(); return c.r > 150 && c.b > 130 && c.g < 110; }, 10000), $"the scene did not follow the window: {Centre()}");

        // Health is measured from what really arrives.
        Assert.True(WaitFor(() => engine.ScreenHealth.Active && engine.ScreenHealth.Width > 0, 5000));
        output.WriteLine("health: " + engine.ScreenHealth);
        Assert.False(engine.ScreenHealth.Minimized);
        Assert.NotEqual(true, engine.ScreenHealth.Black);
        Assert.False(string.IsNullOrEmpty(engine.ScreenHealth.Method));

        // Drawings go on top of the window's picture, and the recording has both.
        var centre = new Vec2(scene.Screen.CenterX, scene.Screen.CenterY);
        scene.Annotations.Add(new Annotation
        {
            Kind = AnnotationKind.Rectangle, Color = 0xFFFFFF00, Thickness = 40,
            Points = { new Vec2(centre.X - 20, centre.Y - 20), new Vec2(centre.X + 20, centre.Y + 20) },
        });
        var folder = Path.Combine(Artifacts, "recordings");
        var status = engine.StartRecording(Settings(folder), out var recordError);
        Assert.True(status != null, "recording did not start: " + recordError);
        // Keep the window changing while recording, as a phone screen would.
        for (int i = 0; i < 8; i++) { window.SetColor(Color.FromRgb((byte)(200 + i * 5), 40, (byte)(200 - i * 10))); Thread.Sleep(250); }
        var result = await engine.StopRecordingAsync();
        Assert.True(result is { Error: null }, "recording failed: " + result?.Error);
        var info = Mp4Info.Read(result!.Path);
        output.WriteLine($"recorded {info.VideoFrames} frames, {info.DurationSeconds:F2}s, {info.Width}x{info.Height}");
        Assert.True(info.VideoFrames >= 30 && info.FirstFrameBgra != null);
        SavePng("window-source-recording.png", info.FirstFrameBgra!, info.Width, info.Height);
        double k = info.Width / Scene.CanvasWidth;
        var drawn = Pixel(info.FirstFrameBgra!, info.Width, (int)(centre.X * k), (int)(centre.Y * k));
        Assert.True(drawn.r > 170 && drawn.g > 170 && drawn.b < 110, $"the drawing is missing from the recording: {drawn}");
        var beside = Pixel(info.FirstFrameBgra!, info.Width, (int)((centre.X + 120) * k), (int)(centre.Y * k));
        Assert.True(beside.r > 140 && beside.g < 120, $"the window picture is missing from the recording: {beside}");
        scene.Annotations.Clear();

        // Minimised: reported as such instead of silently freezing.
        window.Do(w => w.WindowState = WindowState.Minimized);
        Assert.True(WaitFor(() => engine.ScreenHealth.Minimized, 8000), "a minimised window was not noticed: " + engine.ScreenHealth);
        Assert.True(engine.ScreenActive, "minimising must not drop the source");
        window.Do(w => w.WindowState = WindowState.Normal);
        Assert.True(WaitFor(() => !engine.ScreenHealth.Minimized, 8000));

        // Resized: the layer follows the new shape.
        double before = scene.Screen.Aspect;
        window.Do(w => { w.Width = 700; w.Height = 400; });
        window.SetColor(Color.FromRgb(30, 60, 220));
        Assert.True(WaitFor(() => Math.Abs(scene.Screen.Aspect - before) > 0.3, 8000), $"resize not followed: aspect {scene.Screen.Aspect} (was {before})");

        // Closed: the source is dropped and the loss is reported with the window it was.
        window.Dispose();
        Assert.True(WaitFor(() => !engine.ScreenActive && lost != null, 8000), "a closed window was not noticed");
        Assert.Equal(target.Handle, lost!.Handle);
        Assert.False(target.StillExists);
        Assert.False(engine.ScreenHealth.Active);
    }

    [Fact]
    public void A_black_or_stopped_phone_picture_is_reported_as_such()
    {
        var scene = new Scene();
        using var engine = new StudioEngine(scene);
        var black = engine.PhoneFrames.Rent(64 * 128 * 4);
        Array.Clear(black);
        for (int i = 3; i < 64 * 128 * 4; i += 4) black[i] = 255;
        engine.PhoneFrames.Submit(black, 64, 128);
        Assert.True(WaitFor(() => engine.PhoneHealth.Active && engine.PhoneHealth.Black == true, 6000), "black picture not detected: " + engine.PhoneHealth);

        var lit = engine.PhoneFrames.Rent(64 * 128 * 4);
        Array.Fill(lit, (byte)180);
        engine.PhoneFrames.Submit(lit, 64, 128);
        Assert.True(WaitFor(() => engine.PhoneHealth.Black == false, 6000), "visible picture still reported black: " + engine.PhoneHealth);

        // No new pictures: the quiet time grows and the rate falls to zero. Nothing is invented.
        Assert.True(WaitFor(() => engine.PhoneHealth.QuietSeconds >= 2 && engine.PhoneHealth.Fps == 0, 8000), "a stopped picture was not noticed: " + engine.PhoneHealth);

        engine.PhoneFrames.Clear();
        Assert.True(WaitFor(() => !engine.PhoneHealth.Active, 6000));
    }
}
