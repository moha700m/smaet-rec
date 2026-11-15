using System.Buffers.Binary;
using SmaetRec.AirPlay.FairPlay;

namespace SmaetRec.AirPlay;

/// <summary>One H.264 access unit in Annex-B form (start codes), ready for a decoder.</summary>
public sealed record VideoPacket(byte[] Data, bool IsKeyFrame, long TimestampMicros, int Width, int Height);

/// <summary>
/// Turns the AirPlay mirroring byte stream (128-byte headers + payloads) into decodable H.264 packets.
/// Pure logic: no sockets, so it can be unit-tested.
/// </summary>
public sealed class MirrorStreamDecoder : IDisposable
{
    public const int HeaderSize = 128;
    private readonly AesCtrStream _cipher;
    private byte[]? _parameterSets;
    private bool _parameterSetsPending;
    public int Width { get; private set; }
    public int Height { get; private set; }

    public MirrorStreamDecoder(byte[] streamKey, byte[] streamIv) => _cipher = new AesCtrStream(streamKey, streamIv);

    /// <summary>Derives the video stream key/iv exactly as the sender does.</summary>
    public static MirrorStreamDecoder Create(byte[] fairPlayKeyMessage, byte[] encryptedKey, byte[] ecdhShared, ulong streamConnectionId)
    {
        var aesKey = new byte[16];
        new OmgHax().DecryptAesKey(fairPlayKeyMessage, encryptedKey, aesKey);
        return FromSessionKey(aesKey, ecdhShared, streamConnectionId);
    }

    public static MirrorStreamDecoder FromSessionKey(byte[] aesKey, byte[] ecdhShared, ulong streamConnectionId)
    {
        var eaesKey = Hashing.Sha512Prefix16(aesKey, ecdhShared);
        var key = Hashing.Sha512Prefix16("AirPlayStreamKey" + streamConnectionId, eaesKey);
        var iv = Hashing.Sha512Prefix16("AirPlayStreamIV" + streamConnectionId, eaesKey);
        return new MirrorStreamDecoder(key, iv);
    }

    public static (int payloadSize, int payloadType) ParseHeader(ReadOnlySpan<byte> header) =>
        (BinaryPrimitives.ReadInt32LittleEndian(header), BinaryPrimitives.ReadUInt16LittleEndian(header[4..]) & 0xFF);

    /// <summary>Handles one packet. Returns a video packet when the payload was a picture, otherwise null.</summary>
    public VideoPacket? Handle(ReadOnlySpan<byte> header, byte[] payload)
    {
        var (_, type) = ParseHeader(header);
        if (type == 1)
        {
            int w = (int)BinaryPrimitives.ReadSingleLittleEndian(header[40..]);
            int h = (int)BinaryPrimitives.ReadSingleLittleEndian(header[44..]);
            if (w > 0 && h > 0) { Width = w; Height = h; }
            var sets = ParseAvcConfig(payload);
            if (sets != null) { _parameterSets = sets; _parameterSetsPending = true; }
            return null;
        }
        if (type != 0) return null;

        _cipher.Process(payload);
        bool key = false;
        int pos = 0;
        while (pos + 4 <= payload.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(pos));
            if (len <= 0 || pos + 4 + len > payload.Length) return null; // not a valid picture (wrong key or damaged)
            payload[pos] = 0; payload[pos + 1] = 0; payload[pos + 2] = 0; payload[pos + 3] = 1;
            if ((payload[pos + 4] & 0x1F) == 5) key = true;
            pos += 4 + len;
        }
        if (pos != payload.Length || _parameterSets == null) return null;

        ulong ntp = BinaryPrimitives.ReadUInt64LittleEndian(header[8..]);
        long micros = (long)(ntp >> 32) * 1_000_000 + (long)((ntp & 0xFFFFFFFF) * 1_000_000 >> 32);
        byte[] data = payload;
        if (key || _parameterSetsPending)
        {
            data = new byte[_parameterSets.Length + payload.Length];
            _parameterSets.CopyTo(data, 0);
            payload.CopyTo(data, _parameterSets.Length);
            _parameterSetsPending = false;
        }
        return new VideoPacket(data, key, micros, Width, Height);
    }

    /// <summary>Converts an avcC configuration record into Annex-B SPS/PPS.</summary>
    public static byte[]? ParseAvcConfig(ReadOnlySpan<byte> p)
    {
        if (p.Length < 8) return null;
        using var ms = new MemoryStream();
        int pos = 5;
        int spsCount = p[pos++] & 0x1F;
        for (int i = 0; i < spsCount; i++)
        {
            if (pos + 2 > p.Length) return null;
            int len = BinaryPrimitives.ReadUInt16BigEndian(p[pos..]); pos += 2;
            if (len == 0 || pos + len > p.Length) return null;
            ms.Write(stackalloc byte[] { 0, 0, 0, 1 }); ms.Write(p.Slice(pos, len)); pos += len;
        }
        if (pos >= p.Length) return null;
        int ppsCount = p[pos++];
        for (int i = 0; i < ppsCount; i++)
        {
            if (pos + 2 > p.Length) return null;
            int len = BinaryPrimitives.ReadUInt16BigEndian(p[pos..]); pos += 2;
            if (len == 0 || pos + len > p.Length) return null;
            ms.Write(stackalloc byte[] { 0, 0, 0, 1 }); ms.Write(p.Slice(pos, len)); pos += len;
        }
        return spsCount > 0 && ppsCount > 0 ? ms.ToArray() : null;
    }

    public void Dispose() => _cipher.Dispose();
}
