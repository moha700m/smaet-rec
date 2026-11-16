using System.Text.Json;
using Xunit;

namespace SmaetRec.Tests;

public sealed class DirectUsbIpcTests
{
    [Fact]
    public void VideoEnvelope_UsesDecodedBgraV2Fields()
    {
        const string json = """{"type":"video","pixelFormat":"bgra8","width":1920,"height":1080,"stride":7680,"payloadBytes":8294400,"timestamp100ns":123456}""";
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("video", root.GetProperty("type").GetString());
        Assert.Equal("bgra8", root.GetProperty("pixelFormat").GetString());
        int width = root.GetProperty("width").GetInt32();
        int height = root.GetProperty("height").GetInt32();
        int stride = root.GetProperty("stride").GetInt32();
        int payloadBytes = root.GetProperty("payloadBytes").GetInt32();
        Assert.Equal(width * 4, stride);
        Assert.Equal(stride * height, payloadBytes);
        Assert.True(root.GetProperty("timestamp100ns").GetInt64() > 0);
    }

    [Theory]
    [InlineData(1, 1, 4)]
    [InlineData(1280, 720, 3686400)]
    [InlineData(1920, 1080, 8294400)]
    public void BgraPayloadSize_IsExact(int width, int height, int expectedBytes)
    {
        Assert.Equal(expectedBytes, checked(width * height * 4));
    }
}
