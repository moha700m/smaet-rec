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
    private int _pipeConnected;
    private int _streaming;
    private readonly TaskCompletionSource<bool> _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _lastStatus = "Starting the USB capture host.";

    /// <summary>True only after the native host has delivered a real decoded frame.</summary>
    public bool IsActive => Volatile.Read(ref _started) != 0 &&
        Volatile.Read(ref _pipeConnected) != 0 && Volatile.Read(ref _streaming) != 0;

    public event Action<byte[], int, int, long>? VideoFrame;
    public event Action<string>? Status;
    public event Action<string>? Failed;
    public event Action? Disconnected;

    public DirectUsbCaptureClient(string resourceDirectory) => _resourceDirectory = Path.GetFullPath(resourceDirectory);

    public async Task StartAsync(string usbSerial, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Direct USB capture is Windows-only.");
        if (string.IsNullOrWhiteSpace(usbSerial) || usbSerial.Length > 128 || usbSerial.Any(char.IsControl)) throw new ArgumentException("A valid Apple USB serial is required.", nameof(usbSerial));
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("USB capture is already started.");
        try
        {
            string exe = Path.Combine(_resourceDirectory, "usb", "iphone-usb-host.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("The separately licensed USB host is not installed. See docs/direct-usb-host.md.", exe);
            _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _pipe = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 1024 * 1024, 1024 * 1024);
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)! };
            foreach (var arg in new[] { "--smaet-ipc", "--pipe", _pipeName, "--device-serial", usbSerial, "--video", "bgra8" }) psi.ArgumentList.Add(arg);
            _host = Process.Start(psi) ?? throw new InvalidOperationException("Could not start USB host.");

            var connection = _pipe.WaitForConnectionAsync(_stop.Token);
            var exited = _host.WaitForExitAsync();
            var timeout = Task.Delay(TimeSpan.FromSeconds(25));
            var completed = await Task.WhenAny(connection, exited, timeout).ConfigureAwait(false);
            if (completed == exited)
            {
                await exited.ConfigureAwait(false);
                throw new IOException($"USB host exited before opening IPC (exit code {_host.ExitCode}).");
            }
            if (completed == timeout) throw new TimeoutException("USB host did not connect to the IPC pipe within 25 seconds.");
            await connection.ConfigureAwait(false);
            Volatile.Write(ref _pipeConnected, 1);
            _lastStatus = "USB host IPC connected; waiting for the iPhone capture handshake.";
            Status?.Invoke(_lastStatus);
            _reader = ReadLoopAsync(_pipe, _stop.Token);

            // A named-pipe connection only proves that the helper process started.
            // It does not prove that the iPhone accepted QuickTime or sent a picture.
            // Do not report a successful USB connection until the first decoded frame.
            var firstFrame = _firstFrame.Task;
            var firstFrameTimeout = Task.Delay(TimeSpan.FromSeconds(55), _stop.Token);
            var firstResult = await Task.WhenAny(firstFrame, exited, firstFrameTimeout).ConfigureAwait(false);
            if (firstResult == exited)
            {
                await exited.ConfigureAwait(false);
                throw new IOException($"USB host exited before the first iPhone video frame (exit code {_host.ExitCode}). Last stage: {_lastStatus}");
            }
            if (firstResult == firstFrameTimeout)
            {
                _stop.Token.ThrowIfCancellationRequested();
                throw new TimeoutException($"iPhone USB capture did not produce a video frame within 55 seconds. Last stage: {_lastStatus}. Keep the iPhone unlocked and trusted, then unplug and reconnect the cable before retrying.");
            }
            await firstFrame.ConfigureAwait(false);
        }
        catch
        {
            await StopAsync().ConfigureAwait(false);
            throw;
        }
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
                if (record.Length < 4) throw new InvalidDataException("Truncated USB IPC record.");
                int headerLength = BinaryPrimitives.ReadInt32LittleEndian(record);
                if (headerLength <= 1 || headerLength > record.Length - 4) throw new InvalidDataException("Invalid USB IPC header length.");
                using var doc = JsonDocument.Parse(record.AsMemory(4, headerLength));
                var root = doc.RootElement;
                switch (root.GetProperty("type").GetString())
                {
                    case "status":
                    {
                        _lastStatus = root.GetProperty("message").GetString() ?? "Native USB host is working.";
                        Status?.Invoke(_lastStatus);
                        break;
                    }
                    case "video":
                    {
                        int payloadBytes = root.GetProperty("payloadBytes").GetInt32();
                        int width = root.GetProperty("width").GetInt32();
                        int height = root.GetProperty("height").GetInt32();
                        int stride = root.GetProperty("stride").GetInt32();
                        string format = root.GetProperty("pixelFormat").GetString() ?? "";
                        int payloadOffset = checked(4 + headerLength);
                        if (format != "bgra8" || width is < 1 or > 16384 || height is < 1 or > 16384 ||
                            stride != checked(width * 4) || payloadBytes != checked(stride * height) ||
                            payloadOffset + payloadBytes != record.Length)
                            throw new InvalidDataException("Invalid BGRA video IPC frame.");
                        Volatile.Write(ref _streaming, 1);
                        _firstFrame.TrySetResult(true);
                        VideoFrame?.Invoke(record.AsSpan(payloadOffset, payloadBytes).ToArray(), width, height,
                            root.GetProperty("timestamp100ns").GetInt64());
                        break;
                    }
                    case "error": throw new IOException(root.GetProperty("message").GetString() ?? "USB host error.");
                    case "stopped": return;
                    default: throw new InvalidDataException("Unknown USB IPC record type.");
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (EndOfStreamException e) when (!token.IsCancellationRequested) { Failed?.Invoke(e.Message); }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or KeyNotFoundException or OverflowException or InvalidOperationException)
        {
            _firstFrame.TrySetException(e);
            Failed?.Invoke(e.Message);
        }
        finally
        {
            Volatile.Write(ref _streaming, 0);
            Volatile.Write(ref _pipeConnected, 0);
            _firstFrame.TrySetException(new IOException($"USB host closed before a decoded frame arrived. Last stage: {_lastStatus}"));
            Disconnected?.Invoke();
        }
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken token)
    {
        int offset = 0;
        while (offset < buffer.Length) { int n = await stream.ReadAsync(buffer[offset..], token).ConfigureAwait(false); if (n == 0) throw new EndOfStreamException("USB host closed IPC."); offset += n; }
    }

    public async Task StopAsync()
    {
        _stop?.Cancel();
        if (_reader != null) { try { await _reader.ConfigureAwait(false); } catch (OperationCanceledException) { } catch (Exception e) when (e is IOException or InvalidDataException or JsonException) { } }
        if (_host != null)
        {
            try { if (!_host.HasExited && !_host.WaitForExit(3000)) _host.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        _host?.Dispose(); _host = null; _pipe?.Dispose(); _pipe = null; _stop?.Dispose(); _stop = null;
        Volatile.Write(ref _streaming, 0);
        Volatile.Write(ref _pipeConnected, 0);
        Interlocked.Exchange(ref _started, 0);
    }
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}