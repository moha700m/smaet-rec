using System.Collections.Concurrent;
using SmaetRec.Core;
using Vortice.MediaFoundation;

namespace SmaetRec.Media;

public sealed record RecorderOptions(string Path, int Width, int Height, int Fps, int BitrateBps, bool WithAudio);

/// <summary>
/// Writes H.264 video (hardware encoder when Windows offers one) and AAC audio into an MP4 file.
/// Video frames are BGRA, top-down. Audio is 48 kHz stereo 16-bit PCM.
/// </summary>
public sealed class Recorder : IDisposable
{
    public const int AudioRate = 48000, AudioChannels = 2;
    private static readonly Guid H264ProfileKey = MediaTypeAttributeKeys.Mpeg2Profile;

    private readonly RecorderOptions _options;
    private readonly IMFSinkWriter _writer;
    private readonly int _videoStream, _audioStream = -1;
    private readonly BlockingCollection<(IMFSample sample, bool audio)> _queue = new(new ConcurrentQueue<(IMFSample, bool)>());
    private readonly Thread _thread;
    private int _pendingVideo;
    private long _videoFrames, _droppedFrames, _audioSamples;
    private volatile string? _error;
    private bool _finished;
    private readonly object _finishGate = new();

    public long VideoFrames => Interlocked.Read(ref _videoFrames);
    public long DroppedFrames => Interlocked.Read(ref _droppedFrames);
    public long AudioSamplesWritten => Interlocked.Read(ref _audioSamples);
    public bool HasAudio => _audioStream >= 0;
    public string? Error => _error;
    public RecorderOptions Options => _options;

    public Recorder(RecorderOptions options)
    {
        if (!MediaFoundationRuntime.EnsureStarted()) throw new InvalidOperationException(MediaFoundationRuntime.StartError ?? "Media Foundation is not available");
        _options = options;
        using var attributes = MediaFactory.MFCreateAttributes(2);
        attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1u);
        _writer = MediaFactory.MFCreateSinkWriterFromURL(options.Path, null!, attributes);
        try
        {
            using (var outType = MediaFactory.MFCreateMediaType())
            {
                outType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                outType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
                outType.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)options.BitrateBps);
                outType.Set(MediaTypeAttributeKeys.InterlaceMode, 2u);
                outType.Set(H264ProfileKey, 100u);
                MediaFactory.MFSetAttributeSize(outType, MediaTypeAttributeKeys.FrameSize, (uint)options.Width, (uint)options.Height);
                MediaFactory.MFSetAttributeRatio(outType, MediaTypeAttributeKeys.FrameRate, (uint)options.Fps, 1);
                MediaFactory.MFSetAttributeRatio(outType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1);
                _videoStream = _writer.AddStream(outType);
            }
            using (var inType = MediaFactory.MFCreateMediaType())
            {
                inType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                inType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
                inType.Set(MediaTypeAttributeKeys.InterlaceMode, 2u);
                inType.Set(MediaTypeAttributeKeys.DefaultStride, (uint)(options.Width * 4));
                MediaFactory.MFSetAttributeSize(inType, MediaTypeAttributeKeys.FrameSize, (uint)options.Width, (uint)options.Height);
                MediaFactory.MFSetAttributeRatio(inType, MediaTypeAttributeKeys.FrameRate, (uint)options.Fps, 1);
                MediaFactory.MFSetAttributeRatio(inType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1);
                _writer.SetInputMediaType(_videoStream, inType, null!);
            }
            if (options.WithAudio)
            {
                using (var outType = MediaFactory.MFCreateMediaType())
                {
                    outType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                    outType.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
                    outType.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
                    outType.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)AudioRate);
                    outType.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)AudioChannels);
                    outType.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 24000u);
                    _audioStream = _writer.AddStream(outType);
                }
                using (var inType = MediaFactory.MFCreateMediaType())
                {
                    inType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                    inType.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
                    inType.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
                    inType.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)AudioRate);
                    inType.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)AudioChannels);
                    inType.Set(MediaTypeAttributeKeys.AudioBlockAlignment, (uint)(AudioChannels * 2));
                    inType.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)(AudioRate * AudioChannels * 2));
                    inType.Set(MediaTypeAttributeKeys.AllSamplesIndependent, 1u);
                    _writer.SetInputMediaType(_audioStream, inType, null!);
                }
            }
            _writer.BeginWriting();
        }
        catch
        {
            _writer.Dispose();
            TryDelete(options.Path);
            throw;
        }
        _thread = new Thread(WriteLoop) { IsBackground = true, Name = "MP4 writer" };
        _thread.Start();
    }

    /// <summary>Queues one picture. <paramref name="pixels"/> points at BGRA rows with the given pitch. Never blocks; drops the frame if the encoder is far behind.</summary>
    public unsafe bool WriteVideo(IntPtr pixels, int pitch, long timeHns, long durationHns)
    {
        if (_finished || _error != null) return false;
        if (Volatile.Read(ref _pendingVideo) >= 8) { Interlocked.Increment(ref _droppedFrames); return false; }
        int rowBytes = _options.Width * 4, total = rowBytes * _options.Height;
        var buffer = MediaFactory.MFCreateMemoryBuffer(total);
        buffer.Lock(out IntPtr dst, out _, out _);
        if (pitch == rowBytes) Buffer.MemoryCopy((void*)pixels, (void*)dst, total, total);
        else
            for (int y = 0; y < _options.Height; y++)
                Buffer.MemoryCopy((byte*)pixels + (long)y * pitch, (byte*)dst + (long)y * rowBytes, rowBytes, rowBytes);
        buffer.Unlock();
        buffer.CurrentLength = total;
        var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        buffer.Dispose();
        sample.SampleTime = timeHns;
        sample.SampleDuration = durationHns;
        Interlocked.Increment(ref _pendingVideo);
        try { _queue.Add((sample, false)); }
        catch (InvalidOperationException)
        {
            // Finish() completed the queue between the check above and the add.
            Interlocked.Decrement(ref _pendingVideo);
            sample.Dispose();
            return false;
        }
        return true;
    }

    /// <summary>Queues interleaved 16-bit stereo PCM starting at the given sample position.</summary>
    public void WriteAudio(short[] pcm, int sampleFrames, long startFrame)
    {
        if (_finished || _error != null || _audioStream < 0 || sampleFrames <= 0) return;
        int bytes = sampleFrames * AudioChannels * 2;
        var buffer = MediaFactory.MFCreateMemoryBuffer(bytes);
        buffer.Lock(out IntPtr dst, out _, out _);
        System.Runtime.InteropServices.Marshal.Copy(pcm, 0, dst, sampleFrames * AudioChannels);
        buffer.Unlock();
        buffer.CurrentLength = bytes;
        var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        buffer.Dispose();
        sample.SampleTime = startFrame * 10_000_000L / AudioRate;
        sample.SampleDuration = sampleFrames * 10_000_000L / AudioRate;
        try { _queue.Add((sample, true)); }
        catch (InvalidOperationException) { sample.Dispose(); }
    }

    private void WriteLoop()
    {
        foreach (var (sample, audio) in _queue.GetConsumingEnumerable())
        {
            try
            {
                if (_error == null)
                {
                    _writer.WriteSample(audio ? _audioStream : _videoStream, sample);
                    if (audio) Interlocked.Add(ref _audioSamples, 1); else Interlocked.Increment(ref _videoFrames);
                }
            }
            catch (SharpGen.Runtime.SharpGenException e)
            {
                _error = e.Message;
                Log.Error("MP4 write", e);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                _error = e.Message;
                Log.Error("MP4 write", e);
            }
            finally
            {
                if (!audio) Interlocked.Decrement(ref _pendingVideo);
                sample.Dispose();
            }
        }
    }

    /// <summary>Finishes the file. Returns null on success, or what went wrong.</summary>
    public string? Finish()
    {
        lock (_finishGate)
        {
            if (_finished) return _error;
            _finished = true;
            try { _queue.CompleteAdding(); } catch (ObjectDisposedException) { }
            _thread.Join();
            try
            {
                if (VideoFrames > 0) _writer.Finalize();
                else _error ??= "No picture was recorded";
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                _error ??= e.Message;
                Log.Error("MP4 finish", e);
            }
            _writer.Dispose();
            if (_error != null && VideoFrames == 0) TryDelete(_options.Path);
            return _error;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose() => Finish();
}
