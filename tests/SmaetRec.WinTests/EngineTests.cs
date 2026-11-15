using System.Diagnostics;
using SmaetRec.Core;
using SmaetRec.Media;
using Xunit.Abstractions;
using static SmaetRec.WinTests.TestSupport;

namespace SmaetRec.WinTests;

[Collection("engine")]
public class EngineTests(ITestOutputHelper output)
{
    [Fact]
    public void Scene_is_drawn_with_layers_frame_drawings_and_effects()
    {
        var scene = new Scene();
        scene.Effects.BackgroundIndex = 6; // black, so anything drawn stands out
        using var engine = new StudioEngine(scene) { SimulationLabel = "SIMULATION" };
        engine.UseSimulatedSources(screen: true, phone: true);
        Assert.True(WaitFor(() => engine.ScreenActive && engine.FramesRendered > 5), "engine never drew a frame");
        output.WriteLine("software rendering: " + engine.IsSoftwareRendering);

        // A red rectangle outline in the top-left corner and a text drawing.
        scene.Annotations.Add(new Annotation { Kind = AnnotationKind.Rectangle, Color = 0xFFFF0000, Thickness = 12, Points = { new Vec2(20, 20), new Vec2(220, 120) } });
        scene.Annotations.Add(new Annotation { Kind = AnnotationKind.Text, Text = "مرحبا SMAET", Color = 0xFFFFFFFF, Thickness = 8, Points = { new Vec2(900, 30) } });
        scene.Annotations.Add(new Annotation { Kind = AnnotationKind.Arrow, Color = 0xFF00FF00, Thickness = 10, Points = { new Vec2(300, 950), new Vec2(700, 1040) } });
        var (pixels, w, h) = engine.Snapshot();
        SavePng("scene.png", pixels, w, h);
        Assert.Equal((1920, 1080), (w, h));

        var corner = Pixel(pixels, w, 4, 4);
        Assert.True(corner.r < 12 && corner.g < 12 && corner.b < 12, $"background should be black, was {corner}");
        var edge = Pixel(pixels, w, 20, 70);
        Assert.True(edge.r > 200 && edge.g < 60 && edge.b < 60, $"rectangle edge should be red, was {edge}");
        var inside = Pixel(pixels, w, 120, 70);
        Assert.True(inside.r < 60, $"rectangle is an outline, inside was {inside}");

        // The screen layer shows the moving test picture; the phone layer shows its frame and picture.
        var s = scene.Screen; var p = scene.Phone;
        var screenPixel = Pixel(pixels, w, (int)s.CenterX, (int)s.CenterY);
        Assert.True(screenPixel.r + screenPixel.g + screenPixel.b > 60, $"screen layer looks empty: {screenPixel}");
        var phonePixel = Pixel(pixels, w, (int)p.CenterX, (int)p.CenterY);
        Assert.True(phonePixel.r + phonePixel.g + phonePixel.b > 60, $"phone layer looks empty: {phonePixel}");
        var frameEdge = Pixel(pixels, w, (int)(p.CenterX - p.Width / 2 + 3), (int)p.CenterY);
        Assert.True(frameEdge.r > 40 && Math.Abs(frameEdge.r - frameEdge.g) < 25, $"phone frame edge should be metal grey, was {frameEdge}");
        // Simulated sources must be labelled: the red badge sits at the top-left of the screen layer.
        var badge = Pixel(pixels, w, (int)(s.CenterX - s.Width / 2 + 24), (int)(s.CenterY - s.Height / 2 + 20));
        Assert.True(badge.r > 200 && badge.g < 110, $"simulation badge missing, was {badge}");

        // Undo removes the last drawing; zoom changes the picture; spotlight darkens away from the pointer.
        scene.Annotations.Undo(); scene.Annotations.Undo(); scene.Annotations.Undo();
        var cleared = engine.Snapshot();
        var edgeAfter = Pixel(cleared.pixels, w, 20, 70);
        Assert.True(edgeAfter.r < 60, $"undo should remove the rectangle, was {edgeAfter}");

        engine.Spotlight = true;
        engine.SetPointer(new Vec2(s.CenterX, s.CenterY));
        var lit = engine.Snapshot();
        SavePng("spotlight.png", lit.pixels, w, h);
        var far = Pixel(lit.pixels, w, (int)(p.CenterX), (int)p.CenterY);
        Assert.True(far.r + far.g + far.b < (phonePixel.r + phonePixel.g + phonePixel.b) * 0.7, $"spotlight should dim far areas: {far} vs {phonePixel}");
        engine.Spotlight = false;

        engine.Magnifier = true;
        var lens = engine.Snapshot();
        SavePng("magnifier.png", lens.pixels, w, h);
        engine.Magnifier = false;

        engine.Zoom.ZoomBy(2, new Vec2(s.CenterX, s.CenterY));
        Assert.True(WaitFor(() => Math.Abs(engine.Zoom.Current - 2) < 0.01), "zoom animation did not finish");
        var zoomed = engine.Snapshot();
        SavePng("zoomed.png", zoomed.pixels, w, h);
        var zoomCorner = Pixel(zoomed.pixels, w, 4, 4);
        Assert.True(zoomCorner.r + zoomCorner.g + zoomCorner.b > 30, "zoomed picture should show the screen layer in the corner");
    }

    [Fact]
    public void Blur_region_hides_detail()
    {
        var scene = new Scene();
        using var engine = new StudioEngine(scene);
        engine.UseSimulatedSources(screen: true, phone: false);
        Assert.True(WaitFor(() => engine.ScreenActive && engine.FramesRendered > 5));
        var s = scene.Screen;
        // The test picture has a checker pattern; measure contrast along a line, then blur that area.
        int y = (int)s.CenterY, x0 = (int)(s.CenterX - 200), x1 = (int)(s.CenterX + 200);
        static int Contrast(byte[] px, int w, int y, int x0, int x1)
        {
            int total = 0;
            for (int x = x0; x < x1; x++) total += Math.Abs(Pixel(px, w, x + 1, y).r - Pixel(px, w, x, y).r);
            return total;
        }
        var before = engine.Snapshot();
        scene.Annotations.Add(new Annotation { Kind = AnnotationKind.Blur, Points = { new Vec2(x0 - 50, y - 80), new Vec2(x1 + 50, y + 80) } });
        var after = engine.Snapshot();
        SavePng("blur.png", after.pixels, after.width, after.height);
        int c0 = Contrast(before.pixels, before.width, y, x0, x1), c1 = Contrast(after.pixels, after.width, y, x0, x1);
        output.WriteLine($"contrast before {c0}, after {c1}");
        Assert.True(c1 < c0 || c0 == 0, "blur should reduce detail");
    }

    [SkippableFact]
    public void Real_screen_capture_shows_the_desktop()
    {
        var monitor = CaptureTarget.PrimaryMonitor();
        Skip.If(monitor == null, "This machine has no display to capture");
        output.WriteLine($"monitor {monitor!.Title} {monitor.Width}x{monitor.Height}, modern capture supported: {WgcScreenSource.IsSupported}");
        var scene = new Scene();
        scene.Effects.BackgroundIndex = 6;
        using var engine = new StudioEngine(scene);
        string? error = null; bool changed = false;
        engine.ScreenChanged += (_, e) => { error = e; changed = true; };
        engine.ShowScreen(monitor);
        Assert.True(WaitFor(() => changed), "capture never reported back");
        Assert.True(engine.ScreenActive, "screen capture did not start: " + error);
        long framesBefore = engine.FramesRendered;
        bool gotPicture = WaitFor(() =>
        {
            var (px, w, _) = engine.Snapshot();
            var c = Pixel(px, w, (int)scene.Screen.CenterX, (int)scene.Screen.CenterY);
            var c2 = Pixel(px, w, (int)scene.Screen.CenterX - 300, (int)scene.Screen.CenterY - 150);
            return c.r + c.g + c.b + c2.r + c2.g + c2.b > 0 && engine.FramesRendered > framesBefore;
        }, 10000);
        var shot = engine.Snapshot();
        SavePng("screen-capture.png", shot.pixels, shot.width, shot.height);
        Skip.If(!gotPicture, "Capture started but the desktop is completely black on this machine (no interactive desktop)");
        Assert.InRange(scene.Screen.Aspect, (double)monitor.Width / monitor.Height - 0.02, (double)monitor.Width / monitor.Height + 0.02);
        Assert.NotEmpty(CaptureTarget.Monitors());
        output.WriteLine($"windows found: {CaptureTarget.Windows().Count}");
        engine.HideScreen();
        Assert.True(WaitFor(() => !engine.ScreenActive));
    }

    [Theory]
    [InlineData(VideoQuality.Hd720, 30)]
    [InlineData(VideoQuality.FullHd1080, 60)]
    public void Recording_produces_a_correct_mp4(VideoQuality quality, int fps)
    {
        var folder = Path.Combine(Artifacts, "recordings");
        var scene = new Scene();
        scene.Effects.BackgroundIndex = 6;
        // Marker to prove the video is the right way up: red block top-left, green block bottom-left.
        scene.Annotations.Add(new Annotation { Kind = AnnotationKind.Rectangle, Color = 0xFFFF0000, Thickness = 60, Points = { new Vec2(60, 60), new Vec2(120, 120) } });
        scene.Annotations.Add(new Annotation { Kind = AnnotationKind.Rectangle, Color = 0xFF00FF00, Thickness = 60, Points = { new Vec2(60, 960), new Vec2(120, 1020) } });
        using var engine = new StudioEngine(scene);
        engine.UseSimulatedSources(screen: true, phone: true);
        Assert.True(WaitFor(() => engine.ScreenActive && engine.FramesRendered > 3));

        var settings = Settings(folder, quality, fps);
        var status = engine.StartRecording(settings, out var error);
        Assert.True(status != null, "recording did not start: " + error);
        output.WriteLine($"recording to {status!.Path}; microphone={status.Microphone} ({status.MicrophoneError}) system audio={status.SystemAudio} ({status.SystemAudioError})");
        Assert.True(engine.IsRecording);
        Thread.Sleep(3000);
        Assert.InRange(engine.RecordingElapsed.TotalSeconds, 2.9, 6);
        var result = engine.StopRecordingAsync().GetAwaiter().GetResult();
        Assert.NotNull(result);
        Assert.False(engine.IsRecording);
        Assert.True(result!.Error == null, "recording failed: " + result.Error);
        output.WriteLine($"frames={result.Frames} dropped={result.DroppedFrames} duration={result.Duration.TotalSeconds:F2}s audio track={result.HadAudioTrack}");

        Assert.True(File.Exists(result.Path));
        Assert.True(new FileInfo(result.Path).Length > 20_000, "file is suspiciously small");
        var info = Mp4Info.Read(result.Path);
        output.WriteLine($"file: {info.Width}x{info.Height} {info.Fps:F2} fps, {info.VideoFrames} frames, {info.DurationSeconds:F2}s video, audio={info.HasAudio} {info.AudioSeconds:F2}s");
        Assert.True(info.HasVideo);
        Assert.Equal(Vortice.MediaFoundation.VideoFormatGuids.H264, info.VideoCodec);
        Assert.Equal((settings.OutputWidth, settings.OutputHeight), (info.Width, info.Height));
        Assert.InRange(info.Fps, fps - 0.5, fps + 0.5);
        Assert.InRange(info.DurationSeconds, 2.5, 4.5);
        Assert.True(info.VideoFrames >= fps * 2, $"too few frames in the file: {info.VideoFrames}");
        Assert.Equal(result.HadAudioTrack, info.HasAudio);
        if (info.HasAudio) Assert.InRange(info.AudioSeconds, info.DurationSeconds - 0.5, info.DurationSeconds + 0.5);

        Assert.NotNull(info.FirstFrameBgra);
        SavePng($"recording-first-frame-{info.Height}p.png", info.FirstFrameBgra!, info.Width, info.Height);
        double k = info.Width / Scene.CanvasWidth;
        var top = Pixel(info.FirstFrameBgra!, info.Width, (int)(90 * k), (int)(90 * k));
        var bottom = Pixel(info.FirstFrameBgra!, info.Width, (int)(90 * k), (int)(990 * k));
        Assert.True(top.r > 150 && top.g < 90, $"top-left of the video should be the red marker (video upside down?), was {top}");
        Assert.True(bottom.g > 150 && bottom.r < 90, $"bottom-left of the video should be the green marker, was {bottom}");
    }

    [Fact]
    public void Recorder_writes_sound_in_step_with_picture()
    {
        var path = Path.Combine(Artifacts, "recordings", "tone.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Delete(path);
        const int w = 640, h = 360, fps = 30, seconds = 2;
        Recorder recorder;
        try { recorder = new Recorder(new RecorderOptions(path, w, h, fps, 2_000_000, WithAudio: true)); }
        catch (Exception e) { Assert.Fail("Recorder with an audio track could not be created: " + e.Message); return; }
        var frame = new byte[w * h * 4];
        var pcm = new short[Recorder.AudioRate / fps * 2];
        long audioPos = 0;
        unsafe
        {
            fixed (byte* p = frame)
                for (int i = 0; i < fps * seconds; i++)
                {
                    for (int n = 0; n < frame.Length; n += 4) { frame[n] = (byte)(i * 4); frame[n + 1] = 80; frame[n + 2] = 200; frame[n + 3] = 255; }
                    while (!recorder.WriteVideo((IntPtr)p, w * 4, i * 10_000_000L / fps, 10_000_000L / fps)) Thread.Sleep(5);
                    for (int s = 0; s < pcm.Length / 2; s++)
                    {
                        short v = (short)(Math.Sin((audioPos + s) * 2 * Math.PI * 440 / Recorder.AudioRate) * 12000);
                        pcm[s * 2] = v; pcm[s * 2 + 1] = v;
                    }
                    recorder.WriteAudio(pcm, pcm.Length / 2, audioPos);
                    audioPos += pcm.Length / 2;
                }
        }
        Assert.Null(recorder.Finish());
        var info = Mp4Info.Read(path);
        output.WriteLine($"{info.VideoFrames} frames {info.DurationSeconds:F2}s, audio {info.AudioSeconds:F2}s");
        Assert.True(info.HasAudio, "the file has no audio track");
        Assert.Equal(fps * seconds, info.VideoFrames);
        Assert.InRange(info.DurationSeconds, seconds - 0.1, seconds + 0.1);
        Assert.InRange(info.AudioSeconds, seconds - 0.15, seconds + 0.15);
        var px = Pixel(info.FirstFrameBgra!, w, w / 2, h / 2);
        Assert.True(px.r > 150 && px.g < 130, $"colour order wrong in the file: {px}");
    }

    [Fact]
    public void Sound_devices_are_reported_honestly()
    {
        var (mic, system) = AudioMixer.Probe();
        output.WriteLine($"microphone available: {mic}, playback device available: {system}");
        var path = Path.Combine(Artifacts, "recordings", "probe.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var recorder = new Recorder(new RecorderOptions(path, 320, 240, 30, 500_000, WithAudio: true));
        using var mixer = new AudioMixer(recorder, Stopwatch.StartNew(), microphone: true, systemAudio: true);
        // Whatever the machine has, "active" must only be true for a device that really opened.
        if (!mic) Assert.False(mixer.MicrophoneActive);
        if (!system) Assert.False(mixer.SystemAudioActive);
        if (!mixer.MicrophoneActive && mic) output.WriteLine("microphone error: " + mixer.MicrophoneError);
        if (!mixer.SystemAudioActive && system) output.WriteLine("system audio error: " + mixer.SystemAudioError);
    }

    [Fact]
    public void Phone_video_stream_is_decoded_to_the_right_colours()
    {
        var stream = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "blue-320x240.h264"));
        // Split into access units at the access-unit delimiter, the way packets arrive from an iPhone (one picture each).
        var units = new List<byte[]>();
        int start = 0;
        for (int i = 4; i + 4 < stream.Length; i++)
            if (stream[i] == 0 && stream[i + 1] == 0 && stream[i + 2] == 0 && stream[i + 3] == 1 && (stream[i + 4] & 0x1F) == 9)
            { units.Add(stream[start..i]); start = i; }
        units.Add(stream[start..]);
        Assert.InRange(units.Count, 28, 32);

        var frames = new MemoryFrameSource();
        int arrived = 0; (int w, int h) size = default;
        frames.FrameArrived += () => Interlocked.Increment(ref arrived);
        using (var decoder = new H264Decoder(frames))
        {
            decoder.SizeChanged += (w, h) => size = (w, h);
            long t = 0;
            foreach (var unit in units) { decoder.Push(unit, t, isKeyFrame: ContainsNal(unit, 5)); t += 33_333; }
            bool ok = WaitFor(() => decoder.FramesDecoded >= 20 || decoder.Error != null, 15000);
            output.WriteLine($"decoded {decoder.FramesDecoded} of {units.Count}, size {decoder.Width}x{decoder.Height}, error: {decoder.Error}");
            Assert.True(decoder.Error == null, "decoder failed: " + decoder.Error);
            Assert.True(ok, "decoder produced too few pictures");
            Assert.Equal((320, 240), (decoder.Width, decoder.Height));
        }
        Assert.Equal((320, 240), size);
        Assert.True(arrived >= 20);

        // Upload the last picture the way the engine does and check the colour (source was B=192 G=64 R=32).
        var device = new GraphicsDevice();
        frames.Update(device);
        Assert.NotNull(frames.Bitmap);
        var pixels = new byte[320 * 240 * 4];
        using (var map = new MappedBitmap(frames.Bitmap!)) map.CopyTo(pixels);
        frames.Dispose(); device.Dispose();
        var c = Pixel(pixels, 320, 160, 120);
        output.WriteLine($"decoded colour b={c.b} g={c.g} r={c.r}");
        Assert.InRange(c.b, 192 - 14, 192 + 14);
        Assert.InRange(c.g, 64 - 14, 64 + 14);
        Assert.InRange(c.r, 32 - 14, 32 + 14);
    }

    private static bool ContainsNal(byte[] data, int type)
    {
        for (int i = 0; i + 4 < data.Length; i++)
            if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1 && (data[i + 3] & 0x1F) == type) return true;
        return false;
    }

    /// <summary>Reads a Direct2D bitmap back to memory.</summary>
    private sealed class MappedBitmap(Vortice.Direct2D1.ID2D1Bitmap bitmap) : IDisposable
    {
        public void CopyTo(byte[] destination)
        {
            var b1 = bitmap.QueryInterface<Vortice.Direct2D1.ID2D1Bitmap1>();
            var size = b1.PixelSize;
            var map = b1.Map(Vortice.Direct2D1.MapOptions.Read);
            try
            {
                for (int y = 0; y < size.Height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(map.Bits + (nint)(y * map.Pitch), destination, y * size.Width * 4, size.Width * 4);
            }
            finally { b1.Unmap(); b1.Dispose(); }
        }
        public void Dispose() { }
    }

    [Fact]
    public void Usb_phone_check_runs_and_is_consistent()
    {
        var status = UsbPhoneDetector.Check();
        output.WriteLine(status.ToString());
        if (status.NetworkAddress != null) Assert.True(status.AppleDriverInstalled);
        Assert.Equal(status.PhonePluggedIn && status.AppleDriverInstalled && status.NetworkAddress != null, status.Ready);
    }
}
