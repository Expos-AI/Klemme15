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
using Klemme15.Protocols.Recording;
using Klemme15.Protocols.Simulation;
using Klemme15.Protocols.Transport;
using Xunit;

namespace Klemme15.Tests;

public class SessionRecordingTests
{
    private static readonly VehicleDatabase Db = VehicleDatabase.Load(Path.Combine(Path.GetTempPath(), "klemme15-no-overrides"));
    private static readonly K15LogInfo Info = new("0.99.42", "E9x", "E9x-325i", "DCan", new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));

    private static async Task<(VehicleScanResult Scan, byte[] File)> RecordScan()
    {
        var file = new MemoryStream();
        VehicleScanResult scan;
        await using (var backend = new NativeBackend(new RecordingTransport(VirtualCar.E90_325i(), new K15LogRecorder(file, Info)),
                         new NativeBackendOptions { Mode = CableMode.DCan, ProbeTimeout = TimeSpan.FromMilliseconds(80) }))
        {
            await backend.ConnectAsync();
            scan = await new VehicleScanner(backend, Db).ScanAsync(Db.FindVehicle("E9x")!.Modules, "E90 325i");
        }
        return (scan, file.ToArray());
    }

    [Fact]
    public async Task ARecordedScanPlaysBackTheSame()
    {
        var (recorded, file) = await RecordScan();
        var log = K15Log.Read(file);
        Assert.True(log.Complete);
        Assert.Equal("E9x", log.Info.Vehicle);
        Assert.Equal("E9x-325i", log.Info.Model);
        Assert.True(log.Exchanges.Count > 20);

        await using var backend = new NativeBackend(new ReplayTransport(log, speed: 20), new NativeBackendOptions { Mode = CableMode.DCan, ProbeTimeout = TimeSpan.FromMilliseconds(80) });
        await backend.ConnectAsync();
        var played = await new VehicleScanner(backend, Db).ScanAsync(Db.FindVehicle("E9x")!.Modules, "E90 325i");

        Assert.Equal(recorded.Vin, played.Vin);
        Assert.Equal(recorded.RespondingEcus, played.RespondingEcus);
        Assert.Equal(recorded.Ecus.SelectMany(e => e.Faults).Select(f => f.EcuId + f.Code).Order(),
                     played.Ecus.SelectMany(e => e.Faults).Select(f => f.EcuId + f.Code).Order());
        Assert.Equal(recorded.Ecus.Select(e => e.Identity?.PartNumber), played.Ecus.Select(e => e.Identity?.PartNumber));
    }

    [Fact]
    public async Task ARequestTheRecordingNeverMadeGetsNoReply()
    {
        var (_, file) = await RecordScan();
        var replay = new ReplayTransport(K15Log.Read(file), speed: 50);
        replay.Open();
        replay.Write([0x83, 0x12, 0xF1, 0x31, 0x99, 0x99, 0x69]);
        var buffer = new byte[64];
        Assert.Equal(0, replay.Read(buffer, TimeSpan.FromMilliseconds(20)));
        Assert.Equal(1, replay.Unmatched);
    }

    [Fact]
    public async Task ARecordingCutOffMidWayStillPlaysWhatItHas()
    {
        var (_, file) = await RecordScan();
        var cut = K15Log.Read(file[..(file.Length * 2 / 3)]);
        Assert.False(cut.Complete);
        Assert.True(cut.Exchanges.Count > 0);
        Assert.True(cut.Exchanges.Count < K15Log.Read(file).Exchanges.Count);
    }

    [Fact]
    public void OtherFilesAreRefusedInPlainWords()
    {
        Assert.Throws<DiagnosticException>(() => K15Log.Read("not a recording at all"u8.ToArray()));
        Assert.Throws<DiagnosticException>(() => K15Log.Read([]));
        Assert.Throws<DiagnosticException>(() => K15Log.Read([(byte)'K', (byte)'1', (byte)'5', (byte)'L', 99, 0]));
    }

    [Fact]
    public void SilenceAfterARequestKeepsItApartFromTheNext()
    {
        var file = new MemoryStream();
        using (var recorder = new K15LogRecorder(file, Info))
        {
            recorder.Add(LogKind.Tx, [1, 2, 3]);
            recorder.Add(LogKind.Rx, []);          // listened, nothing came
            recorder.Add(LogKind.Tx, [4, 5]);
            recorder.Add(LogKind.Tx, [6]);         // sent back to back: one request
            recorder.Add(LogKind.Rx, [7, 8]);
            recorder.Add(LogKind.Rx, [9]);
        }
        var log = K15Log.Read(file.ToArray());
        Assert.Equal(2, log.Exchanges.Count);
        Assert.Equal(new byte[] { 1, 2, 3 }, log.Exchanges[0].Request);
        Assert.Empty(log.Exchanges[0].Replies);
        Assert.Equal(new byte[] { 4, 5, 6 }, log.Exchanges[1].Request);
        Assert.Equal(new byte[] { 7, 8, 9 }, log.Exchanges[1].Replies.SelectMany(r => r.Data).ToArray());
    }

    [Fact]
    public void RepeatedRequestsMoveForwardThroughTheRecording()
    {
        var file = new MemoryStream();
        using (var recorder = new K15LogRecorder(file, Info))
            for (byte i = 1; i <= 3; i++)
            {
                recorder.Add(LogKind.Tx, [0xAA, 0xBB]);
                recorder.Add(LogKind.Rx, [i]);
            }
        var replay = new ReplayTransport(K15Log.Read(file.ToArray()), speed: 100);
        var buffer = new byte[8];
        var seen = new List<byte>();
        for (var i = 0; i < 4; i++)
        {
            replay.Write([0xAA, 0xBB]);
            Assert.Equal(1, replay.Read(buffer, TimeSpan.FromMilliseconds(200)));
            seen.Add(buffer[0]);
        }
        Assert.Equal(new byte[] { 1, 2, 3, 1 }, seen);   // and round again once the recording ends
    }

    [Fact]
    public void TheQueueKeepsRecordsWholeAcrossItsEnd()
    {
        var ring = new SpscByteRing(16);
        var read = new byte[16];
        Assert.True(ring.TryWrite([1, 2, 3, 4, 5], [6, 7, 8, 9, 10]));
        Assert.Equal(10, ring.Read(read));
        Assert.True(ring.TryWrite([11, 12, 13, 14], [15, 16, 17, 18, 19, 20]));   // wraps past the end
        Assert.False(ring.TryWrite([1, 2, 3, 4], [5, 6, 7]));                      // 7 don't fit beside 10: nothing is written
        Assert.Equal(10, ring.Read(read));
        Assert.Equal(new byte[] { 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 }, read[..10]);
        Assert.Equal(0, ring.Read(read));
        Assert.Throws<ArgumentException>(() => new SpscByteRing(100));
    }
}
