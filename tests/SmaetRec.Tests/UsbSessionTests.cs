using System.Buffers.Binary;
using System.Text;
using SmaetRec.Media;
using Xunit;

namespace SmaetRec.Tests;

/// <summary>The stream of records the native USB host sends: statuses, pictures, errors, stop.</summary>
public sealed class UsbIpcStreamTests
{
    private static byte[] Record(string json, byte[]? payload = null)
    {
        var header = Encoding.UTF8.GetBytes(json);
        payload ??= Array.Empty<byte>();
        var bytes = new byte[8 + header.Length + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 4 + header.Length + payload.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), header.Length);
        header.CopyTo(bytes, 8);
        payload.CopyTo(bytes, 8 + header.Length);
        return bytes;
    }

    private static byte[] Video(int w, int h, byte fill, long ts) =>
        Record($$"""{"type":"video","pixelFormat":"bgra8","width":{{w}},"height":{{h}},"stride":{{w * 4}},"payloadBytes":{{w * h * 4}},"timestamp100ns":{{ts}}}""",
            Enumerable.Repeat(fill, w * h * 4).ToArray());

    /// <summary>Hands data out a few bytes at a time, as a pipe does.</summary>
    private sealed class Trickle(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            base.ReadAsync(buffer[..Math.Min(7, buffer.Length)], ct);
    }

    [Fact]
    public async Task Statuses_and_pictures_arrive_in_order_and_pictures_land_in_the_rented_buffer()
    {
        var stream = new Trickle(Record("""{"type":"status","message":"QuickTime handshake"}""")
            .Concat(Video(3, 2, 0x55, 111)).Concat(Video(2, 2, 0x66, 222))
            .Concat(Record("""{"type":"stopped"}""")).Concat(new byte[] { 9, 9, 9 }).ToArray());
        var statuses = new List<string>(); var frames = new List<(byte[] px, int w, int h, long ts)>();
        var rented = new List<byte[]>();
        await DirectUsbCaptureClient.ReadRecordsAsync(stream, n => { var b = new byte[n + 100]; rented.Add(b); return b; },
            statuses.Add, (px, w, h, ts) => frames.Add((px, w, h, ts)), default);

        Assert.Equal(new[] { "QuickTime handshake" }, statuses);
        Assert.Equal(2, frames.Count);
        Assert.Same(rented[0], frames[0].px);                         // no second copy of the picture
        Assert.Equal((3, 2, 111L), (frames[0].w, frames[0].h, frames[0].ts));
        Assert.All(frames[0].px.Take(24), b => Assert.Equal(0x55, b));
        Assert.Equal(0, frames[0].px[24]);                            // only the picture's bytes were written
        Assert.Equal((2, 2, 222L), (frames[1].w, frames[1].h, frames[1].ts));
        Assert.Equal(0x66, frames[1].px[15]);
    }

    [Fact]
    public async Task Host_error_is_reported_with_its_own_words()
    {
        var stream = new MemoryStream(Record("""{"type":"error","message":"The iPhone is locked (stage: pairing)"}"""));
        var error = await Assert.ThrowsAsync<IOException>(() =>
            DirectUsbCaptureClient.ReadRecordsAsync(stream, null, _ => { }, (_, _, _, _) => { }, default));
        Assert.Equal("The iPhone is locked (stage: pairing)", error.Message);
    }

    [Fact]
    public async Task Host_that_dies_mid_picture_is_an_end_of_stream_not_a_fake_frame()
    {
        var bytes = Video(4, 4, 1, 1);
        var stream = new MemoryStream(bytes[..(bytes.Length - 10)]);
        int frames = 0;
        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            DirectUsbCaptureClient.ReadRecordsAsync(stream, null, _ => { }, (_, _, _, _) => frames++, default));
        Assert.Equal(0, frames);
    }

    [Theory]
    [InlineData("""{"type":"video","pixelFormat":"nv12","width":2,"height":2,"stride":8,"payloadBytes":16,"timestamp100ns":1}""", 16)]   // wrong format
    [InlineData("""{"type":"video","pixelFormat":"bgra8","width":2,"height":2,"stride":9,"payloadBytes":18,"timestamp100ns":1}""", 18)]  // padded rows
    [InlineData("""{"type":"video","pixelFormat":"bgra8","width":2,"height":2,"stride":8,"payloadBytes":16,"timestamp100ns":1}""", 12)]  // short payload
    [InlineData("""{"type":"video","pixelFormat":"bgra8","width":0,"height":2,"stride":0,"payloadBytes":0,"timestamp100ns":1}""", 0)]    // empty picture
    [InlineData("""{"type":"video","pixelFormat":"bgra8","width":70000,"height":2,"stride":280000,"payloadBytes":560000,"timestamp100ns":1}""", 0)]
    [InlineData("""{"type":"surprise"}""", 0)]
    [InlineData("""{"type":"status","message":"x"}""", 5)]                                                                             // status with payload
    public async Task Malformed_records_are_rejected(string json, int payloadBytes)
    {
        var stream = new MemoryStream(Record(json, new byte[payloadBytes]));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            DirectUsbCaptureClient.ReadRecordsAsync(stream, null, _ => { }, (_, _, _, _) => { }, default));
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(100_000_000, 10)]   // over the 64 MiB limit
    [InlineData(40, 0)]
    [InlineData(40, 200)]           // header longer than the record
    public async Task Impossible_lengths_are_rejected_before_anything_is_allocated(int recordLength, int headerLength)
    {
        var prefix = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, recordLength);
        BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(4), headerLength);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            DirectUsbCaptureClient.ReadRecordsAsync(new MemoryStream(prefix), null, _ => { }, (_, _, _, _) => { }, default));
    }

    [Fact]
    public async Task Starting_without_the_host_program_fails_clearly_and_can_be_retried()
    {
        if (!OperatingSystem.IsWindows()) return;   // the client refuses to run elsewhere; covered by Windows CI
        string empty = Path.Combine(Path.GetTempPath(), "smaet-nohost-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            await using var client = new DirectUsbCaptureClient(empty);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                await Assert.ThrowsAsync<FileNotFoundException>(() => client.StartAsync("00008140-000123456789ABCD"));
                Assert.False(client.IsActive);
            }
            await Assert.ThrowsAsync<ArgumentException>(() => client.StartAsync("bad\nserial"));
        }
        finally { Directory.Delete(empty, true); }
    }
}

public sealed class ThreeUToolsWindowTests
{
    private static CaptureTarget Window(long handle, string title, string process = "3uTools") =>
        new(CaptureKind.Window, new IntPtr(handle), title, 0, 0, 400, 800, false) { ProcessName = process };

    [Fact]
    public void Viewer_is_recognised_by_title_across_versions_and_by_process()
    {
        Assert.True(Window(1, "Realtime Screen").IsLikely3uToolsWindow);
        Assert.True(Window(1, "3uTools - Real-time Screen (iPhone)", "other").IsLikely3uToolsWindow);
        Assert.True(Window(1, "", "3uTools").IsLikely3uToolsWindow);            // caption-less viewer builds
        Assert.False(Window(1, "Notepad", "notepad").IsLikely3uToolsWindow);
        Assert.False(new CaptureTarget(CaptureKind.Monitor, new IntPtr(1), "3uTools", 0, 0, 1, 1, true).IsLikely3uToolsWindow);
        Assert.True(Window(1, "REALTIME SCREEN").IsRealtimeScreenViewer);
        Assert.False(Window(1, "3uTools").IsRealtimeScreenViewer);
    }

    [Fact]
    public void A_reopened_viewer_replaces_the_closed_one_but_the_main_window_never_does()
    {
        var lost = Window(10, "Realtime Screen");
        var main = Window(20, "3uTools");
        var reopened = Window(30, "Realtime Screen");
        Assert.Null(CaptureTarget.Pick3uToolsReplacement(lost, new[] { main }));                 // only the main window: keep waiting
        Assert.Null(CaptureTarget.Pick3uToolsReplacement(lost, new[] { lost, main }));           // the dead handle is not a replacement
        Assert.Same(reopened, CaptureTarget.Pick3uToolsReplacement(lost, new[] { main, reopened }));

        var renamed = Window(40, "Real-time Screen - iPhone 16");                                 // title changed between versions/devices
        Assert.Same(renamed, CaptureTarget.Pick3uToolsReplacement(lost, new[] { main, renamed }));

        var lostMain = Window(50, "3uTools");                                                     // user had picked the main window by hand
        Assert.Null(CaptureTarget.Pick3uToolsReplacement(lostMain, new[] { Window(60, "3uTools") }));
        Assert.Same(reopened, CaptureTarget.Pick3uToolsReplacement(lostMain, new[] { Window(60, "3uTools"), reopened }));
    }
}

public sealed class FirewallRuleTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SmaetRec.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    [Theory]
    [InlineData("installer", "SmaetRec.iss")]
    [InlineData("src", "SmaetRec.App", "NetworkDiagnostics.cs")]
    public void Firewall_rules_cover_public_networks_and_stay_limited_to_this_program(params string[] path)
    {
        // Windows files the iPhone's USB and hotspot networks under "Public". A rule limited to private networks
        // lets the phone see nothing, which looks exactly like "USB does not work".
        var rules = RepoFile(path).Split('\n').Where(l => l.Contains("advfirewall firewall add rule")).ToList();
        Assert.Equal(2, rules.Count);
        Assert.All(rules, rule =>
        {
            Assert.Contains("profile=any", rule);
            Assert.Contains("program=", rule);
            Assert.Contains("dir=in", rule);
        });
    }
}
