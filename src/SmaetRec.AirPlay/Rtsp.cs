using System.Net.Sockets;
using System.Text;

namespace SmaetRec.AirPlay;

public sealed class RtspRequest
{
    public string Method = "";
    public string Path = "";
    public string Protocol = "RTSP/1.0";
    public Dictionary<string, string> Headers = new(StringComparer.OrdinalIgnoreCase);
    public byte[] Body = Array.Empty<byte>();
}

public sealed class RtspResponse
{
    public int Status = 200;
    public string Reason = "OK";
    public Dictionary<string, string> Headers = new(StringComparer.OrdinalIgnoreCase);
    public byte[] Body = Array.Empty<byte>();

    public void SetBody(byte[] body, string contentType) { Body = body; Headers["Content-Type"] = contentType; }

    public byte[] ToBytes(RtspRequest request)
    {
        var sb = new StringBuilder();
        sb.Append(request.Protocol).Append(' ').Append(Status).Append(' ').Append(Reason).Append("\r\n");
        if (request.Headers.TryGetValue("CSeq", out var cseq)) sb.Append("CSeq: ").Append(cseq).Append("\r\n");
        sb.Append("Server: AirTunes/220.68\r\n");
        foreach (var h in Headers) sb.Append(h.Key).Append(": ").Append(h.Value).Append("\r\n");
        sb.Append("Content-Length: ").Append(Body.Length).Append("\r\n\r\n");
        var head = Encoding.ASCII.GetBytes(sb.ToString());
        var all = new byte[head.Length + Body.Length];
        head.CopyTo(all, 0);
        Body.CopyTo(all, head.Length);
        return all;
    }
}

/// <summary>Reads RTSP/HTTP style requests from a stream, one at a time.</summary>
public sealed class RtspReader
{
    private const int MaxHeader = 64 * 1024, MaxBody = 8 * 1024 * 1024;
    private readonly Stream _stream;
    private byte[] _buffer = new byte[8192];
    private int _count;

    public RtspReader(Stream stream) => _stream = stream;

    /// <summary>Returns null when the other side closed the connection.</summary>
    public async Task<RtspRequest?> ReadAsync(CancellationToken ct)
    {
        int headerEnd;
        while ((headerEnd = FindHeaderEnd()) < 0)
        {
            if (_count >= MaxHeader) throw new InvalidDataException("Request header too large");
            if (!await FillAsync(ct).ConfigureAwait(false)) return null;
        }
        var lines = Encoding.ASCII.GetString(_buffer, 0, headerEnd).Split("\r\n");
        var first = lines[0].Split(' ', 3);
        if (first.Length < 3) throw new InvalidDataException("Bad request line");
        var req = new RtspRequest { Method = first[0], Path = first[1], Protocol = first[2].Trim() };
        foreach (var line in lines.Skip(1))
        {
            int colon = line.IndexOf(':');
            if (colon > 0) req.Headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        int bodyLength = 0;
        if (req.Headers.TryGetValue("Content-Length", out var cl) && (!int.TryParse(cl, out bodyLength) || bodyLength < 0 || bodyLength > MaxBody))
            throw new InvalidDataException("Bad Content-Length");
        int total = headerEnd + 4 + bodyLength;
        while (_count < total)
            if (!await FillAsync(ct, total).ConfigureAwait(false)) return null;
        req.Body = _buffer.AsSpan(headerEnd + 4, bodyLength).ToArray();
        Buffer.BlockCopy(_buffer, total, _buffer, 0, _count - total);
        _count -= total;
        return req;
    }

    private int FindHeaderEnd()
    {
        for (int i = 0; i + 3 < _count; i++)
            if (_buffer[i] == '\r' && _buffer[i + 1] == '\n' && _buffer[i + 2] == '\r' && _buffer[i + 3] == '\n') return i;
        return -1;
    }

    private async Task<bool> FillAsync(CancellationToken ct, int needed = 0)
    {
        if (_count == _buffer.Length || needed > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, needed));
        int n = await _stream.ReadAsync(_buffer.AsMemory(_count), ct).ConfigureAwait(false);
        if (n <= 0) return false;
        _count += n;
        return true;
    }
}

internal static class StreamExtensions
{
    /// <summary>Fills the buffer completely. Returns false if the stream ended first.</summary>
    public static async Task<bool> ReadFullyAsync(this NetworkStream stream, byte[] buffer, int count, CancellationToken ct)
    {
        int read = 0;
        while (read < count)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read, count - read), ct).ConfigureAwait(false);
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }
}
