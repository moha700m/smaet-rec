using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace SmaetRec.Media;

/// <summary>IPC client for the separately licensed GPL QuickTime USB host.</summary>
public sealed class DirectUsbCaptureClient : IAsyncDisposable
{
    private const int MaxRecordBytes = 64 * 1024 * 1024;
    private readonly string _resourceDirectory;
    private readonly string _pipeName = "smaet-usb-" + Guid.NewGuid().ToString("N");
    private Process? _host;
    private NamedPipeServerStream? _pipe;
    private CancellationTokenSource? _stop;
    private Task? _reader;
    private int _started;

    public event Action<byte[], long, bool>? VideoAccessUnit;
    public event Action<string>? Status;
    public event Action<string>? Failed;
    public event Action? Disconnected;

    public DirectUsbCaptureClient(string resourceDirectory) => _resourceDirectory = Path.GetFullPath(resourceDirectory);

    public async Task StartAsync(string udid, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("USB capture is already started.");
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Direct USB capture is Windows-only.");
        if (string.IsNullOrWhiteSpace(udid) || udid.Length > 128 || udid.Any(char.IsControl)) throw new ArgumentException("A valid Apple UDID is required.", nameof(udid));
        string exe = Path.Combine(_resourceDirectory, "usb", "iphone-usb-host.exe");
        if (!File.Exists(exe)) { Interlocked.Exchange(ref _started, 0); throw new FileNotFoundException("The separately licensed USB host is not installed. See docs/direct-usb-host.md.", exe); }
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pipe = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 1024 * 1024, 1024 * 1024);
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)! };
        foreach (var arg in new[] { "--smaet-ipc", "--pipe", _pipeName, "--device", udid, "--video", "h264-annexb" }) psi.ArgumentList.Add(arg);
        _host = Process.Start(psi) ?? throw new InvalidOperationException("Could not start USB host.");
        try { await _pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false); Status?.Invoke("USB host IPC connected; waiting for QuickTime video."); _reader = ReadLoopAsync(_pipe, _stop.Token); }
        catch { await StopAsync().ConfigureAwait(false); throw; }
    }

    private async Task ReadLoopAsync(Stream stream, CancellationToken token)
    {
        byte[] prefix = new byte[4];
        try
        {
            while (!token.IsCancellationRequested)
            {
                await ReadExactlyAsync(stream, prefix, token).ConfigureAwait(false);
                int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
                if (length < 2 || length > MaxRecordBytes) throw new InvalidDataException($"Invalid USB IPC record length: {length}.");
                byte[] record = GC.AllocateUninitializedArray<byte>(length);
                await ReadExactlyAsync(stream, record, token).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(record);
                var root = doc.RootElement;
                switch (root.GetProperty("type").GetString())
                {
                    case "status": Status?.Invoke(root.GetProperty("message").GetString() ?? ""); break;
                    case "video":
                    {
                        int headerBytes = root.GetProperty("headerBytes").GetInt32();
                        int payloadBytes = root.GetProperty("payloadBytes").GetInt32();
                        if (headerBytes <= 0 || payloadBytes <= 0 || headerBytes + payloadBytes != record.Length) throw new InvalidDataException("Inconsistent video IPC lengths.");
                        VideoAccessUnit?.Invoke(record.AsSpan(headerBytes, payloadBytes).ToArray(), root.GetProperty("timestampMicros").GetInt64(), root.GetProperty("keyFrame").GetBoolean());
                        break;
                    }
                    case "error": throw new IOException(root.GetProperty("message").GetString() ?? "USB host error.");
                    case "stopped": return;
                    default: throw new InvalidDataException("Unknown USB IPC record type.");
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (EndOfStreamException) { }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or KeyNotFoundException) { Failed?.Invoke(e.Message); }
        finally { Disconnected?.Invoke(); }
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken token)
    {
        int offset = 0;
        while (offset < buffer.Length) { int n = await stream.ReadAsync(buffer[offset..], token).ConfigureAwait(false); if (n == 0) throw new EndOfStreamException("USB host closed IPC."); offset += n; }
    }

    public async Task StopAsync()
    {
        _stop?.Cancel();
        if (_reader != null) { try { await _reader.ConfigureAwait(false); } catch (OperationCanceledException) { } }
        if (_host is { HasExited: false }) { try { if (!_host.WaitForExit(3000)) _host.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
        _host?.Dispose(); _host = null; _pipe?.Dispose(); _pipe = null; _stop?.Dispose(); _stop = null;
        Interlocked.Exchange(ref _started, 0);
    }
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}