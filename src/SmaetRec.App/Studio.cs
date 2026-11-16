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
    // The start of the current iPhone session's video, kept in memory so it can be saved on request for diagnosis.
    private const int SampleLimitBytes = 12 * 1024 * 1024;
    private MemoryStream? _sample;
    private int _samplePackets;
    private DirectUsbCaptureClient? _usbClient;
    private volatile bool _usbStreaming;
    private volatile string _usbStatus = "USB is idle.";

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

    public async Task ConnectUsbAsync(string usbSerial, CancellationToken cancellationToken = default)
    {
        await StopUsbAsync().ConfigureAwait(false);
        _usbStatus = "Starting the native USB capture host.";
        var client = new DirectUsbCaptureClient(AppContext.BaseDirectory);
        client.VideoFrame += OnDirectUsbFrame;
        client.Status += message =>
        {
            _usbStatus = message;
            Log.Write("USB: " + message);
            PhoneStateChanged?.Invoke();
        };
        client.Failed += message =>
        {
            _usbStatus = "USB error: " + message;
            Log.Write(_usbStatus);
            PhoneStateChanged?.Invoke();
        };
        client.Disconnected += () =>
        {
            _usbStreaming = false;
            if (!_usbStatus.StartsWith("USB error:", StringComparison.Ordinal))
                _usbStatus = "USB capture disconnected.";
            PhoneStateChanged?.Invoke();
        };
        _usbClient = client;
        try { await client.StartAsync(usbSerial, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex)
        {
            _usbStatus = "USB startup failed: " + ex.Message;
            _usbClient = null;
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        PhoneStateChanged?.Invoke();
    }

    public async Task StopUsbAsync()
    {
        var client = _usbClient;
        _usbClient = null;
        _usbStreaming = false;
        _usbStatus = "USB capture stopped.";
        if (client != null) await client.DisposeAsync().ConfigureAwait(false);
        if (Receiver.State != ReceiverState.Connected && !Engine.PhoneFrames.IsSimulated) Engine.PhoneFrames.Clear();
        PhoneStateChanged?.Invoke();
    }

    private void OnDirectUsbFrame(byte[] bgra, int width, int height, long timestamp100ns)
    {
        // iPhoneMirror's GPL Core API already decodes QuickTime FEED samples to
        // BGRA. Forward decoded pixels directly; do not pretend they are H.264.
        bool becameStreaming = !_usbStreaming;
        _usbStreaming = true;
        _usbStatus = "USB video is live.";
        Engine.PhoneFrames.Submit(bgra, width, height);
        if (becameStreaming) PhoneStateChanged?.Invoke();
    }

    private void OnVideoPacket(VideoPacket packet)
    {
        lock (_decoderLock)
        {
            if (_decoder == null)
            {
                _decoder = new H264Decoder(Engine.PhoneFrames);
                _sample = null;
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
            if (_sample == null && packet.IsKeyFrame) { _sample = new MemoryStream(); _samplePackets = 0; }
            if (_sample != null && _sample.Length + packet.Data.Length + 13 <= SampleLimitBytes)
            {
                // Simple container: length, time, key flag, then the packet exactly as it goes to the decoder.
                Span<byte> head = stackalloc byte[13];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(head, packet.Data.Length);
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(head[4..], pts);
                head[12] = packet.IsKeyFrame ? (byte)1 : (byte)0;
                _sample.Write(head);
                _sample.Write(packet.Data);
                _samplePackets++;
            }
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
            if (old != null) AddReceiverLog("Decoder at end of session: " + old.Describe() + (old.Error is { } error ? " error=" + error : ""));
            old?.Dispose();
            if (!UsbSessionActive && !Engine.PhoneFrames.IsSimulated) Engine.PhoneFrames.Clear();
        }
        PhoneStateChanged?.Invoke();
    }

    public bool UsbSessionActive => _usbClient?.IsActive == true;
    public string UsbStatus => _usbStatus;
    public bool UsbConnected => _usbStreaming;
    public bool PhoneConnected => Receiver.State == ReceiverState.Connected || UsbConnected || Engine.PhoneFrames.IsSimulated;

    /// <summary>Pictures the Windows decoder has produced for the current iPhone session.</summary>
    public long PhoneFramesDecoded { get { lock (_decoderLock) return _decoder?.FramesDecoded ?? 0; } }
    public string? PhoneDecoderError { get { lock (_decoderLock) return _decoder?.Error; } }

    /// <summary>Counts, speed and picture layout of the Windows decoder for the current session, or null when no iPhone is sending.</summary>
    public string? PhoneDecoderStats { get { lock (_decoderLock) return _decoder?.Describe(); } }
    public long PhonePacketsDropped { get { lock (_decoderLock) return _decoder?.PacketsDropped ?? 0; } }

    private void AddReceiverLog(string message)
    {
        lock (_receiverLogLock)
        {
            _receiverLog.Enqueue($"{DateTime.Now:HH:mm:ss}  {message}");
            while (_receiverLog.Count > 120) _receiverLog.Dequeue();
        }
        Log.Write(message);
    }

    /// <summary>
    /// Writes what is needed to diagnose a bad iPhone picture into a new folder: the picture as it is shown now,
    /// the first seconds of the video exactly as received, and the log. Returns the folder, or null when no iPhone video has arrived.
    /// </summary>
    public string? SaveDiagnosticSample()
    {
        byte[]? video; int packets; string? stats;
        lock (_decoderLock) { video = _sample?.ToArray(); packets = _samplePackets; stats = _decoder?.Describe(); }
        bool hasPicture = Engine.PhoneFrames.TryCopyLatest(out var pixels, out int width, out int height);
        if (video == null && !hasPicture) return null;

        string folder = Path.Combine(AppPaths.DataFolder, "diagnostics", $"iphone-{DateTime.Now:yyyy-MM-dd HH-mm-ss}");
        Directory.CreateDirectory(folder);
        if (hasPicture)
        {
            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null, pixels, width * 4);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(folder, "iphone-picture.png"));
            encoder.Save(file);
        }
        if (video != null) File.WriteAllBytes(Path.Combine(folder, "iphone-video.smaetpkt"), video);
        var lines = new List<string>
        {
            $"SMAET REC {typeof(Studio).Assembly.GetName().Version} on {Environment.OSVersion}, {Environment.ProcessorCount} cores",
            $"Picture: {(hasPicture ? $"{width}x{height}" : "none")}; video sample: {packets} packets, {video?.Length ?? 0} bytes",
            $"Decoder: {stats ?? "not running"}",
            $"Receiver: packets={Receiver.MirrorPackets} pictures={Receiver.MirrorPictures} invalid={Receiver.MirrorInvalid} key={Receiver.MirrorKeyVariant}",
            "",
        };
        lines.AddRange(ReceiverLogSnapshot());
        File.WriteAllLines(Path.Combine(folder, "log.txt"), lines);
        return folder;
    }

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
