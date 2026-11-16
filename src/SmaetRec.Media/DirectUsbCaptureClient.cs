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
    private KillOnCloseJob? _job;
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
            // If this app dies for any reason, Windows ends the host with it: no orphaned capture process.
            _job = KillOnCloseJob.TryAttach(_host);

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

    /// <summary>Supplies the buffer a picture is read into (so frames are not copied twice). Defaults to a new array.</summary>
    public Func<int, byte[]>? RentBuffer { get; set; }

    /// <summary>Decoded pictures received from the host in this session.</summary>
    public long FramesReceived => Interlocked.Read(ref _frames);
    /// <summary>Milliseconds since the last picture arrived, or -1 before the first one.</summary>
    public long MillisecondsSinceLastFrame
    {
        get { long t = Interlocked.Read(ref _lastFrameTick); return t == 0 ? -1 : Environment.TickCount64 - t; }
    }
    private long _frames, _lastFrameTick;

    private async Task ReadLoopAsync(Stream stream, CancellationToken token)
    {
        try
        {
            await ReadRecordsAsync(stream, RentBuffer, message =>
            {
                _lastStatus = message;
                Status?.Invoke(message);
            }, (pixels, width, height, timestamp) =>
            {
                Interlocked.Increment(ref _frames);
                Interlocked.Exchange(ref _lastFrameTick, Environment.TickCount64);
                Volatile.Write(ref _streaming, 1);
                _firstFrame.TrySetResult(true);
                VideoFrame?.Invoke(pixels, width, height, timestamp);
            }, token).ConfigureAwait(false);
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

    /// <summary>
    /// Reads host records until a "stopped" record. Pictures are read straight into a buffer from
    /// <paramref name="rent"/> (at least width*height*4 bytes) and handed to <paramref name="onVideo"/>, which owns it afterwards.
    /// Throws <see cref="IOException"/> for a host-reported error and <see cref="InvalidDataException"/> for a malformed stream.
    /// </summary>
    public static async Task ReadRecordsAsync(Stream stream, Func<int, byte[]>? rent, Action<string> onStatus,
        Action<byte[], int, int, long> onVideo, CancellationToken token)
    {
        byte[] prefix = new byte[8];
        byte[] header = new byte[1024];
        while (!token.IsCancellationRequested)
        {
            await ReadExactlyAsync(stream, prefix, token).ConfigureAwait(false);
            int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
            int headerLength = BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(4));
            if (length < 6 || length > MaxRecordBytes) throw new InvalidDataException($"Invalid USB IPC record length: {length}.");
            if (headerLength <= 1 || headerLength > length - 4 || headerLength > 64 * 1024) throw new InvalidDataException("Invalid USB IPC header length.");
            if (header.Length < headerLength) header = new byte[headerLength];
            await ReadExactlyAsync(stream, header.AsMemory(0, headerLength), token).ConfigureAwait(false);
            int payloadLength = length - 4 - headerLength;

            string type; string? message = null;
            int width = 0, height = 0; long timestamp = 0;
            using (var doc = JsonDocument.Parse(header.AsMemory(0, headerLength)))
            {
                var root = doc.RootElement;
                type = root.GetProperty("type").GetString() ?? "";
                if (type is "status" or "error") message = root.GetProperty("message").GetString();
                else if (type == "video")
                {
                    int payloadBytes = root.GetProperty("payloadBytes").GetInt32();
                    width = root.GetProperty("width").GetInt32();
                    height = root.GetProperty("height").GetInt32();
                    int stride = root.GetProperty("stride").GetInt32();
                    string format = root.GetProperty("pixelFormat").GetString() ?? "";
                    if (format != "bgra8" || width is < 1 or > 16384 || height is < 1 or > 16384 ||
                        stride != checked(width * 4) || payloadBytes != checked(stride * height) || payloadBytes != payloadLength)
                        throw new InvalidDataException("Invalid BGRA video IPC frame.");
                    timestamp = root.GetProperty("timestamp100ns").GetInt64();
                }
            }
            switch (type)
            {
                case "video":
                {
                    byte[] pixels = rent?.Invoke(payloadLength) ?? GC.AllocateUninitializedArray<byte>(payloadLength);
                    if (pixels.Length < payloadLength) throw new InvalidOperationException("The frame buffer is too small.");
                    await ReadExactlyAsync(stream, pixels.AsMemory(0, payloadLength), token).ConfigureAwait(false);
                    onVideo(pixels, width, height, timestamp);
                    break;
                }
                case "status":
                    if (payloadLength != 0) throw new InvalidDataException("Unexpected payload on a status record.");
                    onStatus(message ?? "Native USB host is working.");
                    break;
                case "error": throw new IOException(message ?? "USB host error.");
                case "stopped": return;
                default: throw new InvalidDataException("Unknown USB IPC record type.");
            }
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
        // Close our end first: the host's next write then fails and it shuts its capture session down
        // cleanly (restoring the iPhone's USB configuration). Only if it does not, end it.
        try { _pipe?.Dispose(); } catch (IOException) { }
        _pipe = null;
        if (_reader != null) { try { await _reader.ConfigureAwait(false); } catch (OperationCanceledException) { } catch (Exception e) when (e is IOException or InvalidDataException or JsonException or ObjectDisposedException) { } }
        _reader = null;
        var host = _host; _host = null;
        if (host != null)
        {
            try
            {
                if (!host.HasExited)
                {
                    using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                    try { await host.WaitForExitAsync(grace.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { host.Kill(entireProcessTree: true); }
                }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            host.Dispose();
        }
        _job?.Dispose(); _job = null;
        _stop?.Dispose(); _stop = null;
        Volatile.Write(ref _streaming, 0);
        Volatile.Write(ref _pipeConnected, 0);
        Interlocked.Exchange(ref _started, 0);
    }
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}

/// <summary>A Windows job object that ends the processes in it when this app exits, however it exits.</summary>
internal sealed class KillOnCloseJob : IDisposable
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private IntPtr _handle;
    private KillOnCloseJob(IntPtr handle) => _handle = handle;

    public static KillOnCloseJob? TryAttach(Process process)
    {
        try
        {
            IntPtr job = CreateJobObjectW(IntPtr.Zero, null);
            if (job == IntPtr.Zero) return null;
            // JOBOBJECT_EXTENDED_LIMIT_INFORMATION (x64: 144 bytes); LimitFlags at offset 16; KILL_ON_JOB_CLOSE = 0x2000.
            const int size = 144;
            IntPtr info = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
            try
            {
                for (int i = 0; i < size; i++) System.Runtime.InteropServices.Marshal.WriteByte(info, i, 0);
                System.Runtime.InteropServices.Marshal.WriteInt32(info, 16, 0x2000);
                if (!SetInformationJobObject(job, 9, info, size) || !AssignProcessToJobObject(job, process.Handle))
                {
                    CloseHandle(job);
                    return null;
                }
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(info); }
            return new KillOnCloseJob(job);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        // Closing the handle would kill the host; by the time this runs the host has already exited or been ended.
        if (_handle != IntPtr.Zero) { CloseHandle(_handle); _handle = IntPtr.Zero; }
    }
}
