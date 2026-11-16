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
using System.Runtime.InteropServices;
using Klemme15.Protocols.Transport;

namespace Klemme15.Protocols.Recording;

/// <summary>Passes everything through to the cable and copies it into a session recording.</summary>
public sealed class RecordingTransport(ITransport inner, K15LogRecorder recorder) : ITransport
{
    public string Name => inner.Name;
    public bool IsOpen => inner.IsOpen;
    public bool? IgnitionOn => inner.IgnitionOn;
    public void Open() => inner.Open();
    public void Close() => inner.Close();
    public void SetBreak(bool on) => inner.SetBreak(on);
    public void SetDtr(bool on) => inner.SetDtr(on);
    public void SetRts(bool on) => inner.SetRts(on);
    public void DiscardInput() => inner.DiscardInput();

    public void Configure(int baudRate, SerialParity parity)
    {
        inner.Configure(baudRate, parity);
        Span<byte> line = stackalloc byte[5];
        BitConverter.TryWriteBytes(line, baudRate);
        line[4] = (byte)parity;
        recorder.Add(LogKind.Line, line);
    }

    private bool _listening;

    public void Write(ReadOnlySpan<byte> data)
    {
        recorder.Add(LogKind.Tx, data);
        _listening = true;
        inner.Write(data);
    }

    public int Read(Span<byte> buffer, TimeSpan timeout)
    {
        var n = inner.Read(buffer, timeout);
        if (n > 0) recorder.Add(LogKind.Rx, buffer[..n]);
        // Silence after a request is recorded too (an empty reply), so playback knows that request ended there
        // and doesn't run it together with the next one.
        else if (_listening) recorder.Add(LogKind.Rx, []);
        _listening = false;
        return n;
    }

    public void Dispose()
    {
        inner.Dispose();
        recorder.Dispose();
    }
}

/// <summary>
/// Plays a session recording back as if it were the cable. Klemme15 asks, and this answers with what the car
/// answered when the recording was made, after the same delay. A request is matched to the next identical one in
/// the recording, so values that were read again and again move forward the way they did on the drive. A request
/// the recording never made gets no reply, the same as a module that isn't there. Nothing is sent anywhere.
/// </summary>
public sealed class ReplayTransport(K15Log log, double speed = 1) : ITransport
{
    private readonly List<byte> _pending = [];
    private readonly Queue<(long Due, byte[] Data)> _replies = new();
    private byte[] _rest = [];
    private int _cursor;
    private long _silentUntil;

    public string Name => "Recording";
    public bool IsOpen { get; private set; }
    public bool? IgnitionOn => true;

    /// <summary>How far into the recording the last matched request was.</summary>
    public TimeSpan Position { get; private set; }

    /// <summary>Requests the recording had no answer for.</summary>
    public int Unmatched { get; private set; }

    public void Open() => IsOpen = true;
    public void Close() => IsOpen = false;
    public void Configure(int baudRate, SerialParity parity) { }
    public void SetBreak(bool on) { }
    public void SetDtr(bool on) { }
    public void SetRts(bool on) { }
    public void Dispose() => Close();

    public void DiscardInput()
    {
        _replies.Clear();
        _rest = [];
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        _pending.AddRange(data);
        var wanted = CollectionsMarshal.AsSpan(_pending);
        var count = log.Exchanges.Count;
        // First an exact match. Then, when bytes that never matched are still waiting in front (a keep-alive the
        // recording doesn't have, say), a recorded request that the waiting bytes end with.
        for (var pass = 0; pass < 2; pass++)
        for (var k = 0; k < count; k++)
        {
            var i = (_cursor + k) % count;
            var exchange = log.Exchanges[i];
            if (pass == 0 ? !wanted.SequenceEqual(exchange.Request)
                : exchange.Request.Length < 4 || exchange.Request.Length >= wanted.Length || !wanted.EndsWith(exchange.Request)) continue;
            var now = Stopwatch.GetTimestamp();
            _replies.Clear();
            _rest = [];
            foreach (var (delay, bytes) in exchange.Replies)
                _replies.Enqueue((now + (long)(delay / Math.Max(speed, 0.01) * Stopwatch.Frequency / 1_000_000), bytes));
            _silentUntil = exchange.Replies.Count == 0 && exchange.SilenceMicros > 0
                ? now + (long)(exchange.SilenceMicros / Math.Max(speed, 0.01) * Stopwatch.Frequency / 1_000_000) : 0;
            _cursor = i + 1;
            Position = TimeSpan.FromMicroseconds(exchange.AtMicros);
            _pending.Clear();
            return;
        }
        // No match: the first part of a request sent in pieces, or one the recording never made. Read() tells them apart.
    }

    public int Read(Span<byte> buffer, TimeSpan timeout)
    {
        if (buffer.IsEmpty) return 0;
        if (_rest.Length == 0)
        {
            if (!_replies.TryPeek(out var next))
            {
                if (_pending.Count > 0) { Unmatched++; _pending.Clear(); }
                // A request nobody answered: stay silent as long as Klemme15 listened when the recording was made.
                var silence = _silentUntil != 0 ? Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), _silentUntil) : timeout / Math.Max(speed, 0.01);
                _silentUntil = 0;
                Wait(silence < timeout ? silence : timeout);
                return 0;
            }
            var wait = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), next.Due);
            if (wait > timeout) { Wait(timeout); return 0; }
            Wait(wait);
            _replies.Dequeue();
            _rest = next.Data;
        }
        var n = Math.Min(buffer.Length, _rest.Length);
        _rest.AsSpan(0, n).CopyTo(buffer);
        _rest = n == _rest.Length ? [] : _rest[n..];
        return n;
    }

    private static void Wait(TimeSpan time)
    {
        if (time > TimeSpan.Zero) Thread.Sleep(time);
    }
}
