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
    private System.Threading.Timer? _autosave;

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

    private void OnVideoPacket(VideoPacket packet)
    {
        lock (_decoderLock)
        {
            _decoder ??= new H264Decoder(Engine.PhoneFrames);
            _decoder.Push(packet.Data, packet.TimestampMicros, packet.IsKeyFrame);
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
            lock (_decoderLock) { old = _decoder; _decoder = null; }
            old?.Dispose();
            if (!Engine.PhoneFrames.IsSimulated) Engine.PhoneFrames.Clear();
        }
        PhoneStateChanged?.Invoke();
    }

    public bool PhoneConnected => Receiver.State == ReceiverState.Connected || Engine.PhoneFrames.IsSimulated;

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
        Receiver.Dispose();
        lock (_decoderLock) { _decoder?.Dispose(); _decoder = null; }
        Engine.Dispose();
    }
}
