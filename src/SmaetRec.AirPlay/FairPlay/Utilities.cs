// Small helpers needed by the adapted FairPlay code (same behaviour as the originals in airplayreceiver, MIT).
using System.Buffers.Binary;

namespace SmaetRec.AirPlay.FairPlay;

internal static class Utilities
{
    public static byte[] CopyOfRange(byte[] src, int start, int end)
    {
        var dest = new byte[end - start];
        Array.Copy(src, start, dest, 0, dest.Length);
        return dest;
    }

    public static void Swap(byte[] arr, int idxA, int idxB)
    {
        int a = BinaryPrimitives.ReadInt32LittleEndian(arr.AsSpan(idxA));
        int b = BinaryPrimitives.ReadInt32LittleEndian(arr.AsSpan(idxB));
        BinaryPrimitives.WriteInt32LittleEndian(arr.AsSpan(idxB), a);
        BinaryPrimitives.WriteInt32LittleEndian(arr.AsSpan(idxA), b);
    }
}
