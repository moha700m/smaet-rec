using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace SmaetRec.AirPlay;

/// <summary>One DNS-SD service to announce: "instance._type._tcp.local" on a port with TXT key/values.</summary>
public sealed record MdnsService(string Instance, string Type, int Port, IReadOnlyList<(string Key, string Value)> Text);

/// <summary>Wire format for the small part of DNS that announcing a service needs.</summary>
public static class MdnsPacket
{
    public const ushort TypeA = 1, TypePtr = 12, TypeTxt = 16, TypeSrv = 33, TypeAny = 255;

    public static void WriteName(Stream s, params string[] labels)
    {
        foreach (var label in labels)
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            if (bytes.Length is 0 or > 63) throw new ArgumentException("Bad DNS label: " + label);
            s.WriteByte((byte)bytes.Length);
            s.Write(bytes);
        }
        s.WriteByte(0);
    }

    private static string[] Labels(string dotted) => dotted.Split('.', StringSplitOptions.RemoveEmptyEntries);

    private static void WriteRecord(Stream s, string[] name, ushort type, bool unique, uint ttl, Action<Stream> data)
    {
        WriteName(s, name);
        Span<byte> head = stackalloc byte[10];
        BinaryPrimitives.WriteUInt16BigEndian(head, type);
        BinaryPrimitives.WriteUInt16BigEndian(head[2..], (ushort)(unique ? 0x8001 : 0x0001)); // class IN, cache-flush for records we own alone
        BinaryPrimitives.WriteUInt32BigEndian(head[4..], ttl);
        using var rdata = new MemoryStream();
        data(rdata);
        BinaryPrimitives.WriteUInt16BigEndian(head[8..], (ushort)rdata.Length);
        s.Write(head);
        rdata.WriteTo(s);
    }

    /// <summary>Builds an unsolicited-style response carrying every record for the services. A TTL of zero says goodbye.</summary>
    public static byte[] BuildResponse(IReadOnlyList<MdnsService> services, string hostLabel, IPAddress address, bool goodbye = false)
    {
        using var body = new MemoryStream();
        int count = 0;
        uint longTtl = goodbye ? 0u : 4500u, shortTtl = goodbye ? 0u : 120u;
        string[] host = { hostLabel, "local" };
        foreach (var service in services)
        {
            string[] type = Labels(service.Type).Append("local").ToArray();
            string[] instance = new[] { service.Instance }.Concat(type).ToArray();
            WriteRecord(body, new[] { "_services", "_dns-sd", "_udp", "local" }, TypePtr, false, longTtl, d => WriteName(d, type)); count++;
            WriteRecord(body, type, TypePtr, false, longTtl, d => WriteName(d, instance)); count++;
            WriteRecord(body, instance, TypeSrv, true, shortTtl, d =>
            {
                Span<byte> head = stackalloc byte[6];
                BinaryPrimitives.WriteUInt16BigEndian(head[4..], (ushort)service.Port);
                d.Write(head);
                WriteName(d, host);
            }); count++;
            WriteRecord(body, instance, TypeTxt, true, longTtl, d =>
            {
                foreach (var (key, value) in service.Text)
                {
                    var bytes = Encoding.UTF8.GetBytes(key + "=" + value);
                    if (bytes.Length > 255) continue;
                    d.WriteByte((byte)bytes.Length);
                    d.Write(bytes);
                }
            }); count++;
        }
        WriteRecord(body, host, TypeA, true, shortTtl, d => d.Write(address.GetAddressBytes())); count++;

        var packet = new byte[12 + body.Length];
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), 0x8400); // response, authoritative
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(6), (ushort)count);
        body.ToArray().CopyTo(packet, 12);
        return packet;
    }

    /// <summary>Reads the questions of a query. Returns an empty list for responses or damaged packets.</summary>
    public static List<(string Name, ushort Type)> ReadQuestions(ReadOnlySpan<byte> packet)
    {
        var result = new List<(string, ushort)>();
        if (packet.Length < 12 || (packet[2] & 0x80) != 0) return result;
        int questions = BinaryPrimitives.ReadUInt16BigEndian(packet[4..]);
        int pos = 12;
        for (int i = 0; i < questions && i < 50; i++)
        {
            var name = ReadName(packet, ref pos);
            if (name == null || pos + 4 > packet.Length) return new();
            result.Add((name, BinaryPrimitives.ReadUInt16BigEndian(packet[pos..])));
            pos += 4;
        }
        return result;
    }

    private static string? ReadName(ReadOnlySpan<byte> packet, ref int pos)
    {
        var sb = new StringBuilder();
        int p = pos, jumps = 0;
        bool jumped = false;
        while (true)
        {
            if (p >= packet.Length) return null;
            int len = packet[p];
            if (len == 0) { p++; break; }
            if ((len & 0xC0) == 0xC0)
            {
                if (p + 1 >= packet.Length || ++jumps > 20) return null;
                if (!jumped) pos = p + 2;
                jumped = true;
                p = ((len & 0x3F) << 8) | packet[p + 1];
                continue;
            }
            if (len > 63 || p + 1 + len > packet.Length) return null;
            if (sb.Length > 0) sb.Append('.');
            sb.Append(Encoding.UTF8.GetString(packet.Slice(p + 1, len)));
            p += 1 + len;
        }
        if (!jumped) pos = p;
        return sb.ToString();
    }
}

/// <summary>
/// Announces services with multicast DNS on every IPv4 network adapter, and keeps watching for adapters that
/// appear later (such as the iPhone's USB network). Works alongside Windows' and Apple's own responders.
/// </summary>
public sealed class MdnsResponder : IDisposable
{
    private static readonly IPAddress Group = IPAddress.Parse("224.0.0.251");
    private static readonly IPEndPoint GroupEndPoint = new(Group, 5353);

    private sealed record Link(IPAddress Address, IPAddress Mask, Socket Socket);

    private readonly IReadOnlyList<MdnsService> _services;
    private readonly string _hostLabel;
    private readonly HashSet<string> _names;
    private readonly object _lock = new();
    private readonly Dictionary<IPAddress, Link> _links = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Action<string>? _log;

    public MdnsResponder(IReadOnlyList<MdnsService> services, string hostLabel, Action<string>? log = null)
    {
        _services = services; _hostLabel = hostLabel; _log = log;
        _names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "_services._dns-sd._udp.local", hostLabel + ".local" };
        foreach (var s in services) { _names.Add(s.Type + ".local"); _names.Add(s.Instance + "." + s.Type + ".local"); }
        Rescan();
        _ = WatchAdapters(_cts.Token);
    }

    public int AdapterCount { get { lock (_lock) return _links.Count; } }

    private static List<(IPAddress address, IPAddress mask)> LocalAddresses()
    {
        var list = new List<(IPAddress, IPAddress)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || !nic.SupportsMulticast) continue;
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                        list.Add((unicast.Address, unicast.IPv4Mask ?? IPAddress.Parse("255.255.255.0")));
            }
        }
        catch (NetworkInformationException) { }
        return list;
    }

    private void Rescan()
    {
        var current = LocalAddresses();
        lock (_lock)
        {
            foreach (var gone in _links.Keys.Where(k => current.All(c => !c.address.Equals(k))).ToList())
            {
                try { _links[gone].Socket.Dispose(); } catch (SocketException) { }
                _links.Remove(gone);
            }
            foreach (var (address, mask) in current)
            {
                if (_links.ContainsKey(address)) continue;
                try
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    socket.Bind(new IPEndPoint(IPAddress.Any, 5353));
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(Group, address));
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
                    var link = new Link(address, mask, socket);
                    _links[address] = link;
                    _ = Receive(link, _cts.Token);
                    _ = Announce(link, _cts.Token);
                    _log?.Invoke($"Announcing on {address}");
                }
                catch (SocketException e) { _log?.Invoke($"Cannot announce on {address}: {e.Message}"); }
            }
        }
    }

    private async Task WatchAdapters(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(4000, ct).ConfigureAwait(false);
                Rescan();
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task Announce(Link link, CancellationToken ct)
    {
        try
        {
            var packet = MdnsPacket.BuildResponse(_services, _hostLabel, link.Address);
            for (int i = 0; i < 3 && !ct.IsCancellationRequested; i++)
            {
                await link.Socket.SendToAsync(packet, SocketFlags.None, GroupEndPoint, ct).ConfigureAwait(false);
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }

    private static bool SameSubnet(IPAddress a, IPAddress b, IPAddress mask)
    {
        byte[] x = a.GetAddressBytes(), y = b.GetAddressBytes(), m = mask.GetAddressBytes();
        if (x.Length != 4 || y.Length != 4) return false;
        for (int i = 0; i < 4; i++) if ((x[i] & m[i]) != (y[i] & m[i])) return false;
        return true;
    }

    private async Task Receive(Link link, CancellationToken ct)
    {
        var buffer = new byte[9000];
        EndPoint any = new IPEndPoint(IPAddress.Any, 0);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await link.Socket.ReceiveFromAsync(buffer, SocketFlags.None, any, ct).ConfigureAwait(false);
                var from = ((IPEndPoint)result.RemoteEndPoint).Address;
                // Several of our sockets may see the same query; only the one on the asker's own network answers.
                bool single;
                lock (_lock) single = _links.Count == 1;
                if (!single && !SameSubnet(from, link.Address, link.Mask)) continue;
                var questions = MdnsPacket.ReadQuestions(buffer.AsSpan(0, result.ReceivedBytes));
                if (!questions.Any(q => _names.Contains(q.Name))) continue;
                await link.Socket.SendToAsync(MdnsPacket.BuildResponse(_services, _hostLabel, link.Address), SocketFlags.None, GroupEndPoint, ct).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var link in _links.Values)
            {
                try { link.Socket.SendTo(MdnsPacket.BuildResponse(_services, _hostLabel, link.Address, goodbye: true), GroupEndPoint); }
                catch (Exception e) when (e is SocketException or ObjectDisposedException) { }
            }
            _cts.Cancel();
            foreach (var link in _links.Values) link.Socket.Dispose();
            _links.Clear();
        }
    }
}
