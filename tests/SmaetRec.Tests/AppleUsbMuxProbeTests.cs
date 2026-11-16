using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SmaetRec.Media;

namespace SmaetRec.Tests;

public sealed class AppleUsbMuxProbeTests
{
    [Fact]
    public async Task EmptyDeviceListIsAValidAppleMuxResponse()
    {
        var result = await ReplyAsync("<plist><dict><key>DeviceList</key><array/></dict></plist>");
        Assert.True(result.Available);
        Assert.True(result.PortOccupied);
    }

    [Fact]
    public async Task PopulatedDeviceListAndFragmentedReplyAreAccepted()
    {
        var result = await ReplyAsync(
            "<plist><dict><key>DeviceList</key><array><dict>" +
            "<key>DeviceID</key><integer>1</integer></dict></array></dict></plist>",
            fragment: true);
        Assert.True(result.Available);
        Assert.True(result.PortOccupied);
    }

    [Theory]
    [InlineData("<plist><dict/></plist>")]
    [InlineData("<plist><dict><key>DeviceList</key><string>wrong</string></dict></plist>")]
    [InlineData("<plist><dict><key>DeviceList</key><array><string>wrong</string></array></dict></plist>")]
    [InlineData("HTTP/1.1 200 OK")]
    [InlineData("<plist><dict>")]
    public async Task NonMuxPayloadsAreNotReportedAsAppleSupport(string payload)
    {
        var result = await ReplyAsync(payload);
        Assert.False(result.Available);
        Assert.True(result.PortOccupied);
    }

    [Theory]
    [InlineData(4)] // usbmux version
    [InlineData(8)] // message type
    [InlineData(12)] // request tag
    public async Task InvalidMuxHeadersAreRejected(int fieldOffset)
    {
        var result = await ReplyAsync(
            "<plist><dict><key>DeviceList</key><array/></dict></plist>",
            invalidHeaderField: fieldOffset);
        Assert.False(result.Available);
        Assert.True(result.PortOccupied);
    }

    [Fact]
    public async Task SilentListenerIsBoundedAndNotMistakenForAppleMux()
    {
        var timer = Stopwatch.StartNew();
        var result = await ReplyAsync(null, stall: true, timeout: TimeSpan.FromMilliseconds(250));
        Assert.False(result.Available);
        Assert.True(result.PortOccupied);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ClosedPortIsNotReportedAsAConflict()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var result = await AppleUsbMuxProbe.ProbeAsync(port, TimeSpan.FromSeconds(1));
        Assert.False(result.Available);
        Assert.False(result.PortOccupied);
    }

    private static async Task<AppleUsbMuxStatus> ReplyAsync(
        string? xml, bool fragment = false, int invalidHeaderField = -1,
        bool stall = false, TimeSpan? timeout = null)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var serverDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));

        Task server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync(serverDeadline.Token);
            NetworkStream stream = peer.GetStream();
            byte[] requestHeader = new byte[16];
            await stream.ReadExactlyAsync(requestHeader, serverDeadline.Token);
            uint requestLength = BinaryPrimitives.ReadUInt32LittleEndian(requestHeader);
            Assert.InRange(requestLength, 17u, 4096u);
            Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(requestHeader.AsSpan(4)));
            Assert.Equal(8u, BinaryPrimitives.ReadUInt32LittleEndian(requestHeader.AsSpan(8)));
            Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(requestHeader.AsSpan(12)));

            byte[] request = new byte[requestLength - 16];
            await stream.ReadExactlyAsync(request, serverDeadline.Token);
            Assert.Contains("ListDevices", Encoding.UTF8.GetString(request), StringComparison.Ordinal);

            if (stall)
            {
                byte[] probe = new byte[1];
                Assert.Equal(0, await stream.ReadAsync(probe, serverDeadline.Token));
                return;
            }

            byte[] payload = Encoding.UTF8.GetBytes(xml ?? string.Empty);
            byte[] responseHeader = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(responseHeader, checked((uint)(16 + payload.Length)));
            BinaryPrimitives.WriteUInt32LittleEndian(responseHeader.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(responseHeader.AsSpan(8), 8);
            BinaryPrimitives.WriteUInt32LittleEndian(responseHeader.AsSpan(12), 1);
            if (invalidHeaderField >= 0)
                BinaryPrimitives.WriteUInt32LittleEndian(responseHeader.AsSpan(invalidHeaderField), 77);

            byte[] response = responseHeader.Concat(payload).ToArray();
            if (fragment)
            {
                foreach (byte[] part in response.Chunk(7))
                    await stream.WriteAsync(part, serverDeadline.Token);
            }
            else
            {
                await stream.WriteAsync(response, serverDeadline.Token);
            }
        }, serverDeadline.Token);

        var result = await AppleUsbMuxProbe.ProbeAsync(
            port, timeout ?? TimeSpan.FromSeconds(2));
        await server.WaitAsync(TimeSpan.FromSeconds(5));
        return result;
    }
}
