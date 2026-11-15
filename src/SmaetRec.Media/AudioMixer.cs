using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SmaetRec.Core;

namespace SmaetRec.Media;

/// <summary>Captures the microphone and/or the computer's sound and feeds a mixed track to the recorder, in step with the video clock.</summary>
public sealed class AudioMixer : IDisposable
{
    private const int DelayFrames = Recorder.AudioRate * 15 / 100; // write 150 ms behind real time so late packets still fit
    private readonly Stopwatch _clock;
    private readonly Recorder _recorder;
    private WasapiCapture? _mic;
    private WasapiLoopbackCapture? _loopback;
    private readonly AudioTimeline _micLine = new(), _loopLine = new();
    private Thread? _pump;
    private volatile bool _stop;
    private long _written;

    public bool MicrophoneActive { get; private set; }
    public bool SystemAudioActive { get; private set; }
    public string? MicrophoneError { get; private set; }
    public string? SystemAudioError { get; private set; }

    /// <summary>True when at least one sound device can be opened (so an audio track is worth adding).</summary>
    public static (bool mic, bool system) Probe()
    {
        try
        {
            using var e = new MMDeviceEnumerator();
            return (e.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Console), e.HasDefaultAudioEndpoint(DataFlow.Render, Role.Console));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException or NotSupportedException)
        {
            Log.Error("Audio probe", ex);
            return (false, false);
        }
    }

    public AudioMixer(Recorder recorder, Stopwatch clock, bool microphone, bool systemAudio)
    {
        _recorder = recorder; _clock = clock;
        if (microphone)
        {
            try
            {
                _mic = new WasapiCapture();
                var format = _mic.WaveFormat;
                _mic.DataAvailable += (_, a) => OnData(a, format, _micLine);
                _mic.StartRecording();
                MicrophoneActive = true;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                MicrophoneError = e.Message; Log.Error("Microphone", e);
                _mic?.Dispose(); _mic = null;
            }
        }
        if (systemAudio)
        {
            try
            {
                _loopback = new WasapiLoopbackCapture();
                var format = _loopback.WaveFormat;
                _loopback.DataAvailable += (_, a) => OnData(a, format, _loopLine);
                _loopback.StartRecording();
                SystemAudioActive = true;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                SystemAudioError = e.Message; Log.Error("System audio", e);
                _loopback?.Dispose(); _loopback = null;
            }
        }
        if (recorder.HasAudio)
        {
            _pump = new Thread(Pump) { IsBackground = true, Name = "Audio mixer" };
            _pump.Start();
        }
    }

    private long NowFrame => _clock.ElapsedTicks * Recorder.AudioRate / Stopwatch.Frequency;

    private void OnData(WaveInEventArgs args, WaveFormat format, AudioTimeline line)
    {
        if (_stop || args.BytesRecorded <= 0) return;
        int channels = format.Channels, bytesPerSample = format.BitsPerSample / 8;
        if (channels <= 0 || bytesPerSample <= 0) return;
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                       (format is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71"));
        int frames = args.BytesRecorded / (channels * bytesPerSample);
        var stereo = ConvertToStereo(args.Buffer, frames, channels, bytesPerSample, isFloat, new float[frames * 2]);
        line.Append(stereo, format.SampleRate, NowFrame);
    }

    public static float[] ConvertToStereo(byte[] buffer, int frames, int channels, int bytesPerSample, bool isFloat, float[] output)
    {
        for (int f = 0; f < frames; f++)
        {
            int offset = f * channels * bytesPerSample;
            float l = ReadSample(buffer, offset, bytesPerSample, isFloat);
            float r = channels > 1 ? ReadSample(buffer, offset + bytesPerSample, bytesPerSample, isFloat) : l;
            output[f * 2] = l; output[f * 2 + 1] = r;
        }
        return output;
    }

    private static float ReadSample(byte[] b, int o, int bytes, bool isFloat) => bytes switch
    {
        4 when isFloat => BitConverter.ToSingle(b, o),
        4 => BitConverter.ToInt32(b, o) / 2147483648f,
        3 => ((b[o] << 8) | (b[o + 1] << 16) | (b[o + 2] << 24)) / 2147483648f,
        2 => BitConverter.ToInt16(b, o) / 32768f,
        1 => (b[o] - 128) / 128f,
        _ => 0,
    };

    private void Pump()
    {
        var mix = new float[Recorder.AudioRate / 5 * 2];
        var pcm = new short[mix.Length];
        while (!_stop)
        {
            Thread.Sleep(10);
            WriteUpTo(NowFrame - DelayFrames, mix, pcm);
        }
        WriteUpTo(NowFrame, mix, pcm); // flush the tail when recording stops
    }

    private void WriteUpTo(long target, float[] mix, short[] pcm)
    {
        while (_written < target)
        {
            int frames = (int)Math.Min(target - _written, mix.Length / 2);
            Array.Clear(mix, 0, frames * 2);
            if (MicrophoneActive) _micLine.MixInto(mix, _written, frames);
            if (SystemAudioActive) _loopLine.MixInto(mix, _written, frames);
            for (int i = 0; i < frames * 2; i++)
            {
                float v = mix[i];
                pcm[i] = (short)(v >= 1f ? short.MaxValue : v <= -1f ? short.MinValue : v * 32767f);
            }
            _recorder.WriteAudio(pcm, frames, _written);
            _written += frames;
        }
    }

    public void Dispose()
    {
        if (_stop) return;
        try { _mic?.StopRecording(); } catch (Exception e) { Log.Error("Microphone stop", e); }
        try { _loopback?.StopRecording(); } catch (Exception e) { Log.Error("System audio stop", e); }
        _stop = true;
        _pump?.Join(2000);
        _mic?.Dispose(); _loopback?.Dispose();
        MicrophoneActive = SystemAudioActive = false;
    }
}
