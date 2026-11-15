using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Math.EC.Rfc7748;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace SmaetRec.AirPlay;

public enum ReceiverState { Stopped, Waiting, Connected, Failed }

/// <summary>
/// AirPlay screen-mirroring receiver. Advertises this computer on the local network (all interfaces,
/// including the iPhone's USB network), accepts one iPhone at a time and raises decodable H.264 packets.
/// </summary>
public sealed class AirPlayReceiver : IDisposable
{
    private const string Features = "0x5A7FFEE6";
    private const long FeaturesValue = 0x5A7FFEE6;
    private const string Model = "AppleTV3,2";
    private const string SourceVersion = "220.68";
    private const string PairingId = "2e388006-13ba-4041-9a67-25dd4a43d536";

    private readonly object _lock = new();
    private readonly byte[] _edPrivate = new byte[32];
    private readonly byte[] _edPublic = new byte[32];
    private CancellationTokenSource? _cts;
    private TcpListener? _control, _mirror, _events;
    private MdnsResponder? _mdns;
    private Session? _mirrorSession;
    private int _activeMirrorStreams;

    public string Name { get; private set; } = "SMAET REC";
    public string DeviceId { get; private set; } = "";
    public int ControlPort { get; private set; }
    public ReceiverState State { get; private set; } = ReceiverState.Stopped;
    public string? LastError { get; private set; }
    /// <summary>Display size offered to the iPhone; it scales its picture to fit inside.</summary>
    public int DisplayWidth { get; set; } = 2560;
    public int DisplayHeight { get; set; } = 1440;

    public event Action<ReceiverState>? StateChanged;
    public event Action<VideoPacket>? VideoPacket;
    public event Action<string>? Log;

    public AirPlayReceiver()
    {
        RandomNumberGenerator.Fill(_edPrivate);
        Ed25519.GeneratePublicKey(_edPrivate, 0, _edPublic, 0);
    }

    public byte[] PublicKey => (byte[])_edPublic.Clone();

    public void Start(string name, bool advertise = true)
    {
        Stop();
        lock (_lock)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "SMAET REC" : name.Trim();
            while (System.Text.Encoding.UTF8.GetByteCount(Name) > 40) Name = Name[..^1];
            DeviceId = FindDeviceId();
            _cts = new CancellationTokenSource();
            try
            {
                _control = StartListener(7000);
                _mirror = StartListener(7100);
                _events = StartListener(0);
                ControlPort = ((IPEndPoint)_control.LocalEndpoint).Port;
                var ct = _cts.Token;
                _ = AcceptLoop(_control, HandleControlAsync, ct);
                _ = AcceptLoop(_mirror, HandleMirrorAsync, ct);
                _ = AcceptLoop(_events, HoldOpenAsync, ct);
                if (advertise) StartAdvertising();
                LastError = null;
                Log?.Invoke($"AirPlay receiver '{Name}' listening on port {ControlPort}, device {DeviceId}");
            }
            catch (Exception e) when (e is SocketException or IOException or InvalidOperationException)
            {
                LastError = e.Message;
                Log?.Invoke("AirPlay receiver failed to start: " + e);
                StopCore();
                SetState(ReceiverState.Failed);
                return;
            }
        }
        SetState(ReceiverState.Waiting);
    }

    public void Stop()
    {
        lock (_lock) StopCore();
        SetState(ReceiverState.Stopped);
    }

    private void StopCore()
    {
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        foreach (var l in new[] { _control, _mirror, _events })
            try { l?.Stop(); } catch (SocketException) { }
        _control = _mirror = _events = null;
        _mdns?.Dispose();
        _mdns = null; _cts = null; _mirrorSession = null;
        Interlocked.Exchange(ref _activeMirrorStreams, 0);
    }

    public void Dispose() => Stop();

    private void SetState(ReceiverState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(state);
    }

    private static TcpListener StartListener(int preferredPort)
    {
        try
        {
            var l = new TcpListener(IPAddress.Any, preferredPort);
            l.Start();
            return l;
        }
        catch (SocketException) when (preferredPort != 0)
        {
            var l = new TcpListener(IPAddress.Any, 0);
            l.Start();
            return l;
        }
    }

    private static string FindDeviceId()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var mac = nic.GetPhysicalAddress().GetAddressBytes();
                if (mac.Length == 6 && mac.Any(b => b != 0)) return string.Join(":", mac.Select(b => b.ToString("X2")));
            }
        }
        catch (NetworkInformationException) { }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Environment.MachineName));
        hash[0] = (byte)((hash[0] | 0x02) & 0xFE);
        return string.Join(":", hash.Take(6).Select(b => b.ToString("X2")));
    }

    private void StartAdvertising()
    {
        string pk = Convert.ToHexString(_edPublic).ToLowerInvariant();
        string id = DeviceId.Replace(":", "");
        var raop = new MdnsService($"{id}@{Name}", "_raop._tcp", ControlPort, new[]
        {
            ("ch", "2"), ("cn", "0,1,2,3"), ("da", "true"), ("et", "0,3,5"), ("vv", "2"), ("ft", Features + ",0x0"), ("am", Model),
            ("md", "0,1,2"), ("rhd", "5.6.0.0"), ("pw", "false"), ("sr", "44100"), ("ss", "16"), ("sv", "false"),
            ("tp", "UDP"), ("txtvers", "1"), ("sf", "0x4"), ("vs", SourceVersion), ("vn", "65537"), ("pk", pk),
        });
        var airplay = new MdnsService(Name, "_airplay._tcp", ControlPort, new[]
        {
            ("deviceid", DeviceId), ("features", Features + ",0x0"), ("pw", "false"), ("flags", "0x4"), ("model", Model), ("pk", pk),
            ("pi", PairingId), ("srcvers", SourceVersion), ("vv", "2"),
        });
        _mdns = new MdnsResponder(new[] { raop, airplay }, "smaet-rec-" + id.ToLowerInvariant()[6..], m => Log?.Invoke(m));
        if (_mdns.AdapterCount == 0) Log?.Invoke("No network adapter to announce on yet; will keep looking");
    }

    /// <summary>Number of network adapters the receiver is currently announced on (0 means the iPhone cannot find it yet).</summary>
    public int AnnouncedAdapters => _mdns?.AdapterCount ?? 0;

    private async Task AcceptLoop(TcpListener listener, Func<TcpClient, CancellationToken, Task> handler, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { return; }
            client.NoDelay = true;
            _ = Task.Run(async () =>
            {
                try { await handler(client, ct).ConfigureAwait(false); }
                catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
                catch (Exception e) { Log?.Invoke("AirPlay connection error: " + e); }
                finally { client.Dispose(); }
            }, ct);
        }
    }

    private static async Task HoldOpenAsync(TcpClient client, CancellationToken ct)
    {
        var buffer = new byte[1024];
        var stream = client.GetStream();
        while (await stream.ReadAsync(buffer, ct).ConfigureAwait(false) > 0) { }
    }

    /// <summary>Per-connection pairing and stream state.</summary>
    private sealed class Session
    {
        public byte[]? EcdhOurs, EcdhTheirs, EdTheirs, EcdhShared, KeyMessage, EncryptedKey;
        public AesCtrStream? PairCipher;
        public bool PairVerified;
        public ulong StreamConnectionId;
        public IPAddress? Remote;
        public List<UdpClient> Udp = new();
        public CancellationTokenSource? TimingCts;
    }

    private async Task HandleControlAsync(TcpClient client, CancellationToken ct)
    {
        var session = new Session { Remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address };
        var stream = client.GetStream();
        var reader = new RtspReader(stream);
        Log?.Invoke($"iPhone control connection from {session.Remote}");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var request = await reader.ReadAsync(ct).ConfigureAwait(false);
                if (request == null) break;
                var response = new RtspResponse();
                try { Handle(session, request, response); }
                catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException or IndexOutOfRangeException)
                {
                    Log?.Invoke($"Bad {request.Method} {request.Path}: {e.Message}");
                    response.Status = 400; response.Reason = "Bad Request";
                }
                await stream.WriteAsync(response.ToBytes(request), ct).ConfigureAwait(false);
            }
        }
        finally
        {
            session.TimingCts?.Cancel();
            foreach (var u in session.Udp) u.Dispose();
            session.PairCipher?.Dispose();
            lock (_lock) if (_mirrorSession == session) _mirrorSession = null;
            Log?.Invoke("iPhone control connection closed");
        }
    }

    private void Handle(Session s, RtspRequest req, RtspResponse res)
    {
        string path = req.Path.Split('?')[0];
        switch (req.Method)
        {
            case "GET" when path == "/info":
                res.SetBody(BinaryPlist.Write(BuildInfo()), "application/x-apple-binary-plist");
                break;
            case "POST" when path == "/pair-setup":
                res.SetBody((byte[])_edPublic.Clone(), "application/octet-stream");
                break;
            case "POST" when path == "/pair-verify":
                PairVerify(s, req, res);
                break;
            case "POST" when path == "/fp-setup":
                FairPlaySetup(s, req, res);
                break;
            case "SETUP":
                Setup(s, req, res);
                break;
            case "GET_PARAMETER":
                if (Encoding.ASCII.GetString(req.Body).StartsWith("volume", StringComparison.Ordinal))
                    res.SetBody(Encoding.ASCII.GetBytes("volume: 0.000000\r\n"), "text/parameters");
                break;
            case "RECORD":
                res.Headers["Audio-Latency"] = "11025";
                res.Headers["Audio-Jack-Status"] = "connected; type=analog";
                break;
            case "OPTIONS":
                res.Headers["Public"] = "SETUP, RECORD, PAUSE, FLUSH, TEARDOWN, OPTIONS, GET_PARAMETER, SET_PARAMETER";
                break;
            // SET_PARAMETER, FLUSH, TEARDOWN, POST /feedback, /command, /audioMode: acknowledge with an empty 200.
        }
    }

    private Dictionary<string, object> BuildInfo() => new()
    {
        ["audioFormats"] = new List<object>
        {
            new Dictionary<string, object> { ["type"] = 100, ["audioInputFormats"] = 67108860, ["audioOutputFormats"] = 67108860 },
            new Dictionary<string, object> { ["type"] = 101, ["audioInputFormats"] = 67108860, ["audioOutputFormats"] = 67108860 },
        },
        ["audioLatencies"] = new List<object>
        {
            new Dictionary<string, object> { ["type"] = 100, ["audioType"] = "default", ["inputLatencyMicros"] = 0, ["outputLatencyMicros"] = 0 },
            new Dictionary<string, object> { ["type"] = 101, ["audioType"] = "default", ["inputLatencyMicros"] = 0, ["outputLatencyMicros"] = 0 },
        },
        ["deviceID"] = DeviceId,
        ["displays"] = new List<object>
        {
            new Dictionary<string, object>
            {
                ["uuid"] = "e0ff8a27-6738-3d56-8a16-cc53aacee925",
                ["widthPhysical"] = 0, ["heightPhysical"] = 0,
                ["width"] = DisplayWidth, ["height"] = DisplayHeight,
                ["widthPixels"] = DisplayWidth, ["heightPixels"] = DisplayHeight,
                ["rotation"] = false, ["refreshRate"] = 1.0 / 60.0, ["maxFPS"] = 60, ["overscanned"] = false, ["features"] = 14,
            },
        },
        ["features"] = FeaturesValue,
        ["keepAliveLowPower"] = true,
        ["keepAliveSendStatsAsBody"] = true,
        ["macAddress"] = DeviceId,
        ["model"] = Model,
        ["name"] = Name,
        ["pi"] = PairingId,
        ["pk"] = (byte[])_edPublic.Clone(),
        ["sourceVersion"] = SourceVersion,
        ["statusFlags"] = 4,
        ["vv"] = 2,
    };

    private void PairVerify(Session s, RtspRequest req, RtspResponse res)
    {
        var body = req.Body;
        if (body.Length < 68) throw new FormatException("pair-verify body too short");
        if (body[0] != 0)
        {
            s.EcdhTheirs = body[4..36];
            s.EdTheirs = body[36..68];
            var ourPrivate = new byte[32];
            RandomNumberGenerator.Fill(ourPrivate);
            s.EcdhOurs = new byte[32];
            X25519.ScalarMultBase(ourPrivate, 0, s.EcdhOurs, 0);
            s.EcdhShared = new byte[32];
            X25519.ScalarMult(ourPrivate, 0, s.EcdhTheirs, 0, s.EcdhShared, 0);

            s.PairCipher?.Dispose();
            s.PairCipher = new AesCtrStream(
                Hashing.Sha512Prefix16("Pair-Verify-AES-Key", s.EcdhShared),
                Hashing.Sha512Prefix16("Pair-Verify-AES-IV", s.EcdhShared));

            var message = new byte[64];
            s.EcdhOurs.CopyTo(message, 0);
            s.EcdhTheirs.CopyTo(message, 32);
            var signature = new byte[64];
            Ed25519.Sign(_edPrivate, 0, message, 0, message.Length, signature, 0);
            s.PairCipher.Process(signature);

            var output = new byte[96];
            s.EcdhOurs.CopyTo(output, 0);
            signature.CopyTo(output, 32);
            res.SetBody(output, "application/octet-stream");
        }
        else
        {
            if (s.PairCipher == null || s.EcdhOurs == null || s.EcdhTheirs == null || s.EdTheirs == null)
                throw new FormatException("pair-verify out of order");
            var signature = body[4..68];
            s.PairCipher.Process(signature);
            var message = new byte[64];
            s.EcdhTheirs.CopyTo(message, 0);
            s.EcdhOurs.CopyTo(message, 32);
            s.PairVerified = Ed25519.Verify(signature, 0, s.EdTheirs, 0, message, 0, message.Length);
            Log?.Invoke("Pairing verified: " + s.PairVerified);
            if (!s.PairVerified) { res.Status = 470; res.Reason = "Connection Authorization Required"; }
        }
    }

    private static readonly byte[] FpHeader = { 0x46, 0x50, 0x4c, 0x59, 0x03, 0x01, 0x04, 0x00, 0x00, 0x00, 0x00, 0x14 };

    private void FairPlaySetup(Session s, RtspRequest req, RtspResponse res)
    {
        var body = req.Body;
        if (body.Length < 16 || body[4] != 0x03) throw new FormatException("Unsupported FairPlay version");
        if (body.Length == 16)
        {
            int mode = body[14];
            if (mode > 3) throw new FormatException("Unsupported FairPlay mode");
            res.SetBody(FairPlayReplies.Get(mode), "application/octet-stream");
        }
        else if (body.Length == 164)
        {
            s.KeyMessage = (byte[])body.Clone();
            var output = new byte[32];
            FpHeader.CopyTo(output, 0);
            Array.Copy(body, 144, output, 12, 20);
            res.SetBody(output, "application/octet-stream");
        }
        else throw new FormatException("Unexpected fp-setup length " + body.Length);
    }

    private void Setup(Session s, RtspRequest req, RtspResponse res)
    {
        if (BinaryPlist.Read(req.Body) is not Dictionary<string, object?> plist) throw new FormatException("SETUP without plist");
        var reply = new Dictionary<string, object>();

        if (plist.TryGetValue("ekey", out var ekey) && ekey is byte[] key)
        {
            s.EncryptedKey = key;
            int timingPort = plist.TryGetValue("timingPort", out var tp) && tp is long t ? (int)t : 0;
            var timing = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            s.Udp.Add(timing);
            if (timingPort > 0 && s.Remote != null)
            {
                s.TimingCts = new CancellationTokenSource();
                _ = TimingLoop(timing, new IPEndPoint(s.Remote, timingPort), s.TimingCts.Token);
            }
            reply["eventPort"] = ((IPEndPoint)_events!.LocalEndpoint).Port;
            reply["timingPort"] = ((IPEndPoint)timing.Client.LocalEndPoint!).Port;
        }

        if (plist.TryGetValue("streams", out var streamsObj) && streamsObj is List<object?> streams)
        {
            var replies = new List<object>();
            foreach (var item in streams)
            {
                if (item is not Dictionary<string, object?> stream || stream.GetValueOrDefault("type") is not long type) continue;
                if (type == 110)
                {
                    s.StreamConnectionId = unchecked((ulong)(long)stream["streamConnectionID"]!);
                    lock (_lock) _mirrorSession = s;
                    replies.Add(new Dictionary<string, object> { ["type"] = 110, ["dataPort"] = ((IPEndPoint)_mirror!.LocalEndpoint).Port });
                }
                else if (type == 96)
                {
                    // Audio is accepted so the iPhone is happy, but the packets are not decoded.
                    var data = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
                    var control = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
                    s.Udp.Add(data); s.Udp.Add(control);
                    replies.Add(new Dictionary<string, object>
                    {
                        ["type"] = 96,
                        ["dataPort"] = ((IPEndPoint)data.Client.LocalEndPoint!).Port,
                        ["controlPort"] = ((IPEndPoint)control.Client.LocalEndPoint!).Port,
                    });
                }
            }
            reply["streams"] = replies;
        }
        res.SetBody(BinaryPlist.Write(reply), "application/x-apple-binary-plist");
    }

    /// <summary>Periodic NTP-style timing queries to the iPhone, as an Apple TV would send.</summary>
    private static async Task TimingLoop(UdpClient socket, IPEndPoint remote, CancellationToken ct)
    {
        var packet = new byte[32];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Array.Clear(packet);
                packet[0] = 0x80; packet[1] = 0xd2; packet[2] = 0x00; packet[3] = 0x07;
                var now = DateTime.UtcNow - new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                uint seconds = (uint)now.TotalSeconds;
                uint fraction = (uint)((now.TotalSeconds - Math.Floor(now.TotalSeconds)) * uint.MaxValue);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(24), seconds);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(28), fraction);
                await socket.SendAsync(packet, remote, ct).ConfigureAwait(false);
                await Task.Delay(3000, ct).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }

    private async Task HandleMirrorAsync(TcpClient client, CancellationToken ct)
    {
        Session? s;
        lock (_lock) s = _mirrorSession;
        if (s?.KeyMessage == null || s.EncryptedKey == null)
        {
            Log?.Invoke("Mirror data connection without a completed setup; closing");
            return;
        }
        using var decoder = MirrorStreamDecoder.Create(s.KeyMessage, s.EncryptedKey, s.EcdhShared, s.StreamConnectionId);
        var stream = client.GetStream();
        var header = new byte[MirrorStreamDecoder.HeaderSize];
        Interlocked.Increment(ref _activeMirrorStreams);
        SetState(ReceiverState.Connected);
        Log?.Invoke("iPhone mirroring started");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!await stream.ReadFullyAsync(header, header.Length, ct).ConfigureAwait(false)) break;
                var (size, _) = MirrorStreamDecoder.ParseHeader(header);
                if (size < 0 || size > 32 * 1024 * 1024) { Log?.Invoke("Mirror stream out of sync; closing"); break; }
                var payload = new byte[size];
                if (size > 0 && !await stream.ReadFullyAsync(payload, size, ct).ConfigureAwait(false)) break;
                var packet = decoder.Handle(header, payload);
                if (packet != null) VideoPacket?.Invoke(packet);
            }
        }
        finally
        {
            Log?.Invoke("iPhone mirroring stopped");
            if (Interlocked.Decrement(ref _activeMirrorStreams) <= 0 && State == ReceiverState.Connected)
                SetState(ReceiverState.Waiting);
        }
    }
}
