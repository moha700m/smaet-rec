namespace SmaetRec.Core;

/// <summary>
/// A timeline of stereo float samples at 48 kHz, addressed by absolute sample position since recording start.
/// Incoming audio is placed where it happened in time, so silence gaps (e.g. nothing playing) stay silent.
/// </summary>
public sealed class AudioTimeline
{
    public const int Rate = 48000;
    private const int Capacity = Rate * 4; // frames
    private readonly float[] _data = new float[Capacity * 2];
    private readonly object _lock = new();
    private long _writePos;
    private double _resamplePhase;
    private float _lastL, _lastR;

    public long WritePosition { get { lock (_lock) return _writePos; } }

    /// <summary>Appends audio that ended "now" (<paramref name="nowFrame"/> on the recording clock).</summary>
    public void Append(ReadOnlySpan<float> stereo, int sourceRate, long nowFrame)
    {
        lock (_lock)
        {
            int inFrames = stereo.Length / 2;
            if (inFrames == 0) return;
            long outFrames = (long)inFrames * Rate / sourceRate;
            long start = nowFrame - outFrames;
            if (start - _writePos > Rate / 20) { ZeroRange(_writePos, start); _writePos = start; }   // gap: keep it silent
            else if (_writePos - nowFrame > Rate / 4) return;                                          // far ahead of the clock: drop

            if (sourceRate == Rate)
            {
                for (int i = 0; i < inFrames; i++) Put(stereo[i * 2], stereo[i * 2 + 1]);
            }
            else
            {
                double step = (double)sourceRate / Rate;
                double pos = _resamplePhase;
                while (pos < inFrames)
                {
                    int i = (int)pos;
                    float frac = (float)(pos - i);
                    float l0 = i == 0 ? _lastL : stereo[(i - 1) * 2], r0 = i == 0 ? _lastR : stereo[(i - 1) * 2 + 1];
                    Put(l0 + (stereo[i * 2] - l0) * frac, r0 + (stereo[i * 2 + 1] - r0) * frac);
                    pos += step;
                }
                _resamplePhase = pos - inFrames;
            }
            _lastL = stereo[(inFrames - 1) * 2]; _lastR = stereo[(inFrames - 1) * 2 + 1];
        }
    }

    private void Put(float l, float r)
    {
        if (_writePos >= 0)
        {
            int i = (int)(_writePos % Capacity) * 2;
            _data[i] = l; _data[i + 1] = r;
        }
        _writePos++;
    }

    private void ZeroRange(long from, long to)
    {
        from = Math.Max(from, to - Capacity);
        for (long p = Math.Max(0, from); p < to; p++)
        {
            int i = (int)(p % Capacity) * 2;
            _data[i] = 0; _data[i + 1] = 0;
        }
    }

    /// <summary>Adds this timeline's samples for [start, start+frames) into <paramref name="mix"/>. Missing audio counts as silence.</summary>
    public void MixInto(float[] mix, long start, int frames)
    {
        lock (_lock)
        {
            for (int n = 0; n < frames; n++)
            {
                long p = start + n;
                if (p < 0 || p >= _writePos || p < _writePos - Capacity) continue;
                int i = (int)(p % Capacity) * 2;
                mix[n * 2] += _data[i]; mix[n * 2 + 1] += _data[i + 1];
            }
        }
    }
}
