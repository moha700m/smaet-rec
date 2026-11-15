using SmaetRec.Core;
using Vortice.MediaFoundation;

namespace SmaetRec.Media;

/// <summary>Starts Media Foundation once for the process.</summary>
public static class MediaFoundationRuntime
{
    private static readonly object Lock = new();
    private static bool _started;
    public static string? StartError { get; private set; }

    public static bool EnsureStarted()
    {
        lock (Lock)
        {
            if (_started) return true;
            try
            {
                MediaFactory.MFStartup(false).CheckError();
                _started = true;
                return true;
            }
            catch (Exception e) when (e is SharpGen.Runtime.SharpGenException or DllNotFoundException or EntryPointNotFoundException)
            {
                StartError = e.Message;
                Log.Error("Media Foundation start", e);
                return false;
            }
        }
    }

    public const int NeedMoreInput = unchecked((int)0xC00D6D72);
    public const int StreamChange = unchecked((int)0xC00D6D61);
}
