using System.Windows.Media;
using System.Windows.Media.Imaging;
using SmaetRec.Core;
using SmaetRec.Media;
using Vortice.MediaFoundation;

namespace SmaetRec.WinTests;

internal static class TestSupport
{
    /// <summary>Folder that CI uploads, so pictures and videos made by tests can be looked at afterwards.</summary>
    public static string Artifacts
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("SMAET_ARTIFACTS");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Path.GetTempPath(), "smaet-artifacts");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static void SavePng(string name, byte[] bgra, int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, bgra, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(Artifacts, name));
        encoder.Save(file);
    }

    public static (byte b, byte g, byte r) Pixel(byte[] bgra, int width, int x, int y)
    {
        int i = (y * width + x) * 4;
        return (bgra[i], bgra[i + 1], bgra[i + 2]);
    }

    public static bool WaitFor(Func<bool> condition, int milliseconds = 8000)
    {
        var until = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < until) { if (condition()) return true; Thread.Sleep(30); }
        return condition();
    }

    public static AppSettings Settings(string folder, VideoQuality quality = VideoQuality.Hd720, int fps = 30) =>
        new() { OutputFolder = folder, Quality = quality, Fps = fps };
}

/// <summary>What a finished MP4 really contains, read back with Windows' own reader.</summary>
internal sealed class Mp4Info
{
    public int Width, Height, VideoFrames, AudioSamples;
    public double Fps, DurationSeconds, AudioSeconds;
    public bool HasVideo, HasAudio;
    public Guid VideoCodec;
    public byte[]? FirstFrameBgra;

    public static Mp4Info Read(string path)
    {
        Assert.True(MediaFoundationRuntime.EnsureStarted(), MediaFoundationRuntime.StartError);
        var info = new Mp4Info();
        using (var native = MediaFactory.MFCreateSourceReaderFromURL(path, null!))
        {
            try
            {
                using var type = native.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, 0);
                info.HasVideo = true;
                info.VideoCodec = type.GetGUID(MediaTypeAttributeKeys.Subtype);
                MediaFactory.MFGetAttributeSize(type, MediaTypeAttributeKeys.FrameSize, out uint w, out uint h);
                MediaFactory.MFGetAttributeRatio(type, MediaTypeAttributeKeys.FrameRate, out uint n, out uint d);
                info.Width = (int)w; info.Height = (int)h; info.Fps = d == 0 ? 0 : (double)n / d;
            }
            catch (SharpGen.Runtime.SharpGenException) { }
            try { using var audio = native.GetNativeMediaType(SourceReaderIndex.FirstAudioStream, 0); info.HasAudio = true; }
            catch (SharpGen.Runtime.SharpGenException) { }
        }
        if (!info.HasVideo) return info;

        using var attributes = MediaFactory.MFCreateAttributes(1);
        attributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing, 1u);
        using var reader = MediaFactory.MFCreateSourceReaderFromURL(path, attributes);
        using (var rgb = MediaFactory.MFCreateMediaType())
        {
            rgb.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            rgb.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
            reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, rgb);
        }
        int stride = info.Width * 4;
        using (var current = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream))
            stride = (int)MediaFactory.MFGetAttributeUInt32(current, MediaTypeAttributeKeys.DefaultStride, (uint)stride);
        long lastEnd = 0;
        while (true)
        {
            using var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out long time);
            if (flags.HasFlag(SourceReaderFlag.EndOfStream) || flags.HasFlag(SourceReaderFlag.Error)) break;
            if (sample == null) continue;
            info.VideoFrames++;
            lastEnd = Math.Max(lastEnd, time + sample.SampleDuration);
            if (info.FirstFrameBgra == null)
            {
                using var buffer = sample.ConvertToContiguousBuffer();
                buffer.Lock(out IntPtr p, out _, out int length);
                var frame = new byte[info.Width * info.Height * 4];
                int rowBytes = info.Width * 4, absStride = Math.Abs(stride);
                for (int y = 0; y < info.Height && (y + 1) * absStride <= length; y++)
                {
                    int srcRow = stride < 0 ? info.Height - 1 - y : y; // a negative stride means the picture is stored bottom-up
                    System.Runtime.InteropServices.Marshal.Copy(p + srcRow * absStride, frame, y * rowBytes, rowBytes);
                }
                buffer.Unlock();
                info.FirstFrameBgra = frame;
            }
        }
        info.DurationSeconds = lastEnd / 10_000_000.0;
        if (info.HasAudio)
        {
            long audioEnd = 0;
            while (true)
            {
                using var sample = reader.ReadSample(SourceReaderIndex.FirstAudioStream, SourceReaderControlFlag.None, out _, out var flags, out long time);
                if (flags.HasFlag(SourceReaderFlag.EndOfStream) || flags.HasFlag(SourceReaderFlag.Error)) break;
                if (sample == null) continue;
                info.AudioSamples++;
                audioEnd = Math.Max(audioEnd, time + sample.SampleDuration);
            }
            info.AudioSeconds = audioEnd / 10_000_000.0;
        }
        return info;
    }
}
