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
using Klemme15.Protocols.Simulation;
using Xunit;

namespace Klemme15.Tests;

public class CheckControlTests
{
    private static readonly VehicleDatabase Db = VehicleDatabase.Load(Path.Combine(Path.GetTempPath(), "klemme15-no-overrides"));

    [Fact]
    public void ActiveReplyWithCountByte()
    {
        // 61 09, count 2, IDs 67 and 164
        var ids = CheckControlParser.ParseActive([0x61, 0x09, 0x02, 0x00, 0x43, 0x00, 0xA4]);
        Assert.Equal([67, 164], ids!);
    }

    [Fact]
    public void ActiveReplyWithoutCountByte()
    {
        var ids = CheckControlParser.ParseActive([0x61, 0x09, 0x00, 0x43, 0x00, 0xA4]);
        Assert.Equal([67, 164], ids!);
    }

    [Fact]
    public void ActiveReplyEmptyAndPlaceholders()
    {
        Assert.Empty(CheckControlParser.ParseActive([0x61, 0x09, 0x00])!);
        Assert.Empty(CheckControlParser.ParseActive([0x61, 0x09])!);
        Assert.Equal([67], CheckControlParser.ParseActive([0x61, 0x09, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x43])!);
    }

    [Fact]
    public void CountByteLimitsTheList()
    {
        // count says 1, trailing padding ignored
        Assert.Equal([67], CheckControlParser.ParseActive([0x61, 0x09, 0x01, 0x00, 0x43, 0x00, 0x20])!);
    }

    [Fact]
    public void NegativeOrForeignRepliesAreNull()
    {
        Assert.Null(CheckControlParser.ParseActive(null));
        Assert.Null(CheckControlParser.ParseActive([0x7F, 0x21, 0x12]));
        Assert.Null(CheckControlParser.ParseActive([0x61, 0x0F, 0x00]));
    }

    [Fact]
    public void HistoryEntriesCarryMileage()
    {
        // ID 67 at 18900 * 8 km, ID 164 with unknown mileage
        var h = CheckControlParser.ParseHistory([0x61, 0x0F, 0x00, 0x43, 0x49, 0xD4, 0x00, 0xA4, 0xFF, 0xFF]);
        Assert.Equal(2, h.Count);
        Assert.Equal(67, h[0].Id);
        Assert.Equal(0x49D4 * 8L, h[0].MileageKm);
        Assert.Null(h[1].MileageKm);
        Assert.Empty(CheckControlParser.ParseHistory([0x61, 0x0F, 0x00, 0x43, 0x01]));   // unknown layout: nothing rather than nonsense
    }

    [Fact]
    public void EdiabasBinaryList()
    {
        Assert.Equal([67, 164], CheckControlParser.ParseEdiabasList("004300A4"));
        Assert.Equal([67], CheckControlParser.ParseEdiabasList("00 43 00 A4", count: 1));
        Assert.Empty(CheckControlParser.ParseEdiabasList(null));
        Assert.Empty(CheckControlParser.ParseEdiabasList("0000FFFF"));
    }

    [Fact]
    public void CatalogUsesTheRightIds()
    {
        var key = Db.LookupCheckControl(67)!;
        Assert.Contains("Key battery", key.Title);
        Assert.Equal(CcSeverity.Advisory, key.Severity);
        Assert.Contains("Trailer", Db.LookupCheckControl(75)!.Title);   // not the key battery
        Assert.Equal(CcSeverity.Critical, Db.LookupCheckControl(212)!.Severity);
        Assert.Contains("Washer", Db.LookupCheckControl(164)!.Title);
        Assert.All(Db.CheckControl.Entries.Values, e =>
        {
            Assert.NotEmpty(e.Sources!);
            Assert.Contains(e.Confidence, new[] { "high", "medium" });
        });
    }

    [Fact]
    public void UserOverrideWinsAndBadEntriesAreSkipped()
    {
        var folder = Directory.CreateTempSubdirectory("klemme15-cc").FullName;
        File.WriteAllText(Path.Combine(folder, "check-control.json"), """
            { "messages": [
              { "id": 67, "title": "My key text", "severity": "warning" },
              { "id": "x", "title": "bad id" },
              { "id": 4001, "title": "" },
              { "id": 4000, "title": "New one", "severity": "critical" }
            ] }
            """);
        var db = VehicleDatabase.Load(folder);
        Assert.Equal("My key text", db.LookupCheckControl(67)!.Title);
        Assert.Equal(CcSeverity.Critical, db.LookupCheckControl(4000)!.Severity);
        Assert.Null(db.LookupCheckControl(4001));
        Assert.NotNull(db.LookupCheckControl(164));   // built-in entries remain
    }

    [Fact]
    public void UnknownIdsStillShow()
    {
        var m = CheckControlDescriber.Describe(4321, Db);
        Assert.True(m.Unknown);
        Assert.Equal("CC 4321", m.Code);
        Assert.Equal(CcSeverity.Warning, m.Severity);
        Assert.Contains("4321", m.Title);
    }

    [Fact]
    public void DescribeOrdersActiveBySeverityAndDropsDuplicateHistory()
    {
        var reading = new CheckControlReading([67, 212], [new(67, 1000), new(164, 900), new(164, 500)]);
        var list = CheckControlDescriber.Describe(reading, Db);
        Assert.Equal([212, 67, 164], list.Select(m => m.Id));
        Assert.Equal(900, list.Single(m => m.Id == 164).MileageKm);
        Assert.Equal(CcSeverity.Critical, CheckControlDescriber.Worst(list));
        Assert.Null(CheckControlDescriber.Worst([]));
    }

    [Fact]
    public void InfoMemoryReplies()
    {
        var cas = Db.Modules.Single(m => m.Id == "E9x.CAS");
        Assert.True(InfoMemory.Offered(cas));
        Assert.True(InfoMemory.Offered(Db.Modules.Single(m => m.Id == "E9x.KOMBI")));
        Assert.False(InfoMemory.Offered(Db.Modules.Single(m => m.Id == "E46.IKE")));   // DS2: not this protocol
        var entries = InfoMemory.Parse(cas, [0x62, 0x20, 0x00, 0xA0, 0xC4, 0x20, 0x00, 0x00, 0x00]);
        var f = Assert.Single(entries);
        Assert.Equal("A0C4", f.Code);
        Assert.True(f.IsInfo && f.IsShadow);
        Assert.Equal("Shadow / Info memory", f.MemoryLabel);
        Assert.Single(InfoMemory.Parse(cas, [0x62, 0x20, 0x00, 0x01, 0xA0, 0xC4, 0x20]));   // with a count byte
        Assert.Empty(InfoMemory.Parse(cas, [0x7F, 0x22, 0x31]));
        Assert.Empty(InfoMemory.Parse(cas, [0x62, 0x20, 0x00, 0xA0, 0xC4]));               // doesn't fit: ignored
    }

    [Fact]
    public async Task DemoCarShowsTheTriangleReason()
    {
        var car = VirtualCar.E90_325i();
        await using var backend = new NativeBackend(car, new NativeBackendOptions { Mode = CableMode.DCan, ProbeTimeout = TimeSpan.FromMilliseconds(80) });
        await backend.ConnectAsync();
        var scan = await new VehicleScanner(backend, Db).ScanAsync(Db.FindVehicle("E9x")!.Modules, "E90 325i");

        Assert.Equal(1, scan.ActiveCheckControl);
        var key = scan.CheckControl.First();
        Assert.Equal(67, key.Id);
        Assert.True(key.Active);
        var washer = scan.CheckControl.Single(m => m.Id == 164);
        Assert.False(washer.Active);
        Assert.Equal(148_800, washer.MileageKm);
        Assert.DoesNotContain(scan.CheckControl, m => !m.Active && m.Id == 67);   // active wins over its own history

        var cas = scan.Ecus.Single(e => e.Ecu.Id == "E9x.CAS");
        Assert.Equal(EcuScanStatus.Ok, cas.Status);   // info memory is not a fault
        Assert.Contains(cas.Faults, f => f.Code == "A0C4" && f.IsShadow);

        var md = ReportWriter.ToMarkdown(scan);
        Assert.Contains("Dash messages (Check Control)", md);
        Assert.Contains("CC 67", md);
        Assert.Contains("Shadow / Info memory", md);
        var html = ReportWriter.ToHtml(scan);
        Assert.Contains("Dash messages (Check Control)", html);
        Assert.Contains("Key battery low", html);
        var forum = ReportWriter.ToForumPost(scan);
        Assert.Contains("CC 67", forum);
        Assert.Contains("Shadow / Info memory", forum);
        Assert.DoesNotContain(VirtualCar.Vin, forum);
    }

    [Fact]
    public void NoClusterMeansNoNoteAndNoSection()
    {
        var scan = new VehicleScanResult { Started = DateTimeOffset.Now, Backend = "test", Ecus = [] };
        Assert.DoesNotContain("Dash messages", ReportWriter.ToMarkdown(scan));
        Assert.DoesNotContain("Dash messages", ReportWriter.ToForumPost(scan));
    }

    [Fact]
    public void BmwLineBreaksAreJoined()
    {
        Assert.Equal("Motor überhitzt! Vorsichtig anhalten", SgbdFolderSource.CleanCc("Motor überhitzt! Vor- sichtig anhalten"));
        Assert.Equal("Außentemperatur", SgbdFolderSource.CleanCc("Außentemperatur %s"));
    }

    [Fact]
    public void MasterListCoversCommonIdsAndUsesBmwNumbering()
    {
        var entries = Db.CheckControl.Entries;
        Assert.True(entries.Count >= 390);
        foreach (var id in new[] { 14, 63, 67, 71, 88, 134, 164, 213, 229, 345, 608, 811 }) Assert.True(entries.ContainsKey(id), $"CC {id}");
        Assert.Contains("Left", entries[123].Title);   // community lists say right; BMW's table says left
        Assert.All(entries.Values, e => Assert.False(string.IsNullOrWhiteSpace(e.Description)));
    }

    [Fact]
    public void InfoMemoryClearIsExactlyRoutine06()
    {
        // $31 02 would erase flash: these bytes must never change.
        Assert.Equal([0x31, 0x06], InfoMemory.ClearRequest);
        Assert.True(InfoMemory.Cleared([0x71, 0x06]));
        Assert.False(InfoMemory.Cleared([0x7F, 0x31, 0x12]));
        Assert.True(InfoMemory.Supported([0x62, 0x20, 0x00]));
        Assert.False(InfoMemory.Supported([0x7F, 0x22, 0x12]));
    }

    [Fact]
    public async Task ClearingAlsoEmptiesTheInfoMemory()
    {
        var car = VirtualCar.E90_325i();
        await using var backend = new NativeBackend(car, new NativeBackendOptions { Mode = CableMode.DCan, ProbeTimeout = TimeSpan.FromMilliseconds(80) });
        await backend.ConnectAsync();
        var cas = Db.Modules.Single(m => m.Id == "E9x.CAS");
        Assert.Contains(await backend.ReadFaultsAsync(cas), f => f.IsShadow);
        await backend.ClearFaultsAsync(cas);
        Assert.DoesNotContain(await backend.ReadFaultsAsync(cas), f => f.IsShadow);
        await backend.ClearFaultsAsync(Db.Modules.Single(m => m.Id == "E9x.KOMBI"));   // no info memory: left alone, no error
    }

    [Fact]
    public void UnknownCodesStillGetAMeaning()
    {
        var dsc = Db.Modules.Single(m => m.Id == "E9x.DSC");
        var bus = Db.Describe(new Fault { EcuId = dsc.Id, Code = "D35B", Source = FaultSource.Bmw }, dsc, null);
        Assert.Contains("communication", bus.Description);
        Assert.Equal(FaultFallback.Origin, bus.DescriptionOrigin);
        Assert.NotNull(bus.Hint);
        var other = Db.Describe(new Fault { EcuId = dsc.Id, Code = "5FFF", Source = FaultSource.Bmw }, dsc, null);
        Assert.Contains("5FFF", other.Description);
        Assert.Contains("download", other.Hint!.ToLowerInvariant());
    }
}

public class CarStoryTests
{
    private static readonly VehicleDatabase Db = VehicleDatabase.Load(Path.Combine(Path.GetTempPath(), "klemme15-no-overrides"));

    [Fact]
    public void TellsPuncturesAndVoltageDipsApart()
    {
        CheckControlHistoryEntry H(int id, long km) => new(id, km);
        var history = new List<CheckControlHistoryEntry>
        {
            H(63, 120_000), H(63, 120_008), H(63, 120_016), H(63, 120_048),   // a puncture nagging over 48 km
            H(67, 125_000),                                                     // key battery
            H(354, 130_000), H(50, 130_000), H(35, 130_000), H(42, 130_000), H(24, 130_000), H(229, 130_000), // a voltage dip
            H(167, 130_040),                                                    // clock reset after battery work
        };
        var story = CarStory.Build(history, Db);
        Assert.Equal(history.Count, story.Entries);
        Assert.Equal(130_040, story.LastKm);
        Assert.Equal([StoryKind.Puncture, StoryKind.KeyBattery, StoryKind.LowVoltage, StoryKind.BatteryWork], story.Events.Select(e => e.Kind));
        var flat = story.Events[0];
        Assert.Equal(4, flat.Times);
        Assert.Equal("120,000 – 120,048 km", flat.When);
        Assert.Contains("4 times", flat.Explanation);
        var dip = story.Events[2];
        Assert.Equal(6, dip.Messages.Count);
        Assert.Contains("voltage dip", dip.Explanation);
        Assert.Equal("Battery ran low", dip.Title);
    }

    [Fact]
    public void ABatteryWarningOnItsOwnReadsNaturally()
    {
        var e = Assert.Single(CarStory.Build([new(229, 50_000), new(229, 50_000)], Db).Events);
        Assert.Equal(StoryKind.LowVoltage, e.Kind);
        Assert.DoesNotContain("systems", e.Explanation);
    }

    [Fact]
    public void EmptyHistoryHasNoStory()
    {
        Assert.True(CarStory.Build([], Db).IsEmpty);
    }

    [Fact]
    public async Task DemoCarTellsItsStoryInReports()
    {
        var family = Db.FindVehicle("E9x")!;
        var model = family.Models.First(m => m.DemoCheckControl.Count > 0);
        var car = VirtualCar.ForModel(model, family.Modules);
        await using var backend = new NativeBackend(car, new NativeBackendOptions { Mode = CableMode.DCan, ProbeTimeout = TimeSpan.FromMilliseconds(80) });
        await backend.ConnectAsync();
        var scan = await new VehicleScanner(backend, Db).ScanAsync(family.Modules, model.Id);
        Assert.NotNull(scan.Story);
        Assert.Contains(scan.Story!.Events, e => e.Kind == StoryKind.LowVoltage);
        Assert.Contains(scan.Story.Events, e => e.Kind == StoryKind.Puncture && e.Times == 3);
        Assert.Contains("The car's story", ReportWriter.ToHtml(scan));
        Assert.Contains("Battery ran low", ReportWriter.ToMarkdown(scan));
        Assert.Contains("Warning history", ReportWriter.ToForumPost(scan));
    }

    [Fact]
    public void A_report_only_links_web_addresses_and_escapes_everything_else()
    {
        // A shared snapshot can carry anything in its sources and texts: a saved report must not run it.
        var dme = new EcuDefinition { Id = "E9x.DME", Name = "Engine <b>(DME)</b>", Address = 0x12, Protocol = BusProtocol.BmwFast, IsEngine = true };
        var scan = new VehicleScanResult
        {
            Started = DateTimeOffset.Now, Backend = "t",
            Ecus = [new EcuScanResult { Ecu = dme, Status = EcuScanStatus.Faults, Faults =
            [
                new Fault
                {
                    EcuId = dme.Id, Code = "2A82", Source = FaultSource.Bmw, State = FaultState.Stored, Confidence = "high",
                    Description = "<script>alert(1)</script>",
                    Sources = ["javascript:alert(1)", "file:///C:/Windows/System32/calc.exe", "https://example.org/real"],
                },
            ] }],
        };
        var html = ReportWriter.ToHtml(scan);
        Assert.DoesNotContain("<script>alert", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.DoesNotContain("file:///", html);
        Assert.Contains("href=\"https://example.org/real\"", html);
        Assert.Contains("Engine &lt;b&gt;(DME)&lt;/b&gt;", html);
    }
}
