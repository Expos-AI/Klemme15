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
              { "id": 999, "title": "" },
              { "id": 4000, "title": "New one", "severity": "critical" }
            ] }
            """);
        var db = VehicleDatabase.Load(folder);
        Assert.Equal("My key text", db.LookupCheckControl(67)!.Title);
        Assert.Equal(CcSeverity.Critical, db.LookupCheckControl(4000)!.Severity);
        Assert.Null(db.LookupCheckControl(999));
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
        Assert.False(InfoMemory.Offered(Db.Modules.Single(m => m.Id == "E9x.KOMBI")));
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
}
