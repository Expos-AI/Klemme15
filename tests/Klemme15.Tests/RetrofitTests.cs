using Klemme15.Core;
using Klemme15.Data;
using Xunit;

namespace Klemme15.Tests;

public class RetrofitTests
{
    private static readonly VehicleDatabase Db = VehicleDatabase.Load(Path.Combine(Path.GetTempPath(), "klemme15-no-overrides"));
    private static readonly EcuDefinition Frm = new() { Id = "E9x.FRM", Name = "Footwell module (FRM)", Address = 0x72, Protocol = BusProtocol.BmwFast };
    private static readonly EcuDefinition Pdc = new() { Id = "E9x.PDC", Name = "Park distance control (PDC)", Address = 0x64, Protocol = BusProtocol.BmwFast };

    private static VehicleScanResult Scan(DateTimeOffset when, string frmPart, bool pdc = false, IReadOnlyList<CheckControlMessage>? cc = null, IReadOnlyList<Fault>? frmFaults = null) => new()
    {
        Started = when, Backend = "test",
        Ecus = new[] { new EcuScanResult { Ecu = Frm, Status = EcuScanStatus.Ok, Identity = new EcuIdentity { PartNumber = frmPart }, Faults = frmFaults ?? [] } }
            .Concat(pdc ? [new EcuScanResult { Ecu = Pdc, Status = EcuScanStatus.Ok, Identity = new EcuIdentity { PartNumber = "9999999" } }] : []).ToList(),
        CheckControl = cc ?? [],
    };

    [Fact]
    public void ShippedRetrofitsAreCompleteAndTheirMessagesExist()
    {
        Assert.True(Db.Retrofits.Count >= 6);
        foreach (var r in Db.Retrofits)
        {
            Assert.NotEmpty(r.Needs);
            Assert.NotEmpty(r.Sources);
            foreach (var id in r.WatchMessages) Assert.True(Db.LookupCheckControl(id) is not null, $"{r.Id}: dash message {id} isn't in the catalogue");
        }
    }

    [Fact]
    public void ASwappedModuleIsNoticedAndPointsToItsGuide()
    {
        var t0 = DateTimeOffset.Now.AddDays(-10);
        var history = new List<HealthSnapshot> { HealthSnapshot.From(Scan(t0, "6976405") with { Vin = "WBAVB13586PT12345" }, null, null, new Dictionary<string, double>())! };
        var found = RetrofitAdvisor.Detect(Scan(DateTimeOffset.Now, "9169186"), null, history, Db.Retrofits);
        var swap = Assert.Single(found, f => f.Title.EndsWith("was replaced"));
        Assert.Contains("6976405", swap.Detail);
        Assert.Equal("frm-replacement", swap.GuideId);
    }

    [Fact]
    public void AnAddedModuleIsNoticed()
    {
        var model = new VehicleModel { Id = "E9x-325i", FamilyId = "E9x", Name = "325i", Engine = "N52", ModuleIds = ["E9x.FRM"] };
        var found = RetrofitAdvisor.Detect(Scan(DateTimeOffset.Now, "6976405", pdc: true), model, [], Db.Retrofits);
        Assert.Contains(found, f => f.Title == "Park distance control (PDC) is fitted" && f.GuideId == "pdc-retrofit");
    }

    [Fact]
    public void CheckSaysWhatsLeft()
    {
        var guide = Db.Retrofits.Single(r => r.Id == "lci-tail-lights");
        var bulb = CheckControlDescriber.Describe(133, Db, active: true);
        Assert.False(RetrofitAdvisor.Check(guide, Scan(DateTimeOffset.Now, "1", cc: [bulb])).Clean);
        Assert.True(RetrofitAdvisor.Check(guide, Scan(DateTimeOffset.Now, "1")).Clean);
    }
}
