using System.Security.Cryptography;
using System.Text;

namespace SmaetRec.AirPlay;

/// <summary>AES-128 in counter mode as one continuous stream (keystream position carries across calls).</summary>
public sealed class AesCtrStream : IDisposable
{
    private readonly Aes _aes;
    private readonly byte[] _counter = new byte[16];
    private readonly byte[] _keystream = new byte[16];
    private int _used = 16;

    public AesCtrStream(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv)
    {
        _aes = Aes.Create();
        _aes.Key = key[..16].ToArray();
        iv[..16].CopyTo(_counter);
    }

    public void Process(Span<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            if (_used == 16)
            {
                _aes.EncryptEcb(_counter, _keystream, PaddingMode.None);
                for (int j = 15; j >= 0 && ++_counter[j] == 0; j--) { }
                _used = 0;
            }
            data[i] ^= _keystream[_used++];
        }
    }

    public void Dispose() => _aes.Dispose();
}

internal static class Hashing
{
    public static byte[] Sha512Prefix16(byte[] first, byte[] second)
    {
        var combined = new byte[first.Length + second.Length];
        first.CopyTo(combined, 0);
        second.CopyTo(combined, first.Length);
        return SHA512.HashData(combined)[..16];
    }

    public static byte[] Sha512Prefix16(string label, byte[] second) => Sha512Prefix16(Encoding.UTF8.GetBytes(label), second);
}
