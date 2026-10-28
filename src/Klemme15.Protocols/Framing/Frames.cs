namespace Klemme15.Protocols.Framing;

public static class Checksum
{
    public static byte Sum(ReadOnlySpan<byte> data)
    {
        var s = 0;
        foreach (var b in data) s += b;
        return (byte)s;
    }

    public static byte Xor(ReadOnlySpan<byte> data)
    {
        byte x = 0;
        foreach (var b in data) x ^= b;
        return x;
    }
}

/// <summary>
/// KWP2000 / BMW-FAST frame: [fmt|len] [target] [source] ([len]) data... [sum].
/// Used on D-CAN through the K+DCAN cable, and for OBD-II over ISO 14230 on the K-line.
/// </summary>
public sealed record KwpFrame(byte Target, byte Source, byte[] Data)
{
    public const int MaxData = 255;

    public static byte[] Build(byte target, byte source, ReadOnlySpan<byte> data, byte formatBase = 0x80)
    {
        if (data.Length is 0 or > MaxData) throw new ArgumentOutOfRangeException(nameof(data), "KWP frames carry 1-255 data bytes");
        var shortForm = data.Length <= 0x3F;
        var header = shortForm ? 3 : 4;
        var frame = new byte[header + data.Length + 1];
        frame[0] = (byte)(shortForm ? formatBase | data.Length : formatBase);
        frame[1] = target;
        frame[2] = source;
        if (!shortForm) frame[3] = (byte)data.Length;
        data.CopyTo(frame.AsSpan(header));
        frame[^1] = Checksum.Sum(frame.AsSpan(0, frame.Length - 1));
        return frame;
    }

    /// <summary>Parse every valid frame in a buffer, skipping garbage and echoes that do not checksum.</summary>
    public static List<KwpFrame> ParseAll(ReadOnlySpan<byte> buf)
    {
        var frames = new List<KwpFrame>();
        var i = 0;
        while (i < buf.Length)
        {
            if (TryParseAt(buf, i, out var frame, out var length))
            {
                frames.Add(frame);
                i += length;
            }
            else i++;
        }
        return frames;
    }

    private static bool TryParseAt(ReadOnlySpan<byte> buf, int i, out KwpFrame frame, out int length)
    {
        frame = null!;
        length = 0;
        var fmt = buf[i];
        if ((fmt & 0x80) == 0 || i + 3 >= buf.Length) return false;
        int dataLen = fmt & 0x3F, header = 3;
        if (dataLen == 0)
        {
            dataLen = buf[i + 3];
            header = 4;
            if (dataLen == 0) return false;
        }
        length = header + dataLen + 1;
        if (i + length > buf.Length) return false;
        var span = buf.Slice(i, length);
        if (Checksum.Sum(span[..^1]) != span[^1]) return false;
        frame = new KwpFrame(span[1], span[2], span.Slice(header, dataLen).ToArray());
        return true;
    }
}

/// <summary>
/// BMW DS2 frame on the K-line: [address] [total length] data... [xor].
/// Responses carry a status byte first: A0 = OK, A1 = busy, A2 = rejected, FF = error.
/// </summary>
public sealed record Ds2Frame(byte Address, byte[] Data)
{
    public const byte StatusOk = 0xA0;
    public const byte StatusBusy = 0xA1;
    public const byte StatusRejected = 0xA2;
    public const byte StatusError = 0xFF;

    public static byte[] Build(byte address, ReadOnlySpan<byte> data)
    {
        if (data.Length > 252) throw new ArgumentOutOfRangeException(nameof(data));
        var frame = new byte[data.Length + 3];
        frame[0] = address;
        frame[1] = (byte)frame.Length;
        data.CopyTo(frame.AsSpan(2));
        frame[^1] = Checksum.Xor(frame.AsSpan(0, frame.Length - 1));
        return frame;
    }

    public static List<Ds2Frame> ParseAll(ReadOnlySpan<byte> buf)
    {
        var frames = new List<Ds2Frame>();
        var i = 0;
        while (i + 3 <= buf.Length)
        {
            var len = buf[i + 1];
            if (len >= 3 && i + len <= buf.Length && Checksum.Xor(buf.Slice(i, len - 1)) == buf[i + len - 1])
            {
                frames.Add(new Ds2Frame(buf[i], buf.Slice(i + 2, len - 3).ToArray()));
                i += len;
            }
            else i++;
        }
        return frames;
    }
}

/// <summary>ISO 9141-2 OBD frame: 68 6A F1 data... [sum] (request), 48 6B src data... [sum] (response). No length byte.</summary>
public static class Iso9141Frame
{
    public static byte[] BuildRequest(ReadOnlySpan<byte> data)
    {
        var frame = new byte[data.Length + 4];
        frame[0] = 0x68; frame[1] = 0x6A; frame[2] = 0xF1;
        data.CopyTo(frame.AsSpan(3));
        frame[^1] = Checksum.Sum(frame.AsSpan(0, frame.Length - 1));
        return frame;
    }

    /// <summary>Split a buffer of back-to-back responses using header + checksum.</summary>
    public static List<byte[]> ParseResponses(ReadOnlySpan<byte> buf)
    {
        var result = new List<byte[]>();
        var i = 0;
        while (i < buf.Length - 3)
        {
            if (buf[i] != 0x48 || buf[i + 1] != 0x6B) { i++; continue; }
            var found = false;
            for (var end = i + 4; end < buf.Length && end - i <= 11; end++)
            {
                var nextIsHeader = end + 1 == buf.Length || buf[end + 1] == 0x48;
                if (nextIsHeader && Checksum.Sum(buf[i..end]) == buf[end])
                {
                    result.Add(buf[(i + 3)..end].ToArray());
                    i = end + 1;
                    found = true;
                    break;
                }
            }
            if (!found) i++;
        }
        return result;
    }
}

public static class Echo
{
    /// <summary>The K-line is half duplex: the cable hears its own transmission. Remove it if present.</summary>
    public static ReadOnlySpan<byte> Strip(ReadOnlySpan<byte> sent, ReadOnlySpan<byte> received) =>
        received.StartsWith(sent) ? received[sent.Length..] : received;
}
