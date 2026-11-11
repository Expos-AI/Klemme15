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

using System.IO.Compression;
using System.Text;
using Klemme15.Core;
using Klemme15.Data;
using Klemme15.Protocols;
using Klemme15.Protocols.Simulation;
using Xunit;

namespace Klemme15.Tests;

public class SnapshotTests
{
    private static readonly VehicleDatabase Db = VehicleDatabase.Load(Path.Combine(Path.GetTempPath(), "klemme15-no-overrides"));
    private const string Vin = "WBAVB13586PT12345";

    private static async Task<VehicleScanResult> RealishScan()
    {
        await using var backend = new NativeBackend(VirtualCar.E90_325i(), new NativeBackendOptions { Mode = CableMode.DCan, ProbeTimeout = TimeSpan.FromMilliseconds(80) });
        await backend.ConnectAsync();
        var scan = await new VehicleScanner(backend, Db).ScanAsync(Db.FindVehicle("E9x")!.Modules, "E90 325i");
        return scan with
        {
            Vin = Vin,
            BatteryVoltage = 12.4,
            Health = new HealthSummary(4, DateTimeOffset.Now.AddDays(-20), [new HealthTrend(TrendLevel.Watch, "Fuel trims creeping leaner", "From +1 % to +6 %.")]),
        };
    }

    private static CarSnapshot Snapshot(VehicleScanResult scan) => new()
    {
        Manifest = new SnapshotManifest { App = "test", Created = DateTimeOffset.Now, FamilyId = "E9x", ModelId = "E90-325i", ModelName = "325i", Chassis = "E90", Note = $"Rough idle, VIN {Vin}" },
        Scan = scan,
        LiveCsv = "time,\"Engine speed (rpm)\",\"Coolant (°C)\"\n0.000,750,90\n0.500,760,90\n",
        Tests = [new SnapshotTest("water-pump", "Water pump health", DateTimeOffset.Now,
            new TestReport([new TestFinding(TestSeverity.Ok, "Keeps up", "93 %")], [new("Actual vs requested (%)", 93)]) { Traces = [new TestTrace("Pump actual", "pump", [0, 0.5], [38, 39])] })],
        Coding = [new SnapshotCoding("FRM", "7004041", "04", [new SnapshotCodingBlock(0x3000, Convert.ToHexString(Encoding.ASCII.GetBytes("xx" + Vin[^7..] + "yy")))])],
        TrafficLog = $"> 12 22 F1 90\n< VIN {Vin}\nRX 62 F1 90 {string.Join(" ", Convert.ToHexString(Encoding.ASCII.GetBytes(Vin)).Chunk(2).Select(c => new string(c)))}\n",
    };

    [Fact]
    public async Task TheFingerprintIsTheSameForTheSameFileAndChangesWithOneByte()
    {
        var file = SnapshotFile.Write(Snapshot(await RealishScan()));
        var fingerprint = SnapshotFile.Fingerprint(file);
        Assert.Matches("^K15(-[0-9A-HJKMNP-TV-Z]{4}){4}$", fingerprint);
        Assert.Equal(fingerprint, SnapshotFile.Fingerprint(file.ToArray()));
        Assert.Equal(fingerprint, SnapshotFile.Read(file).Fingerprint);
        var altered = file.ToArray();
        altered[^1] ^= 1;
        Assert.NotEqual(fingerprint, SnapshotFile.Fingerprint(altered));
        // A known answer, so the app, the command line and the website's viewer can't drift apart.
        Assert.Equal("K15-Q9W1-DFWF-077Y-MGA1", SnapshotFile.Fingerprint("abc"u8));
    }

    [Fact]
    public async Task RoundTripKeepsTheWholeCar()
    {
        var scan = await RealishScan();
        var back = SnapshotFile.Read(SnapshotFile.Write(Snapshot(scan)));
        Assert.Equal(scan.Ecus.Count, back.Scan.Ecus.Count);
        Assert.Equal(scan.TotalFaults, back.Scan.TotalFaults);
        Assert.Equal(scan.Ecus.Count(e => e.Identity?.PartNumber is not null), back.Scan.Ecus.Count(e => e.Identity?.PartNumber is not null));
        Assert.Equal(12.4, back.Scan.BatteryVoltage);
        Assert.Equal("Fuel trims creeping leaner", back.Scan.Health!.Trends[0].Title);
        Assert.True(back.HasLive);
        Assert.Equal(2, DataLog.Parse(back.LiveCsv).Count);
        Assert.Equal(TestSeverity.Ok, back.Tests.Single().Report.Verdict);
        Assert.Equal(2, back.Tests.Single().Report.Traces.Single().Values.Count);
        Assert.Equal("FRM", back.Coding.Single().Module);
        Assert.Equal("325i", back.Manifest.ModelName);
    }

    [Fact]
    public async Task TheVinIsNowhereInTheFile()
    {
        var bytes = SnapshotFile.Write(Snapshot(await RealishScan()));
        using var zip = new ZipArchive(new MemoryStream(bytes));
        foreach (var e in zip.Entries)
        {
            using var r = new StreamReader(e.Open());
            var text = r.ReadToEnd();
            Assert.False(text.Contains(Vin, StringComparison.OrdinalIgnoreCase), $"{e.FullName} contains the VIN");
            Assert.False(text.Contains(Vin[^7..], StringComparison.OrdinalIgnoreCase), $"{e.FullName} contains the short VIN");
            Assert.False(text.Contains(Convert.ToHexString(Encoding.ASCII.GetBytes(Vin[^7..]))), $"{e.FullName} contains the VIN inside coding bytes");
            Assert.False(text.Contains(string.Join(" ", Convert.ToHexString(Encoding.ASCII.GetBytes(Vin[^7..])).Chunk(2).Select(c => new string(c)))), $"{e.FullName} contains the VIN as logged bytes");
        }
        Assert.Null(SnapshotFile.Read(bytes).Scan.Vin);
    }

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (name, text) in entries)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write(text);
            }
        return ms.ToArray();
    }

    [Fact]
    public async Task DamagedOrHostileFilesAreRefused()
    {
        Assert.Throws<SnapshotException>(() => SnapshotFile.Read(Encoding.UTF8.GetBytes("not a zip")));
        Assert.Throws<SnapshotException>(() => SnapshotFile.Read(Zip(("scan.json", "{}"))));
        Assert.Throws<SnapshotException>(() => SnapshotFile.Read(Zip(("manifest.json", "{\"Format\":\"something-else\"}"))));
        Assert.Throws<SnapshotException>(() => SnapshotFile.Read(Zip(("manifest.json", "{\"Format\":\"klemme15-snapshot\",\"Version\":99}"))));

        // An edited part no longer matches its checksum.
        var good = SnapshotFile.Write(Snapshot(await RealishScan()));
        using (var ms = new MemoryStream())
        {
            ms.Write(good);
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, true))
            {
                zip.GetEntry("live.csv")!.Delete();
                using var w = new StreamWriter(zip.CreateEntry("live.csv").Open());
                w.Write("time,x\n0,1\n");
            }
            Assert.Throws<SnapshotException>(() => SnapshotFile.Read(ms.ToArray()));
        }

        // Paths that try to escape are never read; a bomb-sized part is refused.
        var sneaky = SnapshotFile.Read(AddEntry(good, "../../evil.exe", "MZ"));
        Assert.Null(sneaky.TrafficLog is { } t && t.Contains("MZ") ? t : null);
        Assert.Throws<SnapshotException>(() => SnapshotFile.Read(AddEntry(good, "log.txt", new string('a', (int)SnapshotFile.MaxEntryBytes + 10), replace: true)));
    }

    private static byte[] AddEntry(byte[] file, string name, string text, bool replace = false)
    {
        using var ms = new MemoryStream();
        ms.Write(file);
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, true))
        {
            if (replace) zip.GetEntry(name)?.Delete();
            using var w = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open());
            w.Write(text);
        }
        return ms.ToArray();
    }

    [Fact]
    public void LiveBufferKeepsOnlyTheLastMinute()
    {
        var buffer = new LiveBuffer(TimeSpan.FromSeconds(60));
        var t0 = DateTimeOffset.Now;
        for (var i = 0; i < 200; i++) buffer.Add(t0.AddSeconds(i * 0.5), new Dictionary<string, double?> { ["Engine speed (rpm)"] = 700 + i });
        var log = DataLog.Parse(buffer.ToCsv());
        Assert.InRange(log.Duration.TotalSeconds, 59, 60.01);
        Assert.Equal("Engine speed", log.Channels[0].Name);
        Assert.Equal("rpm", log.Channels[0].Unit);
    }
}
