using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SmaetRec.Media;

/// <summary>Read-only result of speaking Apple's usbmux ListDevices protocol.</summary>
public sealed record AppleUsbMuxStatus(bool Available, bool PortOccupied, string? Listener = null);

/// <summary>
/// Checks the actual local usbmux protocol instead of assuming that an open TCP
/// port or an installed Apple service means the device transport is usable.
/// This probe sends only ListDevices; it never pairs, changes drivers or starts
/// or stops services.
/// </summary>
public static class AppleUsbMuxProbe
{
    public const int DefaultPort = 27015;
    private const uint MaximumPacketBytes = 1024 * 1024;
    private const uint ErrorInsufficientBuffer = 122;

    public static AppleUsbMuxStatus Inspect(TimeSpan? timeout = null)
    {
        if (!OperatingSystem.IsWindows())
            return new(false, false, "Windows-only probe");

        var probe = ProbeAsync(DefaultPort, timeout ?? TimeSpan.FromMilliseconds(900))
            .GetAwaiter().GetResult();
        var listener = FindListener(DefaultPort);
        return probe with
        {
            PortOccupied = probe.PortOccupied || listener is not null,
            Listener = listener
        };
    }

    /// <summary>
    /// Runs a bounded, read-only usbmux ListDevices request. Exposed so the
    /// protocol parser can be tested against local fake servers without an iPhone.
    /// </summary>
    public static async Task<AppleUsbMuxStatus> ProbeAsync(
        int port, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(15))
            throw new ArgumentOutOfRangeException(nameof(timeout));

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using var client = new TcpClient(AddressFamily.InterNetwork);
        var connected = false;
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, deadline.Token).ConfigureAwait(false);
            connected = true;

            byte[] payload = Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><plist version=\"1.0\"><dict>" +
                "<key>MessageType</key><string>ListDevices</string>" +
                "<key>ClientVersionString</key><string>SMAET REC diagnostic</string>" +
                "<key>ProgName</key><string>SMAET REC</string>" +
                "<key>kLibUSBMuxVersion</key><integer>3</integer></dict></plist>");

            byte[] header = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(header, checked((uint)(header.Length + payload.Length)));
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 1); // usbmux version
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), 8); // plist message
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 1); // request tag

            NetworkStream stream = client.GetStream();
            await stream.WriteAsync(header, deadline.Token).ConfigureAwait(false);
            await stream.WriteAsync(payload, deadline.Token).ConfigureAwait(false);

            await stream.ReadExactlyAsync(header, deadline.Token).ConfigureAwait(false);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(header);
            bool validHeader =
                length is > 16 and <= MaximumPacketBytes &&
                BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)) == 1 &&
                BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8)) == 8 &&
                BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12)) == 1;
            if (!validHeader) return new(false, true);

            byte[] response = new byte[length - 16];
            await stream.ReadExactlyAsync(response, deadline.Token).ConfigureAwait(false);
            return new(IsValidDeviceList(response), true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, connected);
        }
        catch (Exception error) when (error is SocketException or IOException or XmlException or
            InvalidDataException or ObjectDisposedException)
        {
            return new(false, connected);
        }
    }

    private static bool IsValidDeviceList(byte[] payload)
    {
        if (payload.Length == 0) return false;
        using var input = new MemoryStream(payload, writable: false);
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumPacketBytes
        });
        var document = XDocument.Load(reader);
        if (document.Root?.Name != "plist") return false;

        var entries = document.Root.Element("dict")?.Elements().ToArray();
        if (entries is null || entries.Length % 2 != 0) return false;
        for (int i = 0; i < entries.Length; i += 2)
        {
            if (entries[i].Name != "key" || entries[i].Value != "DeviceList") continue;
            return entries[i + 1].Name == "array" &&
                entries[i + 1].Elements().All(entry => entry.Name == "dict");
        }
        return false;
    }

    private static string? FindListener(int port)
    {
        const int addressFamilyIpv4 = 2;
        const int tcpTableOwnerPidListeners = 3;
        uint size = 0;
        try
        {
            if (GetExtendedTcpTable(IntPtr.Zero, ref size, false, addressFamilyIpv4,
                    tcpTableOwnerPidListeners, 0) != ErrorInsufficientBuffer)
                return null;

            for (int attempt = 0; attempt < 3 && size is >= 4 and <= 16 * 1024 * 1024; attempt++)
            {
                uint capacity = size;
                IntPtr table = Marshal.AllocHGlobal(checked((int)capacity));
                try
                {
                    uint result = GetExtendedTcpTable(table, ref size, false, addressFamilyIpv4,
                        tcpTableOwnerPidListeners, 0);
                    if (result == ErrorInsufficientBuffer) continue;
                    if (result != 0) return null;

                    int count = Marshal.ReadInt32(table);
                    const int rowSize = 24; // MIB_TCPROW_OWNER_PID on IPv4
                    if (count < 0 || count > (capacity - 4) / rowSize) return null;

                    for (int row = 0; row < count; row++)
                    {
                        IntPtr entry = IntPtr.Add(table, checked(4 + row * rowSize));
                        uint localAddress = unchecked((uint)Marshal.ReadInt32(entry, 4));
                        ushort localPort = BinaryPrimitives.ReverseEndianness(
                            unchecked((ushort)Marshal.ReadInt32(entry, 8)));
                        if (localPort != port || (localAddress != 0 && localAddress != 0x0100007f))
                            continue;

                        int processId = Marshal.ReadInt32(entry, 20);
                        try
                        {
                            using var process = Process.GetProcessById(processId);
                            return $"{process.ProcessName}.exe (PID {processId})";
                        }
                        catch (Exception error) when (error is ArgumentException or
                            InvalidOperationException or System.ComponentModel.Win32Exception)
                        {
                            return $"PID {processId}";
                        }
                    }
                    return null;
                }
                finally { Marshal.FreeHGlobal(table); }
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        catch (System.ComponentModel.Win32Exception) { }
        return null;
    }

    [DllImport("iphlpapi.dll", SetLastError = false)]
    private static extern uint GetExtendedTcpTable(
        IntPtr table, ref uint size, [MarshalAs(UnmanagedType.Bool)] bool order,
        int addressFamily, int tableClass, uint reserved);
}
