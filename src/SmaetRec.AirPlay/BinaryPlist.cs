using System.Buffers.Binary;
using System.Text;

namespace SmaetRec.AirPlay;

/// <summary>
/// Minimal Apple binary property list (bplist00) reader and writer.
/// Values map to: bool, long, double, string, byte[], List&lt;object?&gt;, Dictionary&lt;string, object?&gt;.
/// </summary>
public static class BinaryPlist
{
    public static object? Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 40 || !data[..8].SequenceEqual("bplist00"u8)) throw new FormatException("Not a binary plist");
        var trailer = data[^32..];
        int offsetSize = trailer[6], refSize = trailer[7];
        long count = BinaryPrimitives.ReadInt64BigEndian(trailer[8..]);
        long top = BinaryPrimitives.ReadInt64BigEndian(trailer[16..]);
        long tableOffset = BinaryPrimitives.ReadInt64BigEndian(trailer[24..]);
        if (count <= 0 || count > 1_000_000 || tableOffset < 8 || tableOffset + count * offsetSize > data.Length - 32)
            throw new FormatException("Corrupt binary plist");
        var offsets = new long[count];
        for (int i = 0; i < count; i++)
            offsets[i] = ReadUInt(data.Slice((int)tableOffset + i * offsetSize, offsetSize));
        return ReadObject(data, offsets, refSize, top, 0);
    }

    private static long ReadUInt(ReadOnlySpan<byte> s)
    {
        long v = 0;
        foreach (var b in s) v = (v << 8) | b;
        return v;
    }

    private static object? ReadObject(ReadOnlySpan<byte> data, long[] offsets, int refSize, long index, int depth)
    {
        if (depth > 64 || index < 0 || index >= offsets.Length) throw new FormatException("Corrupt binary plist");
        int pos = checked((int)offsets[index]);
        byte marker = data[pos++];
        int type = marker >> 4, info = marker & 0x0F;
        switch (type)
        {
            case 0x0:
                return info switch { 0x8 => false, 0x9 => true, _ => null };
            case 0x1:
            {
                int len = 1 << info;
                var s = data.Slice(pos, len);
                return len == 8 ? BinaryPrimitives.ReadInt64BigEndian(s) : len == 16 ? BinaryPrimitives.ReadInt64BigEndian(s[8..]) : ReadUInt(s);
            }
            case 0x2:
                return info == 2 ? BinaryPrimitives.ReadSingleBigEndian(data.Slice(pos, 4)) : BinaryPrimitives.ReadDoubleBigEndian(data.Slice(pos, 8));
            case 0x3:
                return BinaryPrimitives.ReadDoubleBigEndian(data.Slice(pos, 8));
            case 0x8:
                return ReadUInt(data.Slice(pos, info + 1));
        }
        int length = info;
        if (info == 0xF)
        {
            int intLen = 1 << (data[pos++] & 0x0F);
            length = checked((int)ReadUInt(data.Slice(pos, intLen)));
            pos += intLen;
        }
        switch (type)
        {
            case 0x4: return data.Slice(pos, length).ToArray();
            case 0x5: return Encoding.ASCII.GetString(data.Slice(pos, length));
            case 0x6: return Encoding.BigEndianUnicode.GetString(data.Slice(pos, length * 2));
            case 0xA:
            {
                var list = new List<object?>(length);
                for (int i = 0; i < length; i++)
                    list.Add(ReadObject(data, offsets, refSize, ReadUInt(data.Slice(pos + i * refSize, refSize)), depth + 1));
                return list;
            }
            case 0xD:
            {
                var dict = new Dictionary<string, object?>(length);
                for (int i = 0; i < length; i++)
                {
                    var key = ReadObject(data, offsets, refSize, ReadUInt(data.Slice(pos + i * refSize, refSize)), depth + 1);
                    var value = ReadObject(data, offsets, refSize, ReadUInt(data.Slice(pos + (length + i) * refSize, refSize)), depth + 1);
                    dict[key?.ToString() ?? ""] = value;
                }
                return dict;
            }
            default: throw new FormatException($"Unsupported plist object 0x{marker:X2}");
        }
    }

    public static byte[] Write(object root)
    {
        var objects = new List<object?>();
        var refs = new Dictionary<int, int[]>();
        Flatten(root, objects, refs);
        int refSize = objects.Count < 256 ? 1 : 2;
        using var ms = new MemoryStream();
        ms.Write("bplist00"u8);
        var offsets = new long[objects.Count];
        for (int i = 0; i < objects.Count; i++)
        {
            offsets[i] = ms.Position;
            WriteObject(ms, objects[i], refs.GetValueOrDefault(i), refSize);
        }
        long tableOffset = ms.Position;
        int offsetSize = tableOffset < 256 ? 1 : tableOffset < 65536 ? 2 : 4;
        foreach (var o in offsets) WriteBE(ms, o, offsetSize);
        Span<byte> trailer = stackalloc byte[32];
        trailer[6] = (byte)offsetSize; trailer[7] = (byte)refSize;
        BinaryPrimitives.WriteInt64BigEndian(trailer[8..], objects.Count);
        BinaryPrimitives.WriteInt64BigEndian(trailer[16..], 0);
        BinaryPrimitives.WriteInt64BigEndian(trailer[24..], tableOffset);
        ms.Write(trailer);
        return ms.ToArray();
    }

    private static int Flatten(object? value, List<object?> objects, Dictionary<int, int[]> refs)
    {
        int index = objects.Count;
        objects.Add(value);
        if (value is System.Collections.IDictionary dict)
        {
            var keys = new List<int>(); var values = new List<int>();
            foreach (System.Collections.DictionaryEntry e in dict) keys.Add(Flatten(e.Key.ToString(), objects, refs));
            foreach (System.Collections.DictionaryEntry e in dict) values.Add(Flatten(e.Value, objects, refs));
            refs[index] = keys.Concat(values).ToArray();
        }
        else if (value is System.Collections.IList list && value is not byte[])
        {
            var items = new int[list.Count];
            for (int i = 0; i < list.Count; i++) items[i] = Flatten(list[i], objects, refs);
            refs[index] = items;
        }
        return index;
    }

    private static void WriteBE(Stream s, long value, int size)
    {
        for (int i = size - 1; i >= 0; i--) s.WriteByte((byte)(value >> (8 * i)));
    }

    private static void WriteHeader(Stream s, int type, int length)
    {
        if (length < 15) { s.WriteByte((byte)((type << 4) | length)); return; }
        s.WriteByte((byte)((type << 4) | 0xF));
        WriteInt(s, length);
    }

    private static void WriteInt(Stream s, long v)
    {
        if (v < 0) { s.WriteByte(0x13); WriteBE(s, v, 8); }
        else if (v <= 0xFF) { s.WriteByte(0x10); WriteBE(s, v, 1); }
        else if (v <= 0xFFFF) { s.WriteByte(0x11); WriteBE(s, v, 2); }
        else if (v <= 0xFFFFFFFFL) { s.WriteByte(0x12); WriteBE(s, v, 4); }
        else { s.WriteByte(0x13); WriteBE(s, v, 8); }
    }

    private static void WriteObject(Stream s, object? value, int[]? refs, int refSize)
    {
        switch (value)
        {
            case null: s.WriteByte(0x00); break;
            case bool b: s.WriteByte(b ? (byte)0x09 : (byte)0x08); break;
            case byte or sbyte or short or ushort or int or uint or long: WriteInt(s, Convert.ToInt64(value)); break;
            case ulong ul: WriteInt(s, unchecked((long)ul)); break;
            case float or double:
            {
                s.WriteByte(0x23);
                Span<byte> buf = stackalloc byte[8];
                BinaryPrimitives.WriteDoubleBigEndian(buf, Convert.ToDouble(value));
                s.Write(buf);
                break;
            }
            case byte[] data: WriteHeader(s, 0x4, data.Length); s.Write(data); break;
            case string str:
                if (str.All(c => c < 128)) { WriteHeader(s, 0x5, str.Length); s.Write(Encoding.ASCII.GetBytes(str)); }
                else { WriteHeader(s, 0x6, str.Length); s.Write(Encoding.BigEndianUnicode.GetBytes(str)); }
                break;
            case System.Collections.IDictionary:
                WriteHeader(s, 0xD, refs!.Length / 2);
                foreach (var r in refs) WriteBE(s, r, refSize);
                break;
            case System.Collections.IList:
                WriteHeader(s, 0xA, refs!.Length);
                foreach (var r in refs) WriteBE(s, r, refSize);
                break;
            default: throw new NotSupportedException($"Cannot write {value.GetType()} to a plist");
        }
    }
}
