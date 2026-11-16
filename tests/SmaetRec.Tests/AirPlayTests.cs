using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Math.EC.Rfc7748;
using Org.BouncyCastle.Math.EC.Rfc8032;
using SmaetRec.AirPlay;
using SmaetRec.AirPlay.FairPlay;

namespace SmaetRec.Tests;

public class PlistTests
{
    [Fact]
    public void Round_trips_nested_values()
    {
        var big = Enumerable.Range(0, 300).Select(i => (object)i).ToList();
        var input = new Dictionary<string, object>
        {
            ["name"] = "SMAET REC", ["arabic"] = "آيفون", ["yes"] = true, ["no"] = false,
            ["small"] = 7, ["port"] = 7000, ["big"] = 0x5A7FFEE6L, ["huge"] = unchecked((long)0xF000000000000001UL),
            ["real"] = 1.0 / 60.0, ["data"] = new byte[] { 1, 2, 3 }, ["long data"] = new byte[40],
            ["list"] = new List<object> { new Dictionary<string, object> { ["type"] = 110, ["dataPort"] = 7100 } },
            ["many"] = big,
        };
        var output = (Dictionary<string, object?>)BinaryPlist.Read(BinaryPlist.Write(input))!;
        Assert.Equal("SMAET REC", output["name"]);
        Assert.Equal("آيفون", output["arabic"]);
        Assert.Equal(true, output["yes"]); Assert.Equal(false, output["no"]);
        Assert.Equal(7L, output["small"]); Assert.Equal(7000L, output["port"]); Assert.Equal(0x5A7FFEE6L, output["big"]);
        Assert.Equal(unchecked((long)0xF000000000000001UL), output["huge"]);
        Assert.Equal(1.0 / 60.0, (double)output["real"]!, 12);
        Assert.Equal(new byte[] { 1, 2, 3 }, output["data"]);
        Assert.Equal(40, ((byte[])output["long data"]!).Length);
        var stream = (Dictionary<string, object?>)((List<object?>)output["list"]!)[0]!;
        Assert.Equal(110L, stream["type"]);
        Assert.Equal(300, ((List<object?>)output["many"]!).Count);
        Assert.Equal(299L, ((List<object?>)output["many"]!)[299]);
    }

    [Fact]
    public void Rejects_garbage()
    {
        Assert.Throws<FormatException>(() => BinaryPlist.Read(new byte[64]));
        Assert.Throws<FormatException>(() => BinaryPlist.Read("bplist00"u8.ToArray()));
    }
}

public class CryptoTests
{
    [Fact]
    public void AesCtr_matches_NIST_SP800_38A_vector_across_split_calls()
    {
        var key = Convert.FromHexString("2b7e151628aed2a6abf7158809cf4f3c");
        var iv = Convert.FromHexString("f0f1f2f3f4f5f6f7f8f9fafbfcfdfeff");
        var plain = Convert.FromHexString("6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710");
        var expected = "874d6191b620e3261bef6864990db6ce9806f66b7970fdff8617187bb9fffdff5ae4df3edbd5d35e5b4f09020db03eab1e031dda2fbe03d1792170a0f3009cee";
        using var ctr = new AesCtrStream(key, iv);
        ctr.Process(plain.AsSpan(0, 5));
        ctr.Process(plain.AsSpan(5, 30));
        ctr.Process(plain.AsSpan(35));
        Assert.Equal(expected, Convert.ToHexString(plain).ToLowerInvariant());
    }

    [Theory]
    [InlineData("46504c590301030000000098008f1a9ca548fdd57560a52926ff399f2eb154d0a7a0fffc997f58e27e00499eb9f310110d019e550e328047aea54308ab71b647041406878af96e06cf74127ae35941dceb58931b5543b39903f9f76a376248ee52e3656b561e1c1a0106ec6608df0ab4f2df528e65db6d622d3892d5b49c6c025606a574f19ebea7d93500bdd69db23333f22edcb3ccf7a6acde7389f2facabfa61b0b50", "RlBMWQECAQAAAAA8AAAAAG1EuhK5H0jgYesjD8U6v6IAAAAQihBgRl1RuAjfES0ItgRQH54+opzgkC88Q7gdUxnQV194UX4B", "0496a612172f41e0fd71912acc33fc54")]
    [InlineData("46504c590301030000000098018f1a9c144a77fb15383f69cf6ba6ae3504582d489a121c644dac40bfb382388d758b294841cbe51bb4feea983f9157a1fb2e57765d1bfc7262053ca6f75c90c82794a43b8d844637aa018c28619a43da6727c7faf81b911a92f317d6ac7a3e1a7b923fe693cffb37317159be8904556862d81ed4f794957dc330b7e681e5a0067f596a0f3f936dd761f5afa2d69ab77938328bb1fcd92e", "RlBMWQECAQAAAAA8AAAAANeaxph3yjgkpIJITHZVWPoAAAAQ4XcsQxoGY1RTSf35eMmoaU2Lt01gaTE63jPkQGxeWkvz7zdc", "1512816fbdbe4856570931c3ec7d0e3d")]
    [InlineData("46504c590301030000000098028f1a9c049ae04d1691802802c75b3ced9204acbeb5482b582f4faf3c008d7dd3675a37967e3bee3079bec95b8bfeea69aaec8233c7ab3b7df283e8f9a50b8ecdcc53e3ee2e5ee1d78421378fdf8cfa1e1c04995d3c6f14b47e9487f3458cc6e4727fe1e3ad2b1db60afdb590c14da5011404ab0972c15ab14ad6a71ebd8cab10098bc1b1822b14dd3e496fe13bfcca8fd399eee52581d4", "RlBMWQECAQAAAAA8AAAAAPKmjeIsM+mWHcEmRgyvv/8AAAAQPLQ8J5ikttb6JDKvhiMnKhy0KXTvqQC0D8/g7vTLKP6mgoLM", "bc3b888f894276c7dd43c3739a08947c")]
    [InlineData("46504c590301030000000098038f1a9c63b126a2325158ff9ba6e5d9996b33cf4fda80c7bb3492ee15790fa57b5cdf0129fc3d29a2f6fb66ef4494aca148ddff2b726df69de41caf7292a3a960673699430df2eb5742ab52e61d5ef576ffbfb960686c14976dce6c66b07b491b94a41b445152a4449221fc704645fe1d5af60f446c3dbf1030580eb2c0002e387053fc9783d04dfba861da3f1444664737bcc2d82f4a5c", "RlBMWQECAQAAAAA8AAAAAN3zvfbLYDrZPRe0ckYxlXcAAAAQgeYGpQr5o7TKIkKljM9O1rlsOKSiBGHwGDNx4BWE+h2Wp3eO", "63082ff46b87d13d8211e048a696d914")]
    public void FairPlay_matches_the_known_playfair_vectors(string messageHex, string keyBase64, string expectedHex)
    {
        var key = new byte[16];
        new OmgHax().DecryptAesKey(Convert.FromHexString(messageHex), Convert.FromBase64String(keyBase64), key);
        Assert.Equal(expectedHex, Convert.ToHexString(key).ToLowerInvariant());
    }

    [Fact]
    public void FairPlay_tables_load_and_key_decryption_is_deterministic()
    {
        var keyMessage = new byte[164];
        var encryptedKey = new byte[72];
        for (int i = 0; i < keyMessage.Length; i++) keyMessage[i] = (byte)(i * 7);
        for (int i = 0; i < encryptedKey.Length; i++) encryptedKey[i] = (byte)(i * 13);
        keyMessage[12] = 2;
        var a = new byte[16]; var b = new byte[16];
        new OmgHax().DecryptAesKey(keyMessage, encryptedKey, a);
        new OmgHax().DecryptAesKey(keyMessage, encryptedKey, b);
        Assert.Equal(a, b);
        Assert.Contains(a, x => x != 0);
        encryptedKey[20] ^= 1;
        new OmgHax().DecryptAesKey(keyMessage, encryptedKey, b);
        Assert.NotEqual(a, b);
    }
}

public class RtspTests
{
    [Fact]
    public async Task Reads_pipelined_requests_with_bodies()
    {
        var bytes = Encoding.ASCII.GetBytes("POST /pair-setup RTSP/1.0\r\nCSeq: 1\r\nContent-Length: 4\r\n\r\nABCDGET /info RTSP/1.0\r\nCSeq: 2\r\n\r\n");
        var reader = new RtspReader(new TrickleStream(bytes, 7));
        var first = await reader.ReadAsync(default);
        Assert.Equal(("POST", "/pair-setup", "ABCD"), (first!.Method, first.Path, Encoding.ASCII.GetString(first.Body)));
        var second = await reader.ReadAsync(default);
        Assert.Equal(("GET", "/info", "2"), (second!.Method, second.Path, second.Headers["cseq"]));
        Assert.Null(await reader.ReadAsync(default));

        var text = Encoding.ASCII.GetString(new RtspResponse { Body = new byte[] { 65 } }.ToBytes(second));
        Assert.StartsWith("RTSP/1.0 200 OK\r\nCSeq: 2\r\n", text);
        Assert.EndsWith("Content-Length: 1\r\n\r\nA", text);
    }

    /// <summary>Delivers data a few bytes at a time, like a slow network.</summary>
    private sealed class TrickleStream(byte[] data, int chunk) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            base.ReadAsync(buffer[..Math.Min(chunk, buffer.Length)], ct);
    }
}

public class MirrorStreamTests
{
    private static byte[] Header(int size, int type, ulong ntp = 0, float w = 0, float h = 0)
    {
        var header = new byte[128];
        BinaryPrimitives.WriteInt32LittleEndian(header, size);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), (ushort)type);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8), ntp);
        BinaryPrimitives.WriteSingleLittleEndian(header.AsSpan(40), w);
        BinaryPrimitives.WriteSingleLittleEndian(header.AsSpan(44), h);
        return header;
    }

    private static byte[] Avcc(params byte[][] nals)
    {
        using var ms = new MemoryStream();
        foreach (var n in nals) { var len = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(len, n.Length); ms.Write(len); ms.Write(n); }
        return ms.ToArray();
    }

    [Fact]
    public void Decrypts_pictures_and_adds_parameter_sets_to_key_frames()
    {
        var key = RandomNumberGenerator.GetBytes(16); var iv = RandomNumberGenerator.GetBytes(16);
        using var sender = new AesCtrStream(key, iv);
        using var decoder = new MirrorStreamDecoder(key, iv);

        byte[] sps = { 0x67, 1, 2, 3 }, pps = { 0x68, 9 };
        byte[] config = new byte[] { 1, 0x64, 0, 0x1f, 0xff, 0xe1, 0, 4 }.Concat(sps).Concat(new byte[] { 1, 0, 2 }).Concat(pps).ToArray();
        Assert.Null(decoder.Handle(Header(config.Length, 1, 0, 1170, 2532), config));
        Assert.Equal((1170, 2532), (decoder.Width, decoder.Height));

        // Key frame of odd length: the iPhone's AES-CTR keystream continues across packets,
        // so the sender and the receiver must not reset it at a packet boundary.
        var idr = Avcc(new byte[] { 0x65, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        sender.Process(idr);
        var p1 = decoder.Handle(Header(idr.Length, 0, (5UL << 32) | 0x80000000), idr)!;
        Assert.True(p1.IsKeyFrame);
        Assert.Equal(5_500_000, p1.TimestampMicros);
        Assert.Equal(new byte[] { 0, 0, 0, 1, 0x67, 1, 2, 3, 0, 0, 0, 1, 0x68, 9, 0, 0, 0, 1, 0x65, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, p1.Data);

        var inter = Avcc(new byte[] { 0x41, 7, 7 }, new byte[] { 0x41, 8 });
        sender.Process(inter);
        var p2 = decoder.Handle(Header(inter.Length, 0), inter)!;
        Assert.False(p2.IsKeyFrame);
        Assert.Equal(new byte[] { 0, 0, 0, 1, 0x41, 7, 7, 0, 0, 0, 1, 0x41, 8 }, p2.Data);

        Assert.Null(decoder.Handle(Header(3, 5), new byte[3])); // other packet types are ignored
    }

    [Fact]
    public void Unpaired_iphone_uses_the_fairplay_key_directly()
    {
        var fairplayKey = RandomNumberGenerator.GetBytes(16);
        const ulong id = 0x0123456789ABCDEF;
        var packetKey = SHA512.HashData(Encoding.UTF8.GetBytes("AirPlayStreamKey" + id).Concat(fairplayKey).ToArray())[..16];
        var packetIv = SHA512.HashData(Encoding.UTF8.GetBytes("AirPlayStreamIV" + id).Concat(fairplayKey).ToArray())[..16];

        using var sender = new AesCtrStream(packetKey, packetIv);
        using var decoder = MirrorStreamDecoder.FromSessionKey(fairplayKey, null, id);
        byte[] sps = { 0x67, 1, 2, 3 }, pps = { 0x68, 9 };
        byte[] config = new byte[] { 1, 0x64, 0, 0x1f, 0xff, 0xe1, 0, 4 }.Concat(sps).Concat(new byte[] { 1, 0, 2 }).Concat(pps).ToArray();
        decoder.Handle(Header(config.Length, 1), config);
        var frame = Avcc(new byte[] { 0x65, 4, 5, 6 });
        sender.Process(frame);
        var decoded = decoder.Handle(Header(frame.Length, 0), frame);
        Assert.NotNull(decoded);
        Assert.True(decoded!.IsKeyFrame);
        Assert.Equal(new byte[] { 0, 0, 0, 1, 0x67, 1, 2, 3, 0, 0, 0, 1, 0x68, 9, 0, 0, 0, 1, 0x65, 4, 5, 6 }, decoded.Data);
    }

    [Fact]
    public void Keystream_continues_across_many_odd_length_packets()
    {
        var key = RandomNumberGenerator.GetBytes(16); var iv = RandomNumberGenerator.GetBytes(16);
        using var sender = new AesCtrStream(key, iv);
        using var decoder = new MirrorStreamDecoder(key, iv);

        byte[] sps = { 0x67, 1, 2, 3 }, pps = { 0x68, 9 };
        byte[] config = new byte[] { 1, 0x64, 0, 0x1f, 0xff, 0xe1, 0, 4 }.Concat(sps).Concat(new byte[] { 1, 0, 2 }).Concat(pps).ToArray();
        decoder.Handle(Header(config.Length, 1), config);

        // Deliberately odd sizes (15, 29, 37 bytes) so a per-packet block reset would corrupt the rest.
        var sizes = new[] { 11, 25, 33 };
        for (int i = 0; i < sizes.Length; i++)
        {
            var nal = new byte[sizes[i]]; nal[0] = (byte)(i == 0 ? 0x65 : 0x41);
            for (int j = 1; j < nal.Length; j++) nal[j] = (byte)(i * 31 + j);
            var frame = Avcc(nal);
            sender.Process(frame);
            var decoded = decoder.Handle(Header(frame.Length, 0, (ulong)(i + 1) << 32), frame);
            Assert.NotNull(decoded);
            Assert.Equal(i == 0, decoded!.IsKeyFrame);
            Assert.Equal(nal, decoded.Data[^nal.Length..]);
        }
        Assert.Equal(3, decoder.Pictures);
        Assert.Equal(0, decoder.InvalidPackets);
        Assert.Null(decoder.LastIssue);
    }

    [Fact]
    public void Wrong_key_yields_no_picture_instead_of_garbage()
    {
        using var sender = new AesCtrStream(new byte[16], new byte[16]);
        using var decoder = new MirrorStreamDecoder(Enumerable.Repeat((byte)1, 16).ToArray(), new byte[16]);
        var config = new byte[] { 1, 0x64, 0, 0x1f, 0xff, 0xe1, 0, 1, 0x67, 1, 0, 1, 0x68 };
        decoder.Handle(Header(config.Length, 1), config);
        var frame = Avcc(new byte[] { 0x65, 1, 2, 3 });
        sender.Process(frame);
        Assert.Null(decoder.Handle(Header(frame.Length, 0), frame));
    }
}

public class ReceiverProtocolTests
{
    private static byte[] Avcc(params byte[][] nals)
    {
        using var ms = new MemoryStream();
        foreach (var n in nals)
        {
            var len = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(len, n.Length);
            ms.Write(len); ms.Write(n);
        }
        return ms.ToArray();
    }

    private static async Task<(int status, Dictionary<string, string> headers, byte[] body)> Send(NetworkStream s, string method, string path, byte[]? body = null, int cseq = 1)
    {
        body ??= Array.Empty<byte>();
        var head = Encoding.ASCII.GetBytes($"{method} {path} RTSP/1.0\r\nCSeq: {cseq}\r\nContent-Length: {body.Length}\r\n\r\n");
        await s.WriteAsync(head.Concat(body).ToArray());
        // The response has the same framing as a request, apart from the first line.
        var buffer = new List<byte>();
        var one = new byte[1];
        while (!Encoding.ASCII.GetString(buffer.ToArray()).EndsWith("\r\n\r\n"))
        {
            if (await s.ReadAsync(one) == 0) throw new IOException("closed");
            buffer.Add(one[0]);
        }
        var lines = Encoding.ASCII.GetString(buffer.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var headers = lines.Skip(1).ToDictionary(l => l[..l.IndexOf(':')], l => l[(l.IndexOf(':') + 1)..].Trim(), StringComparer.OrdinalIgnoreCase);
        var result = new byte[int.Parse(headers["Content-Length"])];
        int read = 0;
        while (read < result.Length) read += await s.ReadAsync(result.AsMemory(read));
        return (int.Parse(lines[0].Split(' ')[1]), headers, result);
    }

    [Fact]
    public async Task Receiver_answers_info_pairing_and_fairplay_setup_like_an_iphone_expects()
    {
        using var receiver = new AirPlayReceiver();
        var states = new List<ReceiverState>();
        receiver.StateChanged += states.Add;
        receiver.Start("Test Studio", advertise: false);
        Assert.Equal(ReceiverState.Waiting, receiver.State);
        Assert.Matches("^([0-9A-F]{2}:){5}[0-9A-F]{2}$", receiver.DeviceId);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, receiver.ControlPort);
        var s = client.GetStream();

        var info = await Send(s, "GET", "/info", cseq: 5);
        Assert.Equal(200, info.status);
        Assert.Equal("5", info.headers["CSeq"]);
        var plist = (Dictionary<string, object?>)BinaryPlist.Read(info.body)!;
        Assert.Equal("Test Studio", plist["name"]);
        Assert.Equal(receiver.PublicKey, plist["pk"]);
        var txt = (byte[])plist["txtAirPlay"]!;
        Assert.True(txt.Length > 20);
        Assert.Equal(txt[0], (byte)"deviceid=".Length + 17);
        Assert.Contains("features=0x5A7FFEE6,0x0", Encoding.ASCII.GetString(txt[1..]));

        var setup = await Send(s, "POST", "/pair-setup", new byte[32]);
        Assert.Equal(receiver.PublicKey, setup.body);

        // Play the iPhone's side of pair-verify.
        var edPrivate = RandomNumberGenerator.GetBytes(32); var edPublic = new byte[32];
        Ed25519.GeneratePublicKey(edPrivate, 0, edPublic, 0);
        var ecdhPrivate = RandomNumberGenerator.GetBytes(32); var ecdhPublic = new byte[32];
        X25519.ScalarMultBase(ecdhPrivate, 0, ecdhPublic, 0);
        var v1 = await Send(s, "POST", "/pair-verify", new byte[] { 1, 0, 0, 0 }.Concat(ecdhPublic).Concat(edPublic).ToArray());
        Assert.Equal(96, v1.body.Length);
        var serverEcdh = v1.body[..32];
        var shared = new byte[32];
        X25519.ScalarMult(ecdhPrivate, 0, serverEcdh, 0, shared, 0);
        using var cipher = new AesCtrStream(
            SHA512.HashData(Encoding.UTF8.GetBytes("Pair-Verify-AES-Key").Concat(shared).ToArray())[..16],
            SHA512.HashData(Encoding.UTF8.GetBytes("Pair-Verify-AES-IV").Concat(shared).ToArray())[..16]);
        var serverSignature = v1.body[32..];
        cipher.Process(serverSignature);
        var serverMessage = serverEcdh.Concat(ecdhPublic).ToArray();
        Assert.True(Ed25519.Verify(serverSignature, 0, receiver.PublicKey, 0, serverMessage, 0, 64), "receiver signature must verify");

        var ourSignature = new byte[64];
        var ourMessage = ecdhPublic.Concat(serverEcdh).ToArray();
        Ed25519.Sign(edPrivate, 0, ourMessage, 0, 64, ourSignature, 0);
        cipher.Process(ourSignature);
        var v2 = await Send(s, "POST", "/pair-verify", new byte[] { 0, 0, 0, 0 }.Concat(ourSignature).ToArray());
        Assert.Equal(200, v2.status);

        var fp1 = await Send(s, "POST", "/fp-setup", new byte[] { 0x46, 0x50, 0x4c, 0x59, 3, 1, 1, 0, 0, 0, 0, 4, 2, 0, 1, 0xbb });
        Assert.Equal(142, fp1.body.Length);
        Assert.Equal(1, fp1.body[13]);
        var fp2Request = new byte[164]; fp2Request[4] = 3;
        for (int i = 144; i < 164; i++) fp2Request[i] = (byte)i;
        var fp2 = await Send(s, "POST", "/fp-setup", fp2Request);
        Assert.Equal(32, fp2.body.Length);
        Assert.Equal(fp2Request[144..], fp2.body[12..]);

        // iOS 17/18 and some AirPlay 2 builds use a 168-byte second handshake.
        var fp2Long = new byte[168]; fp2Long[4] = 3;
        for (int i = 148; i < 168; i++) fp2Long[i] = (byte)(i + 1);
        var fp2LongReply = await Send(s, "POST", "/fp-setup", fp2Long);
        Assert.Equal(32, fp2LongReply.body.Length);
        Assert.Equal(fp2Long[148..], fp2LongReply.body[12..]);

        var setup1 = await Send(s, "SETUP", "rtsp://x/1", BinaryPlist.Write(new Dictionary<string, object> { ["ekey"] = new byte[72], ["eiv"] = new byte[16], ["timingPort"] = 0 }));
        var r1 = (Dictionary<string, object?>)BinaryPlist.Read(setup1.body)!;
        Assert.True((long)r1["eventPort"]! > 0 && (long)r1["timingPort"]! > 0);
        var setup2 = await Send(s, "SETUP", "rtsp://x/1", BinaryPlist.Write(new Dictionary<string, object>
        {
            ["streams"] = new List<object> { new Dictionary<string, object> { ["type"] = 110, ["streamConnectionID"] = unchecked((long)0xF123456789ABCDEFUL) } },
        }));
        var r2 = (Dictionary<string, object?>)BinaryPlist.Read(setup2.body)!;
        var stream = (Dictionary<string, object?>)((List<object?>)r2["streams"]!)[0]!;
        int dataPort = (int)(long)stream["dataPort"]!;
        Assert.Equal(200, (await Send(s, "POST", "/feedback")).status);
        Assert.Equal(400, (await Send(s, "SETUP", "rtsp://x/1", new byte[] { 1, 2, 3 })).status);

        // Opening the video connection switches the receiver to "connected"; closing it switches back.
        using (var video = new TcpClient())
        {
            await video.ConnectAsync(IPAddress.Loopback, dataPort);
            await WaitFor(() => receiver.State == ReceiverState.Connected);
        }
        await WaitFor(() => receiver.State == ReceiverState.Waiting);
        receiver.Stop();
        Assert.Equal(ReceiverState.Stopped, receiver.State);
        Assert.Contains(ReceiverState.Connected, states);
    }

    [Fact]
    public async Task Full_mirroring_session_delivers_decodable_pictures()
    {
        using var receiver = new AirPlayReceiver();
        var packets = new ConcurrentQueue<VideoPacket>();
        var threePictures = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.VideoPacket += p =>
        {
            packets.Enqueue(p);
            if (packets.Count >= 3) threePictures.TrySetResult(true);
        };
        receiver.Start("E2E Studio", advertise: false);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, receiver.ControlPort);
        var stream = client.GetStream();

        var info = await Send(stream, "GET", "/info");
        Assert.Equal(200, info.status);
        var infoPlist = (Dictionary<string, object?>)BinaryPlist.Read(info.body)!;
        Assert.True(infoPlist.ContainsKey("txtAirPlay"));

        Assert.Equal(receiver.PublicKey, (await Send(stream, "POST", "/pair-setup", new byte[32])).body);

        // pair-verify, played from the iPhone side.
        var edPrivate = RandomNumberGenerator.GetBytes(32); var edPublic = new byte[32];
        Ed25519.GeneratePublicKey(edPrivate, 0, edPublic, 0);
        var ecdhPrivate = RandomNumberGenerator.GetBytes(32); var ecdhPublic = new byte[32];
        X25519.ScalarMultBase(ecdhPrivate, 0, ecdhPublic, 0);
        var v1 = await Send(stream, "POST", "/pair-verify", new byte[] { 1, 0, 0, 0 }.Concat(ecdhPublic).Concat(edPublic).ToArray());
        Assert.Equal(200, v1.status);
        var serverEcdh = v1.body[..32];
        var shared = new byte[32];
        X25519.ScalarMult(ecdhPrivate, 0, serverEcdh, 0, shared, 0);
        using var pairCipher = new AesCtrStream(
            SHA512.HashData(Encoding.UTF8.GetBytes("Pair-Verify-AES-Key").Concat(shared).ToArray())[..16],
            SHA512.HashData(Encoding.UTF8.GetBytes("Pair-Verify-AES-IV").Concat(shared).ToArray())[..16]);
        var serverSignature = v1.body[32..];
        pairCipher.Process(serverSignature);
        Assert.True(Ed25519.Verify(serverSignature, 0, receiver.PublicKey, 0, serverEcdh.Concat(ecdhPublic).ToArray(), 0, 64));
        var ourSignature = new byte[64];
        Ed25519.Sign(edPrivate, 0, ecdhPublic.Concat(serverEcdh).ToArray(), 0, 64, ourSignature, 0);
        pairCipher.Process(ourSignature);
        Assert.Equal(200, (await Send(stream, "POST", "/pair-verify", new byte[] { 0, 0, 0, 0 }.Concat(ourSignature).ToArray())).status);

        // FairPlay handshake
        var fp1 = await Send(stream, "POST", "/fp-setup", new byte[] { 0x46, 0x50, 0x4c, 0x59, 3, 1, 1, 0, 0, 0, 0, 4, 2, 0, 0, 0xbb });
        Assert.Equal(142, fp1.body.Length);
        var fpMessage = new byte[164];
        for (int i = 0; i < fpMessage.Length; i++) fpMessage[i] = (byte)(i * 7 + 3);
        fpMessage[4] = 3;   // FPLY setup message
        fpMessage[5] = 1;   // type = setup
        fpMessage[6] = 3;   // sequence = second handshake
        fpMessage[12] = 0;  // FairPlay mode 0..3
        Assert.Equal(32, (await Send(stream, "POST", "/fp-setup", fpMessage)).body.Length);

        // SETUP 1: audio keys and timing.
        var ekey = new byte[72];
        for (int i = 0; i < ekey.Length; i++) ekey[i] = (byte)(i * 11 + 5);
        var setup1 = await Send(stream, "SETUP", "rtsp://x/1", BinaryPlist.Write(new Dictionary<string, object>
        {
            ["ekey"] = ekey, ["eiv"] = new byte[16], ["timingPort"] = 0,
            ["name"] = "Test iPhone", ["model"] = "iPhone15,2", ["osVersion"] = "18.1",
        }));
        var r1 = (Dictionary<string, object?>)BinaryPlist.Read(setup1.body)!;
        Assert.True((long)r1["timingPort"]! > 0);
        Assert.Equal("iPhone15,2", receiver.ClientModel);

        // SETUP 2: the mirror stream.
        const ulong streamId = 0x0123456789ABCDEF;
        var setup2 = await Send(stream, "SETUP", "rtsp://x/1", BinaryPlist.Write(new Dictionary<string, object>
        {
            ["streams"] = new List<object>
            {
                new Dictionary<string, object> { ["type"] = 110, ["streamConnectionID"] = unchecked((long)streamId) },
            },
        }));
        var r2 = (Dictionary<string, object?>)BinaryPlist.Read(setup2.body)!;
        var replyStream = (Dictionary<string, object?>)((List<object?>)r2["streams"]!)[0]!;
        int dataPort = (int)(long)replyStream["dataPort"]!;

        // Derive the picture key exactly as the receiver does.
        var aesKey = new byte[16];
        new OmgHax().DecryptAesKey(fpMessage, ekey, aesKey);
        var eaesKey = SHA512.HashData(aesKey.Concat(shared).ToArray())[..16];
        var pictureKey = SHA512.HashData(Encoding.UTF8.GetBytes("AirPlayStreamKey" + streamId).Concat(eaesKey).ToArray())[..16];
        var pictureIv = SHA512.HashData(Encoding.UTF8.GetBytes("AirPlayStreamIV" + streamId).Concat(eaesKey).ToArray())[..16];
        using var sender = new AesCtrStream(pictureKey, pictureIv);

        using var video = new TcpClient();
        await video.ConnectAsync(IPAddress.Loopback, dataPort);
        var vs = video.GetStream();

        async Task SendPacket(int type, byte[] payload, ulong ntp)
        {
            var header = new byte[128];
            BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), (ushort)type);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8), ntp);
            BinaryPrimitives.WriteSingleLittleEndian(header.AsSpan(40), 1170);
            BinaryPrimitives.WriteSingleLittleEndian(header.AsSpan(44), 2532);
            await vs.WriteAsync(header);
            await vs.WriteAsync(payload);
        }

        byte[] sps = { 0x67, 0x64, 0, 0x1f, 1, 2, 3 }, pps = { 0x68, 1, 2 };
        var config = new byte[] { 1, 0x64, 0, 0x1f, 0xff, 0xe1, 0, (byte)sps.Length }
            .Concat(sps).Concat(new byte[] { 1, 0, (byte)pps.Length }).Concat(pps).ToArray();
        await SendPacket(1, config, 0);

        var frameSizes = new[] { 15, 29, 37 };
        for (int i = 0; i < frameSizes.Length; i++)
        {
            var nal = new byte[frameSizes[i]];
            nal[0] = (byte)(i == 0 ? 0x65 : 0x41);
            for (int j = 1; j < nal.Length; j++) nal[j] = (byte)(i * 17 + j);
            var frame = Avcc(nal);
            sender.Process(frame);
            await SendPacket(0, frame, (ulong)(i + 1) << 32);
        }

        await threePictures.Task.WaitAsync(TimeSpan.FromSeconds(8));
        var received = packets.ToArray();
        Assert.Equal(3, received.Length);
        Assert.True(received[0].IsKeyFrame);
        Assert.False(received[1].IsKeyFrame);
        Assert.Equal(new byte[] { 0, 0, 0, 1, 0x67, 0x64, 0, 0x1f, 1, 2, 3, 0, 0, 0, 1, 0x68, 1, 2, 0, 0, 0, 1, 0x65, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 }, received[0].Data);
        Assert.Equal((1170, 2532), (received[0].Width, received[0].Height));
        Assert.Equal(3, receiver.MirrorPictures);
        Assert.Equal(0, receiver.MirrorInvalid);

        video.Close();
        await WaitFor(() => receiver.State == ReceiverState.Waiting);
    }

    [Fact]
    public async Task Receiver_can_restart_for_reconnect()
    {
        using var receiver = new AirPlayReceiver();
        receiver.Start("A", advertise: false);
        receiver.Start("B", advertise: false);
        Assert.Equal(ReceiverState.Waiting, receiver.State);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, receiver.ControlPort);
        var info = await Send(client.GetStream(), "GET", "/info");
        Assert.Equal("B", ((Dictionary<string, object?>)BinaryPlist.Read(info.body)!)["name"]);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(25);
        Assert.True(condition());
    }
}

public class AdvertisingTests
{
    private static readonly MdnsService[] Services =
    {
        new("AABBCCDDEEFF@My Studio 2.0", "_raop._tcp", 7000, new[] { ("ch", "2"), ("pk", "00ff") }),
        new("My Studio 2.0", "_airplay._tcp", 7000, new[] { ("deviceid", "AA:BB:CC:DD:EE:FF"), ("features", "0x5A7FFEE6,0x0") }),
    };

    [Fact]
    public void Announcement_is_valid_dns_that_an_independent_parser_understands()
    {
        var packet = MdnsPacket.BuildResponse(Services, "smaet-rec-ddeeff", System.Net.IPAddress.Parse("192.168.1.20"));
        var message = new Makaretu.Dns.Message();
        message.Read(packet, 0, packet.Length);
        Assert.True(message.QR && message.AA);
        var records = message.Answers;
        Assert.Equal(9, records.Count);

        var ptr = records.OfType<Makaretu.Dns.PTRRecord>().Single(r => r.Name == "_airplay._tcp.local");
        Assert.Equal(new[] { "My Studio 2.0", "_airplay", "_tcp", "local" }, ptr.DomainName.Labels);
        var srv = records.OfType<Makaretu.Dns.SRVRecord>().Single(r => r.Name.Labels[0] == "My Studio 2.0");
        Assert.Equal(7000, srv.Port);
        Assert.Equal("smaet-rec-ddeeff.local", srv.Target.ToString());
        var txt = records.OfType<Makaretu.Dns.TXTRecord>().Single(r => r.Name.Labels[0] == "My Studio 2.0");
        Assert.Contains("deviceid=AA:BB:CC:DD:EE:FF", txt.Strings);
        Assert.Contains("features=0x5A7FFEE6,0x0", txt.Strings);
        var a = records.OfType<Makaretu.Dns.ARecord>().Single();
        Assert.Equal("192.168.1.20", a.Address.ToString());
        Assert.Equal("smaet-rec-ddeeff.local", a.Name.ToString());
        Assert.Contains(records.OfType<Makaretu.Dns.PTRRecord>(), r => r.Name == "_services._dns-sd._udp.local" && r.DomainName == "_raop._tcp.local");
        Assert.All(records, r => Assert.True(r.TTL > TimeSpan.Zero));

        var goodbye = new Makaretu.Dns.Message();
        var bye = MdnsPacket.BuildResponse(Services, "smaet-rec-ddeeff", System.Net.IPAddress.Parse("192.168.1.20"), goodbye: true);
        goodbye.Read(bye, 0, bye.Length);
        Assert.All(goodbye.Answers, r => Assert.Equal(TimeSpan.Zero, r.TTL));
    }

    [Fact]
    public void Questions_from_a_real_query_are_read_including_compressed_names()
    {
        var query = new Makaretu.Dns.Message();
        query.Questions.Add(new Makaretu.Dns.Question { Name = "_airplay._tcp.local", Type = Makaretu.Dns.DnsType.PTR });
        query.Questions.Add(new Makaretu.Dns.Question { Name = "_raop._tcp.local", Type = Makaretu.Dns.DnsType.PTR });
        var questions = MdnsPacket.ReadQuestions(query.ToByteArray()); // the encoder compresses the repeated "_tcp.local"
        Assert.Equal(new[] { ("_airplay._tcp.local", MdnsPacket.TypePtr), ("_raop._tcp.local", MdnsPacket.TypePtr) }, questions);

        Assert.Empty(MdnsPacket.ReadQuestions(MdnsPacket.BuildResponse(Services, "h", System.Net.IPAddress.Loopback))); // responses are not questions
        Assert.Empty(MdnsPacket.ReadQuestions(new byte[] { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0xC0, 0x0C })); // pointer loop
        Assert.Empty(MdnsPacket.ReadQuestions(new byte[5]));
    }

    [Fact]
    public void Receiver_starts_with_network_announcement_and_stops_cleanly()
    {
        using var receiver = new AirPlayReceiver();
        var log = new List<string>();
        receiver.Log += log.Add;
        receiver.Start("My Studio 2.0");
        Assert.True(receiver.State == ReceiverState.Waiting, "receiver failed: " + receiver.LastError + string.Join("\n", log));
        Assert.Equal("My Studio 2.0", receiver.Name);
        receiver.Stop();
        Assert.Equal(ReceiverState.Stopped, receiver.State);
        Assert.Equal(0, receiver.AnnouncedAdapters);
    }
}
