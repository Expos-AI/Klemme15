// Klemme15: open-source BMW diagnostics
// Copyright (C) 2026 Kindric Castro
//
// This program is free software: you can redistribute it and/or modify it under the terms of the GNU General
// Public License as published by the Free Software Foundation, either version 3 of the License, or (at your
// option) any later version. This program is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
// General Public License for more details. You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Klemme15.Core;

namespace Klemme15.Protocols.Recording;

/// <summary>What a record in a session recording holds.</summary>
public enum LogKind : byte { Tx = 0, Rx = 1, Line = 2, Note = 3, End = 255 }

/// <summary>What a recording says about itself. No VIN: that is only inside the recorded replies.</summary>
public sealed record K15LogInfo(string App, string Vehicle, string? Model, string Cable, DateTimeOffset Started);

/// <summary>One request Klemme15 sent, and what came back: each piece with its delay after the request, in microseconds.</summary>
public sealed record Exchange(byte[] Request, IReadOnlyList<(long DelayMicros, byte[] Data)> Replies, long AtMicros)
{
    /// <summary>For a request nothing answered: how long Klemme15 listened before it gave up. 0 when not recorded.</summary>
    public long SilenceMicros { get; init; }
}

/// <summary>
/// A lock-free byte queue for one writer and one reader. The bus thread writes whole records and never waits; a
/// background task reads them out. The size must be a power of two.
/// </summary>
public sealed class SpscByteRing
{
    private readonly byte[] _buf;
    private readonly int _mask;
    private long _head;   // moved by the writer only
    private long _tail;   // moved by the reader only

    public SpscByteRing(int capacity)
    {
        if (capacity < 16 || (capacity & (capacity - 1)) != 0) throw new ArgumentException("The size must be a power of two.", nameof(capacity));
        _buf = new byte[capacity];
        _mask = capacity - 1;
    }

    public int Capacity => _buf.Length;

    /// <summary>Writes both parts as one piece, or nothing when there isn't room for all of it.</summary>
    public bool TryWrite(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var head = _head;
        var free = _buf.Length - (int)(head - Volatile.Read(ref _tail));
        if (a.Length + b.Length > free) return false;
        Copy(a, head);
        Copy(b, head + a.Length);
        Volatile.Write(ref _head, head + a.Length + b.Length);
        return true;
    }

    public int Read(Span<byte> to)
    {
        var tail = _tail;
        var n = (int)Math.Min(to.Length, Volatile.Read(ref _head) - tail);
        if (n <= 0) return 0;
        var i = (int)(tail & _mask);
        var first = Math.Min(n, _buf.Length - i);
        _buf.AsSpan(i, first).CopyTo(to);
        _buf.AsSpan(0, n - first).CopyTo(to[first..]);
        Volatile.Write(ref _tail, tail + n);
        return n;
    }

    private void Copy(ReadOnlySpan<byte> from, long position)
    {
        var i = (int)(position & _mask);
        var first = Math.Min(from.Length, _buf.Length - i);
        from[..first].CopyTo(_buf.AsSpan(i));
        from[first..].CopyTo(_buf);
    }
}

/// <summary>
/// Writes a session recording (.k15log): everything Klemme15 sends and everything the car answers, with the time of
/// each piece. The bus thread only copies bytes into a queue; compressing and writing happen on another thread, so
/// recording never changes the timing on the wire.
/// File: "K15L", version, length and JSON of <see cref="K15LogInfo"/>, then one Brotli stream of records:
/// microseconds since the record before, the kind, the length, the bytes (numbers as 7-bit varints).
/// </summary>
public sealed class K15LogRecorder : IDisposable
{
    public const byte Version = 1;
    public static ReadOnlySpan<byte> Magic => "K15L"u8;

    private readonly SpscByteRing _ring;
    private readonly long _start = Stopwatch.GetTimestamp();
    private readonly Thread _writer;
    private volatile bool _stop;
    private long _last;
    private int _dropped;
    private bool _disposed;

    public K15LogRecorder(Stream file, K15LogInfo info, int queueBytes = 1 << 18)
    {
        _ring = new SpscByteRing(queueBytes);
        file.Write(Magic);
        file.WriteByte(Version);
        var json = InfoJson(info);
        Span<byte> length = stackalloc byte[10];
        file.Write(length[..K15Log.WriteVarint(length, (ulong)json.Length)]);
        file.Write(json);
        file.Flush();
        _writer = new Thread(() => Pump(file)) { IsBackground = true, Name = "Klemme15 recorder" };
        _writer.Start();
    }

    /// <summary>Records that didn't fit in the queue (the disk fell far behind). They are missing from the file.</summary>
    public int Dropped => Volatile.Read(ref _dropped);

    /// <summary>Adds one record. Called from the bus thread: copies the bytes and returns, never waits.</summary>
    public void Add(LogKind kind, ReadOnlySpan<byte> data)
    {
        if (_stop) return;
        var now = (long)Stopwatch.GetElapsedTime(_start).TotalMicroseconds;
        Span<byte> head = stackalloc byte[21];
        var n = K15Log.WriteVarint(head, (ulong)Math.Max(0, now - _last));
        head[n++] = (byte)kind;
        n += K15Log.WriteVarint(head[n..], (ulong)data.Length);
        if (n + data.Length <= _ring.Capacity && _ring.TryWrite(head[..n], data)) _last = now;
        else Interlocked.Increment(ref _dropped);
    }

    private void Pump(Stream file)
    {
        try
        {
            using var brotli = new BrotliStream(file, CompressionLevel.Optimal, leaveOpen: false);
            var chunk = new byte[16384];
            var unflushed = false;
            var lastFlush = Stopwatch.GetTimestamp();
            while (true)
            {
                var n = _ring.Read(chunk);
                if (n > 0) { brotli.Write(chunk, 0, n); unflushed = true; continue; }
                if (_stop) break;
                // Flushed about once a second, so a recording survives the app or the laptop going down mid-drive.
                if (unflushed && Stopwatch.GetElapsedTime(lastFlush) > TimeSpan.FromSeconds(1))
                {
                    brotli.Flush();
                    unflushed = false;
                    lastFlush = Stopwatch.GetTimestamp();
                }
                Thread.Sleep(15);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or UnauthorizedAccessException)
        {
            _stop = true;   // the disk went away: stop recording, keep the session
        }
    }

    private static byte[] InfoJson(K15LogInfo info)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("app", info.App);
            w.WriteString("vehicle", info.Vehicle);
            if (info.Model is not null) w.WriteString("model", info.Model);
            w.WriteString("cable", info.Cable);
            w.WriteString("started", info.Started);
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // The last record says the recording was closed properly; a file without it was cut off.
        for (var tries = 0; tries < 50 && !_stop; tries++)
        {
            var before = Dropped;
            Add(LogKind.End, []);
            if (Dropped == before) break;
            Thread.Sleep(10);
        }
        _stop = true;
        _writer.Join(TimeSpan.FromSeconds(5));
    }
}

/// <summary>A session recording read back from a file.</summary>
public sealed class K15Log
{
    private K15Log(K15LogInfo info, IReadOnlyList<Exchange> exchanges, long micros, bool complete)
    {
        Info = info;
        Exchanges = exchanges;
        Duration = TimeSpan.FromMicroseconds(micros);
        Complete = complete;
    }

    public K15LogInfo Info { get; }
    public IReadOnlyList<Exchange> Exchanges { get; }
    public TimeSpan Duration { get; }

    /// <summary>False when the file stops before its closing record (the app or laptop went down while recording). What was read is still usable.</summary>
    public bool Complete { get; }

    public const int MaxRecordBytes = 1 << 20;

    /// <summary>The most a recording may unpack to. An hour of continuous traffic is a few tens of megabytes.</summary>
    public const long MaxTotalBytes = 256L << 20;

    public static K15Log Read(byte[] file) => Read(new MemoryStream(file, writable: false));

    public static K15Log Read(Stream file)
    {
        Span<byte> magic = stackalloc byte[5];
        if (file.ReadAtLeast(magic, 5, throwOnEndOfStream: false) < 5 || !magic[..4].SequenceEqual(K15LogRecorder.Magic))
            throw new DiagnosticException("That file isn't a Klemme15 recording (.k15log).");
        if (magic[4] != K15LogRecorder.Version)
            throw new DiagnosticException("That recording was made by a newer Klemme15. Update Klemme15 to play it.");
        if (!TryReadVarint(file, out var infoLength) || infoLength > 4096) throw new DiagnosticException("That recording's header is damaged.");
        var json = new byte[(int)infoLength];
        if (file.ReadAtLeast(json, json.Length, throwOnEndOfStream: false) < json.Length) throw new DiagnosticException("That recording's header is damaged.");
        var info = ParseInfo(json);

        var exchanges = new List<Exchange>();
        var request = new List<byte>();
        var replies = new List<(long, byte[])>();
        long now = 0, requestAt = 0, silence = 0, total = 0;
        var lastKind = LogKind.Note;
        var open = false;
        var complete = false;
        var damaged = false;

        void Close()
        {
            if (open) exchanges.Add(new Exchange(request.ToArray(), replies.ToArray(), requestAt) { SilenceMicros = replies.Count == 0 ? silence : 0 });
            silence = 0;
            request.Clear();
            replies.Clear();
            open = false;
        }

        try
        {
            using var brotli = new BrotliStream(file, CompressionMode.Decompress);
            using var buffered = new BufferedStream(brotli, 16384);
            while (TryReadVarint(buffered, out var delta))
            {
                var kind = buffered.ReadByte();
                if (kind < 0 || !TryReadVarint(buffered, out var length) || length > MaxRecordBytes) { damaged = true; break; }
                var data = new byte[(int)length];
                if (buffered.ReadAtLeast(data, data.Length, throwOnEndOfStream: false) < data.Length) { damaged = true; break; }
                if ((LogKind)kind == LogKind.End) { complete = true; break; }
                now += (long)delta;
                // A file built to unpack into far more than any session holds is cut off here, not loaded into memory.
                if ((total += data.Length + 8) > MaxTotalBytes) { damaged = true; break; }
                switch ((LogKind)kind)
                {
                    case LogKind.Tx:
                        // Bytes sent back to back are one request; a request after replies starts the next exchange.
                        if (lastKind != LogKind.Tx) { Close(); requestAt = now; open = true; }
                        request.AddRange(data);
                        requestAt = now;
                        lastKind = LogKind.Tx;
                        break;
                    case LogKind.Rx:
                        if (open && data.Length > 0) replies.Add((now - requestAt, data));
                        else if (open && lastKind == LogKind.Tx) silence = now - requestAt;   // empty: the app listened and nothing came
                        lastKind = LogKind.Rx;
                        break;
                }
            }
        }
        catch (Exception e) when (e is InvalidDataException or IOException or EndOfStreamException)
        {
            damaged = true;   // cut off mid-stream: keep what was read
        }
        Close();
        return new K15Log(info, exchanges, now, complete && !damaged);
    }

    private static K15LogInfo ParseInfo(byte[] json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string Text(string name) => r.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
            var model = Text("model");
            return new K15LogInfo(Text("app"), Text("vehicle"), model.Length > 0 ? model : null, Text("cable"),
                r.TryGetProperty("started", out var s) && s.TryGetDateTimeOffset(out var t) ? t : default);
        }
        catch (JsonException) { throw new DiagnosticException("That recording's header is damaged."); }
    }

    public static int WriteVarint(Span<byte> to, ulong value)
    {
        var i = 0;
        while (value >= 0x80) { to[i++] = (byte)(value | 0x80); value >>= 7; }
        to[i++] = (byte)value;
        return i;
    }

    private static bool TryReadVarint(Stream from, out ulong value)
    {
        value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            var b = from.ReadByte();
            if (b < 0) return false;
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
        }
        return false;
    }

    /// <summary>One line per exchange, for reading a recording without a car or the app.</summary>
    public string ToText()
    {
        var text = new StringBuilder();
        foreach (var e in Exchanges)
        {
            text.Append(TimeSpan.FromMicroseconds(e.AtMicros).ToString(@"hh\:mm\:ss\.fff")).Append(" TX ").Append(Convert.ToHexString(e.Request));
            text.Append(" RX ").AppendLine(e.Replies.Count == 0 ? "(no reply)" : Convert.ToHexString(e.Replies.SelectMany(r => r.Data).ToArray()));
        }
        return text.ToString();
    }
}
