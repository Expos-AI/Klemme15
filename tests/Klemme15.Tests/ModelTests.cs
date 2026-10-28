using Klemme15.Core;
using Klemme15.Data;
using Klemme15.Protocols;
using Klemme15.Protocols.Simulation;
using Xunit;

namespace Klemme15.Tests;

public class ModelTests
{
    private static readonly VehicleDatabase Db = VehicleDatabase.Load(Path.Combine(Path.GetTempPath(), "klemme15-no-overrides"));

    public static IEnumerable<object[]> AllModels() =>
        Db.Vehicles.SelectMany(v => v.Models).Select(m => new object[] { m.Id });

    [Fact]
    public void EveryFamilyHasModelsAndEveryModuleHasAPosition()
    {
        Assert.Empty(Db.LoadWarnings);
        Assert.All(Db.Vehicles, v => Assert.True(v.Models.Count >= 5));
        Assert.All(Db.Modules, m => Assert.True(m.Position is not null || m.PartOf is not null, $"{m.Id} has no position"));
        Assert.All(Db.Vehicles, v => Assert.Equal(v.Modules.Count, v.Modules.Select(m => m.Address).Distinct().Count()));
        Assert.All(Db.Components, c => Assert.InRange(c.Position.X, 0, 1000));
    }

    [Theory]
    [InlineData("P0303", "cyl3")]
    [InlineData("P0171", "intake")]
    [InlineData("P0597", "thermostat")]
    [InlineData("P0420", "cat")]
    [InlineData("P0087", "fuel_rail")]
    [InlineData("P0156", "o2_b2s2")]
    public void ObdCodesPointAtTheRightComponent(string code, string component)
    {
        var dme = Db.Modules.Single(m => m.Id == "E9x.DME");
        var where = Db.Locate(new Fault { EcuId = dme.Id, Code = code, Source = FaultSource.Obd2 }, dme);
        Assert.Equal(Db.FindComponent(component)!.Name, where!.Title);
        Assert.True(where.IsComponent);
    }

    [Fact]
    public void UnknownCodesPointAtTheirModule()
    {
        var frm = Db.Modules.Single(m => m.Id == "E9x.FRM");
        var where = Db.Locate(new Fault { EcuId = frm.Id, Code = "ABCD", Source = FaultSource.Bmw }, frm);
        Assert.Equal(frm.Position, where!.Position);
        Assert.Equal(frm.Name, where.Title);
    }

    [Fact]
    public void CodesSeenOnARealE90AreDescribedAndPlaced()
    {
        var cas = Db.Modules.Single(m => m.Id == "E9x.CAS");
        Assert.True(cas.Verified);
        Assert.Contains("steering lock", Db.DescribeBmw("E9x.CAS", "A119"));
        var where = Db.Locate(new Fault { EcuId = cas.Id, Code = "A119", Source = FaultSource.Bmw }, cas);
        Assert.True(where!.IsComponent);
        Assert.Equal(Db.FindComponent("steering_lock")!.Name, where.Title);
    }

    [Fact]
    public async Task DiscoveryFindsModulesTheDatabaseDoesNotList()
    {
        var family = Db.FindVehicle("E9x")!;
        var addresses = VehicleDatabase.UnlistedAddresses(family);
        Assert.DoesNotContain(addresses, a => family.Modules.Any(m => m.Address == a));
        Assert.DoesNotContain(addresses, a => a == 0xDF || a >= 0xF0);

        var car = new VirtualCar("extra", [new VirtualEcu { Address = 0x68, Ident = "9876543 RAD" }]);
        await using var backend = new NativeBackend(car, new NativeBackendOptions { Mode = CableMode.DCan });
        var found = await backend.DiscoverAsync(addresses);
        var m = Assert.Single(found);
        Assert.Equal(0x68, m.Address);
        Assert.Equal("9876543", m.Identity.PartNumber);
    }

    [Fact]
    public void ResearchedCodesAreSourcedAndEngineAware()
    {
        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "bmw-codes.json")));
        var count = 0;
        foreach (var module in json.RootElement.GetProperty("modules").EnumerateObject())
        {
            Assert.Contains(Db.Modules, m => m.Id == module.Name);
            foreach (var code in module.Value.EnumerateObject())
            {
                count++;
                Assert.Matches("^[0-9A-F]{4}$", code.Name);
                Assert.True(code.Value.GetProperty("sources").GetArrayLength() > 0, $"{module.Name} {code.Name} has no source");
                Assert.Contains(code.Value.GetProperty("confidence").GetString(), new[] { "high", "medium" });
            }
        }
        Assert.True(count >= 150, $"only {count} researched codes");

        // N54-only meaning read on an N52 car comes with a warning; on an N54 it doesn't.
        Assert.Contains("may differ on your N52", Db.LookupBmw("E9x.DME", "30FF", "N52")!.Text);
        Assert.DoesNotContain("may differ", Db.LookupBmw("E9x.DME", "30FF", "N54")!.Text);
        var misfire = Db.LookupBmw("E9x.DME", "29CF", "N52")!;
        Assert.Equal("high", misfire.Confidence);
        Assert.Equal("cyl3", misfire.Component);
        Assert.NotEmpty(misfire.Sources!);
    }

    [Fact]
    public void FullGenericObdSetIsLoaded()
    {
        Assert.Contains("Lost Communication", Db.DescribeObd("U0100"));
        Assert.StartsWith("System too lean, bank 1", Db.DescribeObd("P0171"));   // our own entry wins
        Assert.DoesNotContain("manufacturer-specific", Db.DescribeObd("P0442"));
    }

    [Fact]
    public async Task BroadcastFindsFittedModulesAndScanStillWorksWithoutIt()
    {
        var model = Db.FindModel("E9x-325i")!;
        var family = Db.FamilyOf(model)!;
        foreach (var broadcast in new[] { true, false })
        {
            var car = VirtualCar.ForModel(model, family.Modules);
            car.AnswersFunctional = broadcast;
            await using var backend = new NativeBackend(car, new NativeBackendOptions { Mode = CableMode.DCan, ProbeTimeout = TimeSpan.FromMilliseconds(80) });
            await backend.ConnectAsync();
            var present = await backend.ProbePresentAsync();
            if (broadcast) Assert.Equal(car.Ecus.Select(e => e.Address).Order(), present!.Order());
            else Assert.Null(present);
            var scan = await new VehicleScanner(backend, Db).ScanAsync(family.Modules, model.Id);
            Assert.Equal(model.ModuleIds.Count, scan.RespondingEcus);
        }
    }

    [Theory]
    [MemberData(nameof(AllModels))]
    public async Task EveryModelHasAWorkingDemoCar(string modelId)
    {
        var model = Db.FindModel(modelId)!;
        var family = Db.FamilyOf(model)!;
        var mode = family.Cable == "KLine" ? CableMode.KLine : CableMode.DCan;
        await using var backend = new NativeBackend(VirtualCar.ForModel(model, family.Modules),
            new NativeBackendOptions { Mode = mode, ProbeTimeout = TimeSpan.FromMilliseconds(60) });
        await backend.ConnectAsync();
        var codes = Db.ForDemo(model);
        var scan = await new VehicleScanner(backend, codes).ScanAsync(family.Modules, model.Id);

        // Exactly the fitted modules answer.
        var answered = scan.Ecus.Where(e => e.Status is EcuScanStatus.Ok or EcuScanStatus.Faults).Select(e => e.Ecu.Id).Order().ToList();
        Assert.Equal(model.ModuleIds.Order().ToList(), answered);

        // Every demo fault the native backend can read shows up, described and locatable.
        var found = scan.Ecus.SelectMany(e => e.Faults.Select(f => (e.Ecu, f))).ToList();
        foreach (var demo in model.DemoFaults.Where(d => d.IsObd || mode == CableMode.DCan))
        {
            var (ecu, fault) = Assert.Single(found, x => x.Ecu.Id == demo.ModuleId && x.f.Code == demo.Code);
            Assert.False(string.IsNullOrWhiteSpace(fault.Description));
            if (!demo.IsObd) Assert.Equal(demo.Present ? FaultState.Present : FaultState.Stored, fault.State);
            Assert.NotNull(Db.Locate(fault, ecu, model));
        }
    }
}
