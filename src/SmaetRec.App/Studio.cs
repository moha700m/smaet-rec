using SmaetRec.AirPlay;
using SmaetRec.Core;
using SmaetRec.Media;

namespace SmaetRec.App;

/// <summary>Everything the windows share: settings, the scene, the engine and the iPhone receiver.</summary>
public sealed class Studio : IDisposable
{
    private readonly object _decoderLock = new();
    private readonly object _receiverLogLock = new();
    private readonly Queue<string> _receiverLog = new();
    private H264Decoder? _decoder;
    private long? _firstPacketTimestamp;
    private long _lastPacketPts = -1;
    private System.Threading.Timer? _autosave;
    private DirectUsbCaptureClient? _usbClient;

    public AppSettings Settings { get; }
    public Scene Scene { get; } = new();
    public StudioEngine Engine { get; }
    public AirPlayReceiver Receiver { get; } = new();
    public bool Simulated { get; }

    /// <summary>Raised on a background thread when the iPhone connects, disconnects or the receiver fails.</summary>
    public event Action? PhoneStateChanged;

    public Studio(bool simulated)
    {
        Simulated = simulated;
        Settings = JsonStore.TryLoad<AppSettings>(AppPaths.SettingsFile) ?? new AppSettings();
        Settings.Normalize();
        Loc.Instance.Language = Settings.Language;
        ProjectFile.TryLoad(AppPaths.SessionFile, Scene);

        Engine = new StudioEngine(Scene) { Fps = Settings.Fps };
        Engine.SetOutputSize(Settings.OutputWidth, Settings.OutputHeight);
        UpdateTexts();

        Receiver.Log += message =>
        {
            lock (_receiverLogLock)
            {
                _receiverLog.Enqueue($"{DateTime.Now:HH:mm:ss}  {message}");
                while (_receiverLog.Count > 120) _receiverLog.Dequeue();
            }
            Log.Write(message);
        };
        Receiver.VideoPacket += OnVideoPacket;
        Receiver.StateChanged += OnReceiverState;
        Scene.Changed += ScheduleAutosave;
    }

    public void UpdateTexts()
    {
        Engine.PhoneHint = Loc.T("PhonePlaceholder");
        Engine.ScreenHint = Loc.T("ScreenPlaceholder");
        Engine.SimulationLabel = Loc.T("Simulation");
        Engine.Invalidate();
    }

    public void SaveSettings()
    {
        try { JsonStore.Save(AppPaths.SettingsFile, Settings); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Error("Save settings", e); }
    }

    /// <summary>(Re)starts listening for the iPhone. Safe to call again for "reconnect".</summary>
    public void StartReceiver() => Task.Run(() =>
    {
        try { Receiver.Start(Settings.ReceiverName); }
        catch (Exception e) when (e is not OutOfMemoryException) { Log.Error("Receiver start", e); }
    });

    public async Task ConnectUsbAsync(string udid, CancellationToken cancellationToken = default)
    {
        await StopUsbAsync().ConfigureAwait(false);
        var client = new DirectUsbCaptureClient(AppContext.BaseDirectory);
        client.VideoAccessUnit += OnDirectUsbVideo;
        client.Status += message => Log.Write("USB: " + message);
        client.Failed += message => { Log.Write("USB error: " + message); PhoneStateChanged?.Invoke(); };
        client.Disconnected += () => PhoneStateChanged?.Invoke();
        _usbClient = client;
        try { await client.StartAsync(udid, cancellationToken).ConfigureAwait(false); }
        catch { _usbClient = null; await client.DisposeAsync().ConfigureAwait(false); throw; }
        PhoneStateChanged?.Invoke();
    }

    public async Task StopUsbAsync()
    {
        var client = _usbClient;
        _usbClient = null;
        if (client != null) await client.DisposeAsync().ConfigureAwait(false);
        PhoneStateChanged?.Invoke();
    }

    private void OnDirectUsbVideo(byte[] data, long timestampMicros, bool keyFrame)
    {
        lock (_decoderLock)
        {
            _decoder ??= new H264Decoder(Engine.PhoneFrames);
            long pts = timestampMicros - (_firstPacketTimestamp ??= timestampMicros);
            if (pts < 0) pts = 0;
            if (pts <= _lastPacketPts) pts = _lastPacketPts + 1;
            _lastPacketPts = pts;
            _decoder.Push(data, pts, keyFrame);
        }
    }

    private void OnVideoPacket(VideoPacket packet)
    {
        lock (_decoderLock)
        {
            if (_decoder == null)
            {
                _decoder = new H264Decoder(Engine.PhoneFrames);
                _firstPacketTimestamp = null;
                _lastPacketPts = -1;
            }
            // Windows Media Foundation is happiest with small, strictly increasing sample
            // times. The phone sends an NTP wall clock, so make the first packet zero and
            // keep every following frame relative to it.
            long pts = packet.TimestampMicros - (_firstPacketTimestamp ??= packet.TimestampMicros);
            if (pts < 0) pts = 0;
            if (pts <= _lastPacketPts) pts = _lastPacketPts + 1;
            _lastPacketPts = pts;
            _decoder.Push(packet.Data, pts, packet.IsKeyFrame);
        }
    }

    private void OnReceiverState(ReceiverState state)
    {
        if (state == ReceiverState.Connected)
        {
            // A previous session may have left the phone layer hidden or dragged off the canvas.
            bool offCanvas;
            lock (Scene.Sync)
            {
                Scene.Phone.Visible = true;
                var p = Scene.Phone;
                offCanvas = p.CenterX + p.Width / 2 < 0 || p.CenterX - p.Width / 2 > Scene.CanvasWidth
                         || p.CenterY + p.Height / 2 < 0 || p.CenterY - p.Height / 2 > Scene.CanvasHeight;
            }
            if (offCanvas) Scene.ResetLayout();
            else Scene.NotifyChanged();
        }
        else
        {
            H264Decoder? old;
            lock (_decoderLock) { old = _decoder; _decoder = null; _firstPacketTimestamp = null; _lastPacketPts = -1; }
            old?.Dispose();
            if (_usbClient == null && !Engine.PhoneFrames.IsSimulated) Engine.PhoneFrames.Clear();
        }
        PhoneStateChanged?.Invoke();
    }

    public bool UsbConnected => _usbClient != null;
    public bool PhoneConnected => Receiver.State == ReceiverState.Connected || UsbConnected || Engine.PhoneFrames.IsSimulated;

    /// <summary>Pictures the Windows decoder has produced for the current iPhone session.</summary>
    public long PhoneFramesDecoded { get { lock (_decoderLock) return _decoder?.FramesDecoded ?? 0; } }
    public string? PhoneDecoderError { get { lock (_decoderLock) return _decoder?.Error; } }

    /// <summary>Recent AirPlay messages, newest last. Safe to call from the UI thread while the receiver is logging.</summary>
    public IReadOnlyList<string> ReceiverLogSnapshot()
    {
        lock (_receiverLogLock) return _receiverLog.ToArray();
    }

    private void ScheduleAutosave()
    {
        _autosave ??= new System.Threading.Timer(_ => SaveSession());
        _autosave.Change(1500, Timeout.Infinite);
    }

    public void SaveSession()
    {
        try { ProjectFile.Save(AppPaths.SessionFile, Scene); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Error("Autosave", e); }
    }

    public void Dispose()
    {
        _autosave?.Dispose();
        SaveSession();
        SaveSettings();
        try { StopUsbAsync().GetAwaiter().GetResult(); } catch (Exception e) { Log.Write("USB shutdown: " + e.Message); }
        Receiver.Dispose();
        lock (_decoderLock) { _decoder?.Dispose(); _decoder = null; }
        Engine.Dispose();
    }
}
