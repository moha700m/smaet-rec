using System.Buffers.Binary;
using System.Text.Json;
using Xunit;

namespace SmaetRec.Tests;

public sealed class DirectUsbIpcTests
{
    [Fact]
    public void LengthPrefix_IsLittleEndian()
    {
        Span<byte> prefix = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, 0x123456);
        Assert.Equal(new byte[] { 0x56, 0x34, 0x12, 0x00 }, prefix.ToArray());
        Assert.Equal(0x123456, BinaryPrimitives.ReadInt32LittleEndian(prefix));
    }

    [Fact]
    public void VideoEnvelope_DeclaresPayloadAndTiming()
    {
        using var doc = JsonDocument.Parse("""{"type":"video","headerBytes":97,"payloadBytes":128,"timestampMicros":1234,"keyFrame":true}""");
        var root = doc.RootElement;
        Assert.Equal("video", root.GetProperty("type").GetString());
        Assert.True(root.GetProperty("headerBytes").GetInt32() > 0);
        Assert.True(root.GetProperty("payloadBytes").GetInt32() > 0);
        Assert.True(root.GetProperty("timestampMicros").GetInt64() > 0);
        Assert.True(root.GetProperty("keyFrame").GetBoolean());
    }
}