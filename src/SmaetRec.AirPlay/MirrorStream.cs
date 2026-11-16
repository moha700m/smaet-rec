using System.Buffers.Binary;
using SmaetRec.AirPlay.FairPlay;

namespace SmaetRec.AirPlay;

/// <summary>One H.264 access unit in Annex-B form (start codes), ready for a decoder.</summary>
public sealed record VideoPacket(byte[] Data, bool IsKeyFrame, long TimestampMicros, int Width, int Height);

/// <summary>
/// Turns the AirPlay mirroring byte stream (128-byte headers + payloads) into decodable H.264 packets.
/// Pure logic: no sockets, so it can be unit-tested.
/// </summary>
/// <remarks>
/// The iPhone encrypts the picture stream as one continuous AES-128-CTR keystream that runs across
/// packet boundaries. A packet is not padded to 16 bytes, so the unused tail of the last keystream
/// block belongs to the first bytes of the next packet. Resetting the cipher per packet (a mistake
/// this class used to make) decrypts everything after the first odd-length packet into garbage.
/// </remarks>
public sealed class MirrorStreamDecoder : IDisposable
{
    public const int HeaderSize = 128;
    private static readonly byte[] StartCode = { 0, 0, 0, 1 };
    private readonly AesCtrStream _cipher;
    private byte[]? _parameterSets;
    private bool _parameterSetsPending;

    public int Width { get; private set; }
    public int Height { get; private set; }
    /// <summary>Headers handled, including SPS/PPS and statistics packets.</summary>
    public long Packets { get; private set; }
    /// <summary>Pictures handed to the decoder.</summary>
    public long Pictures { get; private set; }
    public long KeyFrames { get; private set; }
    /// <summary>Pictures dropped because decryption produced an impossible NAL layout.</summary>
    public long InvalidPackets { get; private set; }
    /// <summary>True when the iPhone started sending HEVC, which this receiver does not decode.</summary>
    public bool UnsupportedCodec { get; private set; }
    public string? LastIssue { get; private set; }

    public MirrorStreamDecoder(byte[] streamKey, byte[] streamIv) => _cipher = new AesCtrStream(streamKey, streamIv);

    /// <summary>Derives the video stream key/iv exactly as the sender does.</summary>
    public static MirrorStreamDecoder Create(byte[]? fairPlayKeyMessage, byte[]? encryptedKey, byte[]? ecdhShared, ulong streamConnectionId, bool mixEcdh = true)
    {
        if (fairPlayKeyMessage is not { Length: >= 16 })
            throw new InvalidOperationException("The iPhone never completed the FairPlay handshake (fp-setup).");
        if (encryptedKey is not { Length: 72 })
            throw new InvalidOperationException($"Unexpected ekey length {encryptedKey?.Length ?? 0}; expected 72 bytes.");

        var aesKey = new byte[16];
        new OmgHax().DecryptAesKey(fairPlayKeyMessage, encryptedKey, aesKey);
        return FromSessionKey(aesKey, mixEcdh ? ecdhShared : null, streamConnectionId);
    }

    /// <summary>
    /// Derives the picture key. When the iPhone paired with us, the FairPlay key is mixed with the
    /// ECDH shared secret; older/unpaired clients use the FairPlay key directly.
    /// </summary>
    public static MirrorStreamDecoder FromSessionKey(byte[] aesKey, byte[]? ecdhShared, ulong streamConnectionId)
    {
        var eaesKey = ecdhShared is { Length: 32 } ? Hashing.Sha512Prefix16(aesKey, ecdhShared) : aesKey;
        var key = Hashing.Sha512Prefix16("AirPlayStreamKey" + streamConnectionId, eaesKey);
        var iv = Hashing.Sha512Prefix16("AirPlayStreamIV" + streamConnectionId, eaesKey);
        return new MirrorStreamDecoder(key, iv);
    }

    public static (int payloadSize, int payloadType) ParseHeader(ReadOnlySpan<byte> header) =>
        (BinaryPrimitives.ReadInt32LittleEndian(header), header[4]);

    /// <summary>Handles one packet. Returns a video packet when the payload was a picture, otherwise null.</summary>
    public VideoPacket? Handle(ReadOnlySpan<byte> header, byte[] payload)
    {
        var (_, type) = ParseHeader(header);
        Packets++;

        if (type == 1)
        {
            ReadPictureSize(header);
            // An empty type-1 packet is the sender's pause/resume notification
            // (option 0x56/0x5e/0x16), not a codec configuration.
            if (payload.Length == 0) return null;
            if (payload.Length > 8 && payload[4] == (byte)'h' && payload[5] == (byte)'v')
            {
                UnsupportedCodec = true;
                LastIssue = "The iPhone is sending HEVC (H.265); this build decodes H.264 only.";
                return null;
            }
            var sets = ParseAvcConfig(payload) ?? TryRawParameterSets(payload);
            if (sets != null)
            {
                _parameterSets = sets;
                _parameterSetsPending = true;
                LastIssue = null;
            }
            else
            {
                LastIssue = "Could not read SPS/PPS from the video config packet.";
            }
            return null;
        }

        if (type != 0 || payload.Length == 0) return null;

        // One continuous keystream for the whole session: do NOT restart it per packet.
        _cipher.Process(payload);

        bool key = false;
        using var foundSets = new MemoryStream();
        int pos = 0;
        while (pos + 4 <= payload.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(pos));
            if (len <= 0 || len > payload.Length - pos - 4)
            {
                InvalidPackets++;
                LastIssue = "Decryption produced an invalid NAL layout (key or counter mismatch).";
                return null;
            }
            int nal = payload[pos + 4] & 0x1F;
            if (nal == 5) key = true;
            if (nal is 7 or 8)
            {
                foundSets.Write(StartCode);
                foundSets.Write(payload, pos + 4, len);
            }
            payload[pos] = 0; payload[pos + 1] = 0; payload[pos + 2] = 0; payload[pos + 3] = 1;
            pos += 4 + len;
        }
        if (pos != payload.Length)
        {
            InvalidPackets++;
            LastIssue = "Decryption produced an invalid NAL layout (key or counter mismatch).";
            return null;
        }

        if (foundSets.Length > 0)
        {
            _parameterSets = foundSets.ToArray();
            _parameterSetsPending = false;
        }

        ulong ntp = BinaryPrimitives.ReadUInt64LittleEndian(header[8..]);
        long micros = (long)(ntp >> 32) * 1_000_000 + (long)((ntp & 0xFFFFFFFF) * 1_000_000 >> 32);

        byte[] data = payload;
        bool prepend = _parameterSets != null && (key || _parameterSetsPending);
        if (prepend)
        {
            data = new byte[_parameterSets!.Length + payload.Length];
            _parameterSets.CopyTo(data, 0);
            payload.CopyTo(data, _parameterSets.Length);
            _parameterSetsPending = false;
        }

        Pictures++;
        if (key) KeyFrames++;
        // A key frame without SPS/PPS cannot start the decoder; report it as a delta so the
        // decoder keeps waiting for the next usable IDR instead of failing on it.
        return new VideoPacket(data, key && _parameterSets != null, micros, Width, Height);
    }

    private void ReadPictureSize(ReadOnlySpan<byte> header)
    {
        int w = (int)BinaryPrimitives.ReadSingleLittleEndian(header[40..]);
        int h = (int)BinaryPrimitives.ReadSingleLittleEndian(header[44..]);
        if (w <= 0 || h <= 0)
        {
            w = (int)BinaryPrimitives.ReadSingleLittleEndian(header[56..]);
            h = (int)BinaryPrimitives.ReadSingleLittleEndian(header[60..]);
        }
        if (w > 0 && h > 0) { Width = w; Height = h; }
    }

    private static byte[]? TryRawParameterSets(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > 8 && payload[0] == 0 && payload[1] == 0 && payload[2] == 0 && payload[3] == 1)
            return payload.ToArray();
        return null;
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
            ms.Write(StartCode); ms.Write(p.Slice(pos, len)); pos += len;
        }
        if (pos >= p.Length) return null;
        int ppsCount = p[pos++];
        for (int i = 0; i < ppsCount; i++)
        {
            if (pos + 2 > p.Length) return null;
            int len = BinaryPrimitives.ReadUInt16BigEndian(p[pos..]); pos += 2;
            if (len == 0 || pos + len > p.Length) return null;
            ms.Write(StartCode); ms.Write(p.Slice(pos, len)); pos += len;
        }
        return spsCount > 0 && ppsCount > 0 ? ms.ToArray() : null;
    }

    public void Dispose() => _cipher.Dispose();
}
