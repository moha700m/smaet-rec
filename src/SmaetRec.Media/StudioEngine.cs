using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SmaetRec.Core;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace SmaetRec.Media;

public sealed record RecordingStatus(string Path, bool Microphone, bool SystemAudio, string? MicrophoneError, string? SystemAudioError);
public sealed record RecordingResult(string Path, string? Error, TimeSpan Duration, long Frames, long DroppedFrames, bool HadAudioTrack);

/// <summary>
/// The heart of the studio: one render thread that pulls pictures from the sources, draws the scene,
/// shows it in the preview window and feeds the recorder. Everything public is safe to call from the UI thread.
/// </summary>
public sealed class StudioEngine : IDisposable
{
    private sealed class RecordingState
    {
        public required Recorder Recorder; public AudioMixer? Mixer; public required Stopwatch Clock;
        public int Fps; public long LastIndex = -1; public bool ErrorReported;
    }

    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly object _recLock = new(), _stateLock = new();
    private readonly bool _forceSoftware;
    private volatile bool _stop;
    private int _dirty = 1;

    // Render-thread state.
    private GraphicsDevice? _gd;
    private Compositor? _compositor;
    private ID3D11Texture2D? _staging;
    private IVideoSource? _screenSource;
    private SimulatedFeed? _simScreenFeed, _simPhoneFeed;
    private IDXGISwapChain1? _swapChain;
    private ID2D1Bitmap1? _backBuffer;
    private IntPtr _hwnd;
    private int _previewWidth, _previewHeight;
    private int _outputWidth = 1920, _outputHeight = 1080;
    private FrameState _lastState = new();
    private Vec2? _lastCursor;
    private long _lastAliveCheck;
    private bool _animating, _phoneLandscapeSeen, _phoneSeen;
    private int _screenW, _screenH;

    // Shared state (under _stateLock).
    private CaptureTarget? _screenTarget;
    private Vec2? _pointer;
    private readonly List<(Vec2 pos, double start)> _ripples = new();
    private RecordingState? _rec;
    private readonly Stopwatch _time = Stopwatch.StartNew();

    public Scene Scene { get; }
    public ZoomController Zoom { get; } = new();
    public MemoryFrameSource PhoneFrames { get; private set; } = new();
    public int Fps { get; set; } = 30;
    public volatile bool Spotlight, Magnifier;
    public LayerKind? Selected { get => _selected; set { _selected = value; Invalidate(); } }
    private LayerKind? _selected;
    public string PhoneHint = "", ScreenHint = "", SimulationLabel = "SIMULATION";
    public bool IsSoftwareRendering { get; private set; }
    public bool ScreenActive { get; private set; }
    public bool IsRecording { get { lock (_recLock) return _rec != null; } }
    public TimeSpan RecordingElapsed { get { lock (_recLock) return _rec?.Clock.Elapsed ?? TimeSpan.Zero; } }
    public long FramesRendered => Interlocked.Read(ref _framesRendered);
    private long _framesRendered;

    /// <summary>Raised when the screen picture starts, stops or fails. Argument: error text or null.</summary>
    public event Action<bool, string?>? ScreenChanged;
    public event Action<string>? RecordingFailed;
    public event Action<string>? Error;

    public StudioEngine(Scene scene, bool forceSoftware = false)
    {
        Scene = scene;
        _forceSoftware = forceSoftware;
        scene.Changed += Invalidate;
        PhoneFrames.FrameArrived += Invalidate;
        _thread = new Thread(Run) { IsBackground = true, Name = "Render", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Invalidate()
    {
        Interlocked.Exchange(ref _dirty, 1);
        _wake.Set();
    }

    private void Post(Action action) { _commands.Enqueue(action); _wake.Set(); }

    private void PostAndWait(Action action)
    {
        if (Thread.CurrentThread == _thread) { action(); return; }
        using var done = new ManualResetEventSlim();
        Exception? failure = null;
        Post(() => { try { action(); } catch (Exception e) { failure = e; } finally { done.Set(); } });
        while (!done.Wait(100)) if (!_thread.IsAlive) return;
        if (failure != null) throw failure;
    }

    // ---------- preview ----------

    public void AttachPreview(IntPtr hwnd, int width, int height) => Post(() =>
    {
        _hwnd = hwnd; _previewWidth = Math.Max(1, width); _previewHeight = Math.Max(1, height);
        CreateSwapChain();
        Interlocked.Exchange(ref _dirty, 1);
    });

    public void ResizePreview(int width, int height) => Post(() =>
    {
        width = Math.Max(1, width); height = Math.Max(1, height);
        if (_swapChain == null || (width == _previewWidth && height == _previewHeight)) return;
        _previewWidth = width; _previewHeight = height;
        _backBuffer?.Dispose(); _backBuffer = null;
        _gd!.DC.Target = null;
        _swapChain.ResizeBuffers(2, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
        CreateBackBuffer();
        Interlocked.Exchange(ref _dirty, 1);
    });

    public void DetachPreview() => PostAndWait(() =>
    {
        _backBuffer?.Dispose(); _backBuffer = null;
        _swapChain?.Dispose(); _swapChain = null;
        _hwnd = IntPtr.Zero;
    });

    private void CreateSwapChain()
    {
        if (_hwnd == IntPtr.Zero || _gd == null) return;
        _backBuffer?.Dispose(); _swapChain?.Dispose();
        using var dxgiDevice = _gd.Device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        using var factory = adapter.GetParent<IDXGIFactory2>();
        var desc = new SwapChainDescription1((uint)_previewWidth, (uint)_previewHeight, Format.B8G8R8A8_UNorm, false,
            Usage.RenderTargetOutput, 2, Scaling.Stretch, SwapEffect.FlipSequential, Vortice.DXGI.AlphaMode.Ignore, SwapChainFlags.None);
        _swapChain = factory.CreateSwapChainForHwnd(_gd.Device, _hwnd, desc, null, null);
        CreateBackBuffer();
    }

    private void CreateBackBuffer()
    {
        using var surface = _swapChain!.GetBuffer<IDXGISurface>(0);
        _backBuffer = _gd!.DC.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore), 96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw));
    }

    /// <summary>Converts a point in the preview window (pixels) to the final picture (canvas units). Null when outside the picture.</summary>
    public static Vec2? PreviewToOutput(double x, double y, double areaWidth, double areaHeight, bool clamp = false)
    {
        var fit = Compositor.PreviewRect(areaWidth, areaHeight);
        double ox = (x - fit.X) / fit.Width * Scene.CanvasWidth, oy = (y - fit.Y) / fit.Height * Scene.CanvasHeight;
        if (clamp) return new Vec2(Math.Clamp(ox, 0, Scene.CanvasWidth), Math.Clamp(oy, 0, Scene.CanvasHeight));
        return ox < 0 || oy < 0 || ox > Scene.CanvasWidth || oy > Scene.CanvasHeight ? null : new Vec2(ox, oy);
    }

    // ---------- sources ----------

    public CaptureTarget? ScreenTarget { get { lock (_stateLock) return _screenTarget; } }

    public void ShowScreen(CaptureTarget target) => Post(() =>
    {
        DropScreenSource();
        string? error = null;
        try
        {
            _screenSource = CreateScreenSource(target);
            lock (_stateLock) _screenTarget = target;
            ScreenActive = true;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Error("Show screen", e);
            error = e.Message;
        }
        lock (Scene.Sync) Scene.Screen.Visible = true;
        Interlocked.Exchange(ref _dirty, 1);
        ScreenChanged?.Invoke(ScreenActive, error);
    });

    private IVideoSource CreateScreenSource(CaptureTarget target)
    {
        if (WgcScreenSource.IsSupported)
        {
            try { return new WgcScreenSource(_gd!, target); }
            catch (Exception e) when (e is not OutOfMemoryException) { Log.Error("Modern capture unavailable, using fallback", e); }
        }
        return new GdiScreenSource(target);
    }

    public void HideScreen() => Post(() =>
    {
        DropScreenSource();
        Interlocked.Exchange(ref _dirty, 1);
        ScreenChanged?.Invoke(false, null);
    });

    private void DropScreenSource()
    {
        _simScreenFeed?.Dispose(); _simScreenFeed = null;
        _screenSource?.Dispose(); _screenSource = null;
        lock (_stateLock) _screenTarget = null;
        ScreenActive = false; _screenW = _screenH = 0;
    }

    /// <summary>Development only: moving test patterns in place of real devices, labelled as a simulation.</summary>
    public void UseSimulatedSources(bool screen, bool phone) => Post(() =>
    {
        if (screen)
        {
            DropScreenSource();
            var source = new MemoryFrameSource(simulated: true);
            source.FrameArrived += Invalidate;
            _simScreenFeed = new SimulatedFeed(source, 1280, 720);
            _screenSource = source;
            ScreenActive = true;
            ScreenChanged?.Invoke(true, null);
        }
        if (phone && _simPhoneFeed == null)
        {
            var old = PhoneFrames;
            PhoneFrames = new MemoryFrameSource(simulated: true);
            PhoneFrames.FrameArrived += Invalidate;
            old.Dispose();
            _simPhoneFeed = new SimulatedFeed(PhoneFrames, 590, 1278);
        }
    });

    // ---------- pointer and effects ----------

    /// <summary>Pointer position on the final picture (canvas units), or null when it left the preview.</summary>
    public void SetPointer(Vec2? outputPoint)
    {
        lock (_stateLock) _pointer = outputPoint;
        if (Spotlight || Magnifier) Invalidate();
    }

    public void AddRipple(Vec2 canvasPoint)
    {
        lock (_stateLock) _ripples.Add((canvasPoint, _time.Elapsed.TotalSeconds));
        Invalidate();
    }

    /// <summary>Maps a desktop position onto the canvas through the screen layer. Null when it is outside what is being shown.</summary>
    public Vec2? DesktopToCanvas(int x, int y)
    {
        CaptureTarget? target;
        lock (_stateLock) target = _screenTarget;
        if (target == null) return null;
        var (left, top, w, h) = target.CurrentBounds();
        if (w <= 0 || h <= 0) return null;
        double u = (x - left) / (double)w, v = (y - top) / (double)h;
        if (u < 0 || v < 0 || u > 1 || v > 1) return null;
        lock (Scene.Sync)
        {
            var l = Scene.Screen;
            return l.Visible ? l.ToCanvas(new Vec2((u - 0.5) * l.Width, (v - 0.5) * l.Height)) : null;
        }
    }

    public void AddDesktopClick(int x, int y)
    {
        if (DesktopToCanvas(x, y) is { } p) AddRipple(p);
    }

    // ---------- recording ----------

    public void SetOutputSize(int width, int height)
    {
        if (IsRecording) return;
        PostAndWait(() =>
        {
            if (width == _outputWidth && height == _outputHeight && _compositor != null) return;
            _outputWidth = width; _outputHeight = height;
            CreateCompositor();
            Interlocked.Exchange(ref _dirty, 1);
        });
    }

    /// <summary>Starts writing an MP4. Returns what is really being recorded, or null with <paramref name="error"/> set.</summary>
    public RecordingStatus? StartRecording(AppSettings settings, out string? error)
    {
        error = null;
        if (IsRecording) return null;
        try
        {
            SetOutputSize(settings.OutputWidth, settings.OutputHeight);
            Fps = settings.Fps;
            string path = AppPaths.NewRecordingPath(settings.OutputFolder, DateTime.Now);
            var (micAvailable, systemAvailable) = AudioMixer.Probe();
            bool wantMic = settings.RecordMicrophone && micAvailable, wantSystem = settings.RecordSystemAudio && systemAvailable;
            Recorder recorder;
            try
            {
                recorder = new Recorder(new RecorderOptions(path, settings.OutputWidth, settings.OutputHeight, settings.Fps, settings.BitrateBps, wantMic || wantSystem));
            }
            catch (Exception e) when ((wantMic || wantSystem) && e is not OutOfMemoryException)
            {
                Log.Error("Recorder with audio failed, retrying without sound", e);
                wantMic = wantSystem = false;
                recorder = new Recorder(new RecorderOptions(path, settings.OutputWidth, settings.OutputHeight, settings.Fps, settings.BitrateBps, false));
            }
            var clock = Stopwatch.StartNew();
            var mixer = recorder.HasAudio ? new AudioMixer(recorder, clock, wantMic, wantSystem) : null;
            lock (_recLock) _rec = new RecordingState { Recorder = recorder, Mixer = mixer, Clock = clock, Fps = settings.Fps };
            Invalidate();
            Log.Write($"Recording started: {path} {settings.OutputWidth}x{settings.OutputHeight}@{settings.Fps} mic={mixer?.MicrophoneActive} system={mixer?.SystemAudioActive}");
            return new RecordingStatus(path, mixer?.MicrophoneActive ?? false, mixer?.SystemAudioActive ?? false, mixer?.MicrophoneError, mixer?.SystemAudioError);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Error("Start recording", e);
            error = e.Message;
            return null;
        }
    }

    /// <summary>Stops and finishes the file in the background.</summary>
    public Task<RecordingResult?> StopRecordingAsync()
    {
        RecordingState? r;
        lock (_recLock) { r = _rec; _rec = null; }
        Invalidate();
        if (r == null) return Task.FromResult<RecordingResult?>(null);
        return Task.Run<RecordingResult?>(() =>
        {
            r.Clock.Stop();
            r.Mixer?.Dispose();
            string? error = r.Recorder.Finish();
            Log.Write($"Recording finished: {r.Recorder.Options.Path} frames={r.Recorder.VideoFrames} dropped={r.Recorder.DroppedFrames} error={error}");
            return new RecordingResult(r.Recorder.Options.Path, error, r.Clock.Elapsed, r.Recorder.VideoFrames, r.Recorder.DroppedFrames, r.Recorder.HasAudio);
        });
    }

    /// <summary>Copies the current finished picture (BGRA, top-down). Used by tests and for thumbnails.</summary>
    public (byte[] pixels, int width, int height) Snapshot()
    {
        byte[] pixels = Array.Empty<byte>(); int w = 0, h = 0;
        PostAndWait(() =>
        {
            if (_gd == null || _compositor == null) return;
            RenderScene();
            w = _compositor.Width; h = _compositor.Height;
            pixels = new byte[w * h * 4];
            _gd.Context.CopyResource(_staging!, _compositor.OutputTexture);
            var map = _gd.Context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                for (int y = 0; y < h; y++) Marshal.Copy(map.DataPointer + (nint)(y * (long)map.RowPitch), pixels, y * w * 4, w * 4);
            }
            finally { _gd.Context.Unmap(_staging!, 0); }
        });
        return (pixels, w, h);
    }

    // ---------- render thread ----------

    private void CreateCompositor()
    {
        _compositor?.Dispose(); _staging?.Dispose();
        _compositor = new Compositor(_gd!, _outputWidth, _outputHeight);
        _staging = _gd!.CreateStaging(_outputWidth, _outputHeight);
    }

    private void CreateDevice()
    {
        _gd = new GraphicsDevice(_forceSoftware);
        IsSoftwareRendering = _gd.IsSoftware;
        CreateCompositor();
        CreateSwapChain();
    }

    private void DestroyDevice()
    {
        _backBuffer?.Dispose(); _backBuffer = null;
        _swapChain?.Dispose(); _swapChain = null;
        if (_screenSource is not MemoryFrameSource) { _screenSource?.Dispose(); _screenSource = null; }
        else ((MemoryFrameSource)_screenSource).ResetDevice();
        PhoneFrames.ResetDevice();
        _staging?.Dispose(); _staging = null;
        _compositor?.Dispose(); _compositor = null;
        _gd?.Dispose(); _gd = null;
    }

    private void Run()
    {
        using var timer = new PreciseTimer();
        try { CreateDevice(); }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Error("Graphics start", e);
            Error?.Invoke(e.Message);
        }
        double next = 0, last = _time.Elapsed.TotalSeconds;
        while (!_stop)
        {
            bool active = _screenSource != null || _animating || IsRecording || Volatile.Read(ref _dirty) == 1 || !_commands.IsEmpty;
            double now = _time.Elapsed.TotalSeconds;
            if (!active) _wake.WaitOne(250);
            else if (next > now) timer.Sleep(next - now);
            now = _time.Elapsed.TotalSeconds;
            // While idle, 30 fps is enough for a live preview even when 60 fps recording is selected.
            double period = 1.0 / Math.Clamp(IsRecording ? Fps : Math.Min(Fps, 30), 10, 120);
            next = Math.Max(next + period, now + period * 0.25);
            try
            {
                while (_commands.TryDequeue(out var command)) command();
                if (_gd == null) { Thread.Sleep(1000); CreateDevice(); RestoreScreenAfterDeviceLoss(); }
                Tick(now, Math.Min(0.25, now - last));
            }
            catch (Exception e) when (e is SharpGen.Runtime.SharpGenException or COMException)
            {
                Log.Error("Render", e);
                Error?.Invoke(e.Message);
                try { DestroyDevice(); } catch (Exception inner) { Log.Error("Device teardown", inner); _gd = null; }
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // A managed hiccup should not silently kill the render thread (and with it recording).
                Log.Error("Render frame", e);
                Error?.Invoke(e.Message);
                Thread.Sleep(120);
            }
            last = now;
        }
        _simScreenFeed?.Dispose(); _simPhoneFeed?.Dispose();
        try { _screenSource?.Dispose(); _screenSource = null; DestroyDevice(); } catch (Exception e) { Log.Error("Render shutdown", e); }
    }

    private void RestoreScreenAfterDeviceLoss()
    {
        CaptureTarget? target;
        lock (_stateLock) target = _screenTarget;
        if (target != null && _screenSource == null)
        {
            try { _screenSource = CreateScreenSource(target); }
            catch (Exception e) when (e is not OutOfMemoryException) { Log.Error("Restore screen", e); DropScreenSource(); ScreenChanged?.Invoke(false, e.Message); }
        }
        Interlocked.Exchange(ref _dirty, 1);
    }

    private void Tick(double now, double dt)
    {
        var gd = _gd!;
        bool dirty = Interlocked.Exchange(ref _dirty, 0) == 1;

        if (_screenSource != null)
        {
            if (_screenSource.Update(gd))
            {
                dirty = true;
                if (_screenSource.Width != _screenW || _screenSource.Height != _screenH)
                {
                    _screenW = _screenSource.Width; _screenH = _screenSource.Height;
                    Scene.SetScreenSourceSize(_screenW, _screenH);
                }
            }
            if (Environment.TickCount64 - _lastAliveCheck > 1000)
            {
                _lastAliveCheck = Environment.TickCount64;
                if (!_screenSource.IsAlive) { DropScreenSource(); dirty = true; ScreenChanged?.Invoke(false, null); }
            }
        }
        if (PhoneFrames.Update(gd))
        {
            dirty = true;
            if (PhoneFrames.Width > 0)
            {
                bool landscape = PhoneFrames.Width > PhoneFrames.Height;
                if (!_phoneSeen || landscape != _phoneLandscapeSeen) { _phoneSeen = true; _phoneLandscapeSeen = landscape; Scene.SetPhoneLandscape(landscape); }
            }
            else _phoneSeen = false;
        }

        bool zooming = Zoom.Step(dt);
        bool ripples;
        lock (_stateLock)
        {
            _ripples.RemoveAll(r => now - r.start > RippleSeconds);
            ripples = _ripples.Count > 0;
        }
        Vec2? cursor = null;
        if (Scene.Effects.CursorHighlight && _screenSource != null && MouseWatcher.CursorPosition() is { } c) cursor = DesktopToCanvas(c.x, c.y);
        if (cursor != _lastCursor) { _lastCursor = cursor; dirty = true; }
        _animating = zooming || ripples;
        if (_animating) dirty = true;

        if (dirty) RenderScene(now);

        lock (_recLock)
        {
            if (_rec is { } r)
            {
                long index = (long)(r.Clock.Elapsed.TotalSeconds * r.Fps);
                if (index > r.LastIndex)
                {
                    r.LastIndex = index;
                    gd.Context.CopyResource(_staging!, _compositor!.OutputTexture);
                    var map = gd.Context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                    try { r.Recorder.WriteVideo(map.DataPointer, (int)map.RowPitch, index * 10_000_000L / r.Fps, 10_000_000L / r.Fps); }
                    finally { gd.Context.Unmap(_staging!, 0); }
                }
                if (r.Recorder.Error is { } error && !r.ErrorReported) { r.ErrorReported = true; RecordingFailed?.Invoke(error); }
            }
        }

        if (dirty && _swapChain != null && _backBuffer != null)
        {
            _compositor!.DrawPreview(_backBuffer, _previewWidth, _previewHeight, _lastState, new Color4(0.043f, 0.047f, 0.063f, 1f));
            _swapChain.Present(0, PresentFlags.None).CheckError();
        }
    }

    private const double RippleSeconds = 0.55;

    private void RenderScene(double? time = null)
    {
        double now = time ?? _time.Elapsed.TotalSeconds;
        var st = new FrameState
        {
            Annotations = Scene.Annotations.Snapshot(),
            ZoomRegion = Zoom.VisibleRegion(),
            Spotlight = Spotlight, Magnifier = Magnifier,
            CursorHighlight = _lastCursor,
            Selected = _selected,
            Recording = IsRecording,
            PhoneHint = PhoneHint, ScreenHint = ScreenHint, SimulationLabel = SimulationLabel,
            PhoneIsSimulated = PhoneFrames.IsSimulated, ScreenIsSimulated = _screenSource?.IsSimulated ?? false,
        };
        lock (Scene.Sync)
        {
            st.Screen = Scene.Screen.Clone(); st.Phone = Scene.Phone.Clone();
            var fx = Scene.Effects;
            st.Effects = new EffectSettings { BackgroundIndex = fx.BackgroundIndex, ClickRipples = fx.ClickRipples, CursorHighlight = fx.CursorHighlight, Shadows = fx.Shadows };
        }
        lock (_stateLock)
        {
            st.Pointer = _pointer;
            foreach (var (pos, start) in _ripples) st.Ripples.Add(new Ripple(pos, (now - start) / RippleSeconds));
        }
        _compositor!.Render(st, _screenSource?.Bitmap, PhoneFrames.Bitmap);
        _lastState = st;
        Interlocked.Increment(ref _framesRendered);
    }

    public void Dispose()
    {
        if (_stop) return;
        var pending = StopRecordingAsync();
        try { pending.Wait(10000); } catch (AggregateException e) { Log.Error("Stop recording on exit", e); }
        _stop = true;
        _wake.Set();
        if (!_thread.Join(5000)) Log.Write("Render thread did not stop in time");
        Scene.Changed -= Invalidate;
    }

    /// <summary>Sleeps accurately using a high-resolution waitable timer, without raising the system timer rate.</summary>
    private sealed class PreciseTimer : IDisposable
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);
        [DllImport("kernel32.dll")] private static extern bool SetWaitableTimer(IntPtr timer, ref long due, int period, IntPtr routine, IntPtr arg, bool resume);
        [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint ms);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        private readonly IntPtr _handle = CreateWaitableTimerExW(IntPtr.Zero, null, 0x2, 0x1F0003);

        public void Sleep(double seconds)
        {
            if (seconds <= 0) return;
            long due = -(long)(seconds * 10_000_000);
            if (_handle != IntPtr.Zero && SetWaitableTimer(_handle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false)) WaitForSingleObject(_handle, 1000);
            else Thread.Sleep(Math.Max(1, (int)(seconds * 1000)));
        }

        public void Dispose() { if (_handle != IntPtr.Zero) CloseHandle(_handle); }
    }
}
