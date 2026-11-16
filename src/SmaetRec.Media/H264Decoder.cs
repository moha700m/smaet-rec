using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SmaetRec.Core;
using Vortice.MediaFoundation;

namespace SmaetRec.Media;

/// <summary>
/// Decodes an H.264 Annex-B stream with the decoder built into Windows and delivers BGRA pictures.
/// Runs on its own thread; feed it with <see cref="Push"/>.
/// </summary>
public sealed class H264Decoder : IDisposable
{
    private static readonly Guid DecoderClsid = new("62CE7E72-4C71-4d20-B15D-452831A87D9D");
    private static readonly Guid TransformIid = new("bf94c121-5b05-4e6f-8000-ba598961414d");
    private static readonly Guid LowLatency = new("9c27891a-ed7a-40e1-88e8-b22727a024ee");
    private static readonly Guid MinimumDisplayAperture = new("d7388766-18fe-48c6-a177-ee894867c8c4");

    private readonly BlockingCollection<(byte[] data, long timestamp, bool key)> _queue = new(900);
    private readonly Thread _thread;
    private readonly MemoryFrameSource _output;
    private volatile bool _disposed, _waitForKey = true;
    private IMFTransform? _transform;
    private int _codedWidth, _codedHeight, _width, _height, _stride;
    private long _decoded;
    private long _inputs, _outputs, _notShown, _dropped, _formatChanges, _badLayout;
    private double _decodeMs, _convertMs;
    private int _lastBufferLength;
    private long _lastShownTick;

    public int Width => _width;
    public int Height => _height;
    /// <summary>Pictures turned into pixels and handed to the screen.</summary>
    public long FramesDecoded => Interlocked.Read(ref _decoded);
    /// <summary>Pictures the Windows decoder produced, shown or not. It always holds the newest one back until the next packet arrives.</summary>
    public long PicturesDecoded => Interlocked.Read(ref _outputs);
    public string? Error { get; private set; }
    /// <summary>Packets thrown away because the computer could not keep up. Any non-zero value means the picture may be damaged until the phone sends a fresh key frame.</summary>
    public long PacketsDropped => Interlocked.Read(ref _dropped);

    /// <summary>One line with everything needed to judge the decoder from a log: counts, speed and the picture layout Windows reported.</summary>
    public string Describe()
    {
        long outputs = Math.Max(1, Interlocked.Read(ref _outputs)), shown = Math.Max(1, FramesDecoded);
        return $"in={Interlocked.Read(ref _inputs)} out={Interlocked.Read(ref _outputs)} shown={FramesDecoded} notShown={Interlocked.Read(ref _notShown)} " +
               $"dropped={PacketsDropped} badLayout={Interlocked.Read(ref _badLayout)} formatChanges={Interlocked.Read(ref _formatChanges)} queue={_queue.Count} " +
               $"decode={_decodeMs / outputs:F1}ms convert={_convertMs / shown:F1}ms " +
               $"coded={_codedWidth}x{_codedHeight} visible={_width}x{_height} stride={_stride} buffer={_lastBufferLength}";
    }

    /// <summary>Raised (on the decoder thread) when the picture size becomes known or changes.</summary>
    public event Action<int, int>? SizeChanged;

    public H264Decoder(MemoryFrameSource output)
    {
        _output = output;
        _thread = new Thread(Run) { IsBackground = true, Name = "H264 decoder" };
        _thread.Start();
    }

    public void Push(byte[] annexB, long timestampMicros, bool isKeyFrame)
    {
        if (_disposed) return;
        if (_queue.TryAdd((annexB, timestampMicros, isKeyFrame))) return;
        // Hopelessly behind (about 15 seconds of video waiting). Every later picture builds on the
        // ones before it, so a dropped packet damages the picture until the next key frame.
        if (Interlocked.Increment(ref _dropped) == 1) Log.Write("H264 decoder cannot keep up; packets are being dropped. " + Describe());
        if (isKeyFrame)
        {
            while (_queue.TryTake(out _)) { }
            _queue.TryAdd((annexB, timestampMicros, isKeyFrame));
        }
    }

    private void Run()
    {
        try
        {
            if (!MediaFoundationRuntime.EnsureStarted()) { Error = MediaFoundationRuntime.StartError; return; }
            CreateTransform();
            foreach (var (data, timestamp, key) in _queue.GetConsumingEnumerable())
            {
                if (_waitForKey) { if (!key) continue; _waitForKey = false; }
                Decode(data, timestamp);
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Error = e.Message;
            Log.Error("H264 decoder", e);
        }
        finally
        {
            _transform?.Dispose();
            _transform = null;
        }
    }

    private void CreateTransform()
    {
        Marshal.ThrowExceptionForHR(Native.CoCreateInstance(DecoderClsid, IntPtr.Zero, 1, TransformIid, out IntPtr ptr));
        _transform = new IMFTransform(ptr);
        try { _transform.Attributes.Set(LowLatency, 1u); } catch (SharpGen.Runtime.SharpGenException) { }

        using (var input = MediaFactory.MFCreateMediaType())
        {
            input.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            input.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
            _transform.SetInputType(0, input, 0);
        }
        SelectOutputType();
        _transform.ProcessMessage(TMessageType.MessageNotifyBeginStreaming, UIntPtr.Zero);
        _transform.ProcessMessage(TMessageType.MessageNotifyStartOfStream, UIntPtr.Zero);
    }

    private void SelectOutputType()
    {
        for (int i = 0; ; i++)
        {
            using var type = _transform!.GetOutputAvailableType(0, i);
            if (type.GetGUID(MediaTypeAttributeKeys.Subtype) != VideoFormatGuids.NV12) continue;
            _transform.SetOutputType(0, type, 0);
            MediaFactory.MFGetAttributeSize(type, MediaTypeAttributeKeys.FrameSize, out uint w, out uint h);
            _codedWidth = (int)w; _codedHeight = (int)h;
            int width = _codedWidth, height = _codedHeight;
            try
            {
                var area = type.GetBlob(MinimumDisplayAperture);
                if (area.Length >= 16)
                {
                    int aw = BitConverter.ToInt32(area, 8), ah = BitConverter.ToInt32(area, 12);
                    if (aw > 0 && ah > 0 && aw <= _codedWidth && ah <= _codedHeight) { width = aw; height = ah; }
                }
            }
            catch (SharpGen.Runtime.SharpGenException) { }
            _stride = (int)MediaFactory.MFGetAttributeUInt32(type, MediaTypeAttributeKeys.DefaultStride, (uint)_codedWidth);
            if (_stride <= 0) _stride = _codedWidth;
            Interlocked.Increment(ref _formatChanges);
            Log.Write($"H264 output format: coded {_codedWidth}x{_codedHeight}, visible {width}x{height}, stride {_stride}");
            if (width != _width || height != _height)
            {
                _width = width; _height = height;
                Log.Write($"iPhone picture size {width}x{height}");
                SizeChanged?.Invoke(width, height);
            }
            return;
        }
    }

    private unsafe void Decode(byte[] data, long timestampMicros)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _inputs);
        using (var buffer = MediaFactory.MFCreateMemoryBuffer(data.Length))
        using (var sample = MediaFactory.MFCreateSample())
        {
            buffer.Lock(out IntPtr p, out _, out _);
            Marshal.Copy(data, 0, p, data.Length);
            buffer.Unlock();
            buffer.CurrentLength = data.Length;
            sample.AddBuffer(buffer);
            sample.SampleTime = timestampMicros * 10;
            _transform!.ProcessInput(0, sample, 0);
        }

        while (!_disposed)
        {
            var info = _transform.GetOutputStreamInfo(0);
            bool transformAllocates = (info.Flags & 0x100) != 0 || (info.Flags & 0x200) != 0;
            var output = new OutputDataBuffer();
            IMFSample? ours = null;
            if (!transformAllocates)
            {
                ours = MediaFactory.MFCreateSample();
                using var outBuffer = MediaFactory.MFCreateMemoryBuffer(Math.Max(info.Size, 1));
                ours.AddBuffer(outBuffer);
                output.Sample = ours;
            }
            try
            {
                var hr = _transform.ProcessOutput(ProcessOutputFlags.None, 1, ref output, out _);
                if (hr.Code == MediaFoundationRuntime.NeedMoreInput) return;
                if (hr.Code == MediaFoundationRuntime.StreamChange) { SelectOutputType(); continue; }
                hr.CheckError();
                var result = output.Sample ?? ours;
                if (result == null) return;
                Interlocked.Increment(ref _outputs);
                _decodeMs += System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                started = System.Diagnostics.Stopwatch.GetTimestamp();
                // Every packet must be decoded (later pictures build on it), but turning a picture into
                // pixels is only worth it for the one that will be seen. When packets are waiting, skip
                // that step so the decoder catches up instead of falling behind and dropping packets.
                bool show = _queue.Count == 0 || Environment.TickCount64 - _lastShownTick >= 50;
                if (!show) { Interlocked.Increment(ref _notShown); continue; }
                using var contiguous = result.ConvertToContiguousBuffer();
                contiguous.Lock(out IntPtr src, out _, out int length);
                try
                {
                    _lastBufferLength = length;
                    if (_width > 0 && _height > 0 && length >= _stride * _codedHeight * 3 / 2)
                    {
                        long convertStart = System.Diagnostics.Stopwatch.GetTimestamp();
                        var frame = _output.Rent(_width * _height * 4);
                        fixed (byte* dst = frame)
                            PixelConvert.Nv12ToBgra((byte*)src, _stride, _width, _height, _codedHeight, dst, _width * 4);
                        _output.Submit(frame, _width, _height);
                        _convertMs += System.Diagnostics.Stopwatch.GetElapsedTime(convertStart).TotalMilliseconds;
                        _lastShownTick = Environment.TickCount64;
                        Interlocked.Increment(ref _decoded);
                    }
                    else if (Interlocked.Increment(ref _badLayout) == 1)
                    {
                        Log.Write("H264 picture skipped: buffer smaller than the reported layout. " + Describe());
                    }
                }
                finally { contiguous.Unlock(); }
            }
            finally
            {
                if (output.Sample != null && !ReferenceEquals(output.Sample, ours)) output.Sample.Dispose();
                output.Events?.Dispose();
                ours?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.CompleteAdding();
        if (!_thread.Join(2000)) Log.Write("H264 decoder thread did not stop in time");
    }
}
