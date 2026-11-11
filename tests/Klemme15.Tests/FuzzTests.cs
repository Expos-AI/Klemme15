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

using Klemme15.Core;
using Klemme15.Data;
using Klemme15.Protocols;
using Klemme15.Protocols.Channels;
using Klemme15.Protocols.Framing;
using Klemme15.Protocols.Transport;
using Xunit;

namespace Klemme15.Tests;

/// <summary>
/// Hostile input. A damaged cable, a noisy K-line or a faulty module can send anything; Klemme15 must answer with a clear
/// message or "no answer", never crash, hang or read past a buffer. The random numbers are seeded, so a failure repeats.
/// </summary>
public class FuzzTests
{
    private static readonly VehicleDatabase Db = VehicleDatabase.Load(Path.Combine(Path.GetTempPath(), "klemme15-no-overrides"));

    private static byte[] Bytes(Random r, int max)
    {
        var b = new byte[r.Next(0, max + 1)];
        r.NextBytes(b);
        return b;
    }

    [Fact]
    public void FrameParsersSurviveRandomBytes()
    {
        var r = new Random(15);
        for (var i = 0; i < 20_000; i++)
        {
            var buf = Bytes(r, 300);
            foreach (var f in KwpFrame.ParseAll(buf)) Assert.InRange(f.Data.Length, 1, KwpFrame.MaxData);
            foreach (var f in Ds2Frame.ParseAll(buf)) Assert.InRange(f.Data.Length, 0, 252);
            foreach (var p in Iso9141Frame.ParseResponses(buf)) Assert.InRange(p.Length, 1, 8);
            Assert.True(Echo.Strip(buf, buf).IsEmpty);
        }
    }

    [Fact]
    public void FrameParsersSurviveDamagedFrames()
    {
        var r = new Random(46);
        for (var i = 0; i < 6_000; i++)
        {
            var data = Bytes(r, 254);
            if (data.Length == 0) data = [0x1A];
            var kwp = KwpFrame.Build((byte)r.Next(256), (byte)r.Next(256), data);
            Assert.True(Assert.Single(KwpFrame.ParseAll(kwp)).Data.SequenceEqual(data));
            var ds2 = Ds2Frame.Build((byte)r.Next(256), data.AsSpan(0, Math.Min(data.Length, 252)));
            Assert.Contains(Ds2Frame.ParseAll(ds2), f => f.Data.Length == Math.Min(data.Length, 252));

            foreach (var frame in new[] { kwp, ds2 })
            {
                // Cut short, a flipped bit, a byte dropped, a byte doubled, garbage in front and behind.
                KwpFrame.ParseAll(frame.AsSpan(0, r.Next(frame.Length + 1)));
                Ds2Frame.ParseAll(frame.AsSpan(0, r.Next(frame.Length + 1)));
                var flipped = (byte[])frame.Clone();
                flipped[r.Next(flipped.Length)] ^= (byte)(1 << r.Next(8));
                KwpFrame.ParseAll(flipped); Ds2Frame.ParseAll(flipped); Iso9141Frame.ParseResponses(flipped);
                var at = r.Next(frame.Length);
                byte[] dropped = [.. frame[..at], .. frame[(at + 1)..]];
                byte[] doubled = [.. frame[..at], frame[at], .. frame[at..]];
                byte[] padded = [.. Bytes(r, 20), .. frame, .. Bytes(r, 20)];
                foreach (var b in new[] { dropped, doubled, padded }) { KwpFrame.ParseAll(b); Ds2Frame.ParseAll(b); Iso9141Frame.ParseResponses(b); }
            }
        }
    }

    [Fact]
    public void AFlippedBitNeverPassesAsTheOriginalFrame()
    {
        var r = new Random(90);
        for (var i = 0; i < 6_000; i++)
        {
            var data = Bytes(r, 60);
            if (data.Length == 0) data = [0x18];
            var frame = KwpFrame.Build(0xF1, 0x12, data);
            var damaged = (byte[])frame.Clone();
            damaged[r.Next(damaged.Length)] ^= (byte)(1 << r.Next(8));
            // The checksum catches every single-bit error: the damaged frame may not be read as a whole frame.
            Assert.DoesNotContain(KwpFrame.ParseAll(damaged), f => f.Data.Length == data.Length && f.Target == 0xF1 && f.Source == 0x12 && !f.Data.SequenceEqual(data));
        }
    }

    [Fact]
    public void TheFastFrameSearchAgreesWithTheFullParser()
    {
        var r = new Random(12);
        for (var i = 0; i < 20_000; i++)
        {
            // Noise with real frames mixed in, some to the tester and some not.
            var parts = new List<byte>();
            for (var k = r.Next(4); k >= 0; k--)
            {
                parts.AddRange(Bytes(r, 12));
                var data = Bytes(r, 70);
                if (data.Length > 0) parts.AddRange(KwpFrame.Build((byte)(r.Next(3) == 0 ? 0x40 : 0xF1), (byte)(r.Next(2) == 0 ? 0x12 : 0x60), data));
            }
            var buf = parts.ToArray();
            var expected = KwpFrame.ParseAll(buf).FirstOrDefault(f => f.Source == 0x12 && f.Target == 0xF1)?.Data;
            var found = KwpFrame.TryFind(buf, 0x12, 0xF1, out var actual);
            Assert.Equal(expected is not null, found);
            if (expected is not null) Assert.True(expected.SequenceEqual(actual));
        }
    }

    [Fact]
    public void ReplyDecodersSurviveRandomPayloads()
    {
        var r = new Random(87);
        var ecu = Db.Modules.First(m => m.Id == "E9x.KOMBI");
        byte[][] heads = [[0x62, 0x20, 0x00], [0x61, 0x09], [0x61, 0x0F], [0x43], [0x49, 0x02], [0x41], [0x58], [0x5A, 0x80], [0x7F], []];
        for (var i = 0; i < 30_000; i++)
        {
            byte[] reply = [.. heads[r.Next(heads.Length)], .. Bytes(r, 140)];
            Assert.All(InfoMemory.Parse(ecu, reply), f => Assert.Equal(4, f.Code.Length));
            CheckControlParser.ParseActive(reply);
            CheckControlParser.ParseHistory(reply);
            CheckControlParser.ParseEdiabasList(Convert.ToHexString(reply), r.Next(-2, 300));
            Obd2.DecodeDtcs(reply, r.Next(2) == 0);
            Obd2.DecodeVin([reply, Bytes(r, 30)]);
            Obd2.DecodeSupported((byte)r.Next(256), reply);
            foreach (var pid in Obd2.Pids)
            {
                var v = Obd2.DecodeResponse(reply, pid);
                Assert.False(v is { } d && (double.IsNaN(d) || double.IsInfinity(d)));
            }
            Kwp2000.StateFromStatus((byte)r.Next(256));
            Kwp2000.NegativeResponseText((byte)r.Next(256));
            MileageReader.Parse(System.Text.Encoding.Latin1.GetString(reply));
        }
    }

    /// <summary>
    /// A car that answers every request with something plausible but wrong: well-framed replies with random contents,
    /// truncated frames, pure noise, a refusal, or silence.
    /// </summary>
    private sealed class HostileCar(int seed, bool kLine) : ITransport
    {
        private readonly Random _r = new(seed);
        private readonly Queue<byte> _pending = new();
        public int Requests { get; private set; }
        public string Name => "hostile car";
        public bool IsOpen { get; private set; }
        public bool? IgnitionOn => null;
        public void Open() => IsOpen = true;
        public void Close() => IsOpen = false;
        public void Configure(int baudRate, SerialParity parity) { }
        public void SetBreak(bool on) { }
        public void SetDtr(bool on) { }
        public void SetRts(bool on) { }
        public void DiscardInput() => _pending.Clear();
        public void Dispose() => Close();

        public void Write(ReadOnlySpan<byte> data)
        {
            Requests++;
            if (data.Length < 3) return;
            var request = data.ToArray();
            if (kLine && _r.Next(2) == 0) Send(request);   // the K-line hears itself
            var roll = _r.Next(100);
            if (roll < 3) return;                                     // silence
            if (roll < 8) { Send(Bytes(_r, 90)); return; }            // noise

            byte[] payload;
            var kwp = (request[0] & 0x80) != 0 && Checksum.Sum(request.AsSpan(0, request.Length - 1)) == request[^1];
            var iso = request[0] == 0x68;
            var start = kwp ? ((request[0] & 0x3F) == 0 ? 4 : 3) : iso ? 3 : 2;
            var body = start < request.Length - 1 ? request[start..^1] : [];
            if (roll < 16 && body.Length > 0) payload = [0x7F, body[0], (byte)_r.Next(256)];                    // refusal
            else if (kwp || iso) payload = [(byte)(body.Length > 0 ? body[0] + 0x40 : 0x50), .. body.Skip(1).Take(_r.Next(3)), .. Bytes(_r, 120)];
            else payload = [(byte)(_r.Next(8) == 0 ? _r.Next(256) : Ds2Frame.StatusOk), .. Bytes(_r, 120)];

            byte[] frame;
            if (kwp)
            {
                var source = request[1] is BmwFastChannel.Functional or BmwFastChannel.FunctionalEf ? (byte)_r.Next(256) : request[1];
                frame = KwpFrame.Build(request[2], source, payload);
            }
            else if (iso)
            {
                frame = [0x48, 0x6B, 0x12, .. payload.Take(7), 0];
                frame[^1] = Checksum.Sum(frame.AsSpan(0, frame.Length - 1));
            }
            else frame = Ds2Frame.Build(request[0], payload.AsSpan(0, Math.Min(payload.Length, 250)));

            if (roll < 24) frame = frame[.._r.Next(frame.Length)];                       // cut off mid-frame
            else if (roll < 30) frame[_r.Next(frame.Length)] ^= (byte)(1 << _r.Next(8)); // one damaged bit
            else if (roll < 36) frame = [.. frame, .. frame, .. Bytes(_r, 12)];          // sent twice, with a tail
            Send(frame);
        }

        private void Send(byte[] bytes) { foreach (var b in bytes) _pending.Enqueue(b); }

        public int Read(Span<byte> buffer, TimeSpan timeout)
        {
            if (_pending.Count == 0) { Thread.Sleep(1); return 0; }
            var n = Math.Min(Math.Min(buffer.Length, _pending.Count), 1 + _r.Next(40));   // arrives in pieces
            for (var i = 0; i < n; i++) buffer[i] = _pending.Dequeue();
            return n;
        }
    }

    private static async Task<int> Storm(NativeBackend backend, IReadOnlyList<EcuDefinition> modules, int rounds, Random r)
    {
        var unexpected = new List<string>();
        var done = 0;
        for (var i = 0; i < rounds; i++)
        {
            var ecu = modules[r.Next(modules.Count)];
            try
            {
                switch (r.Next(7))
                {
                    case 0: await backend.IdentifyAsync(ecu); break;
                    case 1:
                        foreach (var f in await backend.ReadFaultsAsync(ecu)) Assert.False(string.IsNullOrWhiteSpace(f.Code));
                        break;
                    case 2: await backend.ClearFaultsAsync(ecu); break;
                    case 3:
                        if (ecu.Protocol == BusProtocol.BmwFast) await backend.ReadVinAsync();   // on the K-line this is the slow OBD wake-up
                        else await backend.IdentifyAsync(ecu);
                        break;
                    case 4:
                        var live = backend.GetLiveParameters(ecu).Take(6).ToList();
                        if (live.Count > 0)
                            foreach (var v in await backend.ReadLiveAsync(ecu, live)) Assert.False(v.Value is { } d && (double.IsNaN(d) || double.IsInfinity(d)));
                        break;
                    case 5: await backend.RequestAsync(ecu, [0x22, (byte)r.Next(256), (byte)r.Next(256)]); break;
                    default:
                        if (CheckControlDescriber.IsCluster(ecu)) await backend.ReadCheckControlAsync(ecu);
                        else await backend.ProbePresentAsync();
                        break;
                }
                done++;
            }
            catch (Exception e) when (e is DiagnosticException or NotSupportedException or TimeoutException) { done++; }
            catch (Exception e) when (e is not Xunit.Sdk.XunitException) { unexpected.Add($"{ecu.Id}: {e.GetType().Name}: {e.Message} {e.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("Klemme15."))?.Trim()}"); }
        }
        Assert.True(unexpected.Count == 0, string.Join("\n", unexpected.Distinct().Take(12)));
        return done;
    }

    [Fact]
    public async Task TheBackendSurvivesAHostileCarOnDCan()
    {
        var modules = Db.FindVehicle("E9x")!.Modules.Where(m => m.Id is "E9x.DME" or "E9x.KOMBI" or "E9x.FRM" or "E9x.CAS" or "E9x.JBE").ToList();
        foreach (var seed in new[] { 1, 2, 3 })
        {
            var car = new HostileCar(seed, kLine: false);
            await using var backend = new NativeBackend(car, new NativeBackendOptions { Mode = CableMode.DCan, ProbeTimeout = TimeSpan.FromMilliseconds(30), ResponseTimeout = TimeSpan.FromMilliseconds(40) });
            await backend.ConnectAsync();
            Assert.Equal(60, await Storm(backend, modules, 60, new Random(seed)));
            Assert.True(car.Requests > 60);
        }
    }

    /// <summary>A cable that is pulled out after a few reads: every later read and write fails like a lost port does.</summary>
    private sealed class UnpluggedCable(ITransport inner, int readsBeforeUnplug) : ITransport
    {
        private int _reads;
        public int Closed { get; private set; }
        public bool Gone => _reads >= readsBeforeUnplug;
        public string Name => inner.Name;
        public bool IsOpen => inner.IsOpen;
        public bool? IgnitionOn => null;
        public void Open() { if (Gone) throw new DiagnosticException("the port is gone"); inner.Open(); }
        public void Close() { Closed++; inner.Close(); }
        public void Configure(int baudRate, SerialParity parity) => inner.Configure(baudRate, parity);
        public void SetBreak(bool on) => inner.SetBreak(on);
        public void SetDtr(bool on) => inner.SetDtr(on);
        public void SetRts(bool on) => inner.SetRts(on);
        public void DiscardInput() => inner.DiscardInput();
        public void Dispose() => inner.Dispose();
        public void Write(ReadOnlySpan<byte> data) { if (Gone) throw new IOException("A device attached to the system is not functioning."); inner.Write(data); }
        public int Read(Span<byte> buffer, TimeSpan timeout)
        {
            if (++_reads >= readsBeforeUnplug) throw new IOException("A device attached to the system is not functioning.");
            return inner.Read(buffer, timeout);
        }
    }

    [Fact]
    public async Task AnUnpluggedCableIsReportedOnceAndThePortReleased()
    {
        var cable = new UnpluggedCable(Klemme15.Protocols.Simulation.VirtualCar.E90_325i(), readsBeforeUnplug: 6);
        var backend = new NativeBackend(cable, new NativeBackendOptions { Mode = CableMode.DCan, ProbeTimeout = TimeSpan.FromMilliseconds(80) });
        var lost = new List<string>();
        var signalled = new TaskCompletionSource();
        backend.ConnectionLost += m => { lock (lost) lost.Add(m); signalled.TrySetResult(); };
        await backend.ConnectAsync();
        var dme = Db.Modules.Single(m => m.Id == "E9x.DME");

        var errors = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            try { await backend.ReadFaultsAsync(dme); }
            catch (DiagnosticException e) { errors.Add(e.Message); }
        }
        Assert.True(cable.Gone);
        Assert.NotEmpty(errors);
        Assert.Contains("unplugged", errors[0]);
        await signalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);
        lock (lost) Assert.Single(lost);
        Assert.True(cable.Closed >= 1);
        Assert.False(cable.IsOpen);
        await backend.DisposeAsync();   // must not hang or throw after the cable is gone
    }

    [Fact]
    public async Task TheBackendSurvivesAHostileCarOnTheKLine()
    {
        var modules = Db.FindVehicle("E46")!.Modules.Where(m => m.Id is "E46.IKE" or "E46.LCM" or "E46.DSC" or "E46.GM5").ToList();
        var car = new HostileCar(46, kLine: true);
        await using var backend = new NativeBackend(car, new NativeBackendOptions { Mode = CableMode.KLine, ProbeTimeout = TimeSpan.FromMilliseconds(30), ResponseTimeout = TimeSpan.FromMilliseconds(40) });
        await backend.ConnectAsync();
        Assert.Equal(60, await Storm(backend, modules, 60, new Random(46)));
    }
}
