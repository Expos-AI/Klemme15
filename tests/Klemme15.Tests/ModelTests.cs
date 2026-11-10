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
    [InlineData("E9x", "E9x-m3", "S65")]
    [InlineData("E46", "E46-m3", "S54")]
    [InlineData("E60", "E60-m5", "S85")]
    [InlineData("E70", "E70-x5m", "S63")]
    [InlineData("E8x", "E8x-1m", "N54")]
    [InlineData("E9x", "E9x-335d", "M57")]
    public void TheMCarsAndDieselsAreModelsWithTheirOwnEngine(string family, string model, string engine)
    {
        var m = Db.FindVehicle(family)!.Models.Single(x => x.Id == model);
        Assert.Equal(engine, m.EngineCode);
        var dme = Db.FindVehicle(family)!.Modules.Single(x => x.IsEngine);
        Assert.All(m.ModuleIds, id => Assert.Contains(Db.FindVehicle(family)!.Modules, x => x.Id == id));
        Assert.NotEmpty(dme.Variants);
    }

    [Fact]
    public void EngineSpecificTestsAndMeaningsDontApplyToTheWrongEngine()
    {
        var e9x = Db.FindVehicle("E9x")!;
        var m3 = e9x.Models.Single(x => x.Id == "E9x-m3");
        var d = e9x.Models.Single(x => x.Id == "E9x-335d");
        var n52 = e9x.Models.Single(x => x.Id == "E9x-328i");
        var test = (string id) => GuidedTests.All.Single(t => t.Id == id);
        Assert.NotNull(test("water-pump").NotForEngine(m3));
        Assert.NotNull(test("cooling-bleed").NotForEngine(d));
        Assert.NotNull(test("fuel-trims").NotForEngine(d));
        Assert.Null(test("fuel-trims").NotForEngine(m3));
        Assert.All(GuidedTests.All, t => Assert.Null(t.NotForEngine(n52)));
        Assert.All(GuidedTests.All, t => Assert.Null(t.NotForEngine(null)));
        // An N52 misfire meaning on an S65 says it was recorded for other engines.
        Assert.Contains("may differ on your S65", Db.LookupBmw("E9x.DME", "29CD", "S65")!.Text);
        Assert.DoesNotContain("may differ", Db.LookupBmw("E9x.DME", "29CD", "N52")!.Text);
    }

    [Fact]
    public void ResearchedMeaningsAreSharedOnlyWithTheSameModuleVersion()
    {
        EcuDefinition M(string id) => Db.Modules.Single(m => m.Id == id);
        // The 1 Series, X1 and Z4 use the E90 family's footwell module and cluster.
        var frm = Db.LookupShared(M("E8x.FRM"), null, "A8C5", null);
        Assert.NotNull(frm);
        Assert.Contains(VehicleDatabase.SharedNote, frm!.Hint!);
        Assert.NotNull(Db.LookupShared(M("E84.KOMBI"), "komb87", FirstCode("E9x.KOMBI"), null));
        // The 5 Series cluster is a different version: nothing is borrowed, known version or not.
        Assert.Null(Db.LookupShared(M("E60.KOMBI"), null, FirstCode("E9x.KOMBI"), null));
        Assert.Null(Db.LookupShared(M("E60.KOMBI"), "komb60", FirstCode("E9x.KOMBI"), null));
        // The X5's key module is the same one; its stability control isn't.
        Assert.NotNull(Db.LookupShared(M("E70.CAS"), "cas", "A0A9", null));
        Assert.Null(Db.LookupShared(M("E70.DSC"), null, FirstCode("E9x.DSC"), null));
        // Engine codes only for an engine the meaning was researched on.
        Assert.NotNull(Db.LookupShared(M("E8x.DME"), "msd80", "29CD", "N54"));
        Assert.Null(Db.LookupShared(M("E8x.DME"), "d70n47a0", "29CD", "N47"));
        Assert.Null(Db.LookupShared(M("E9x.FRM"), null, "A8C5", null));
        // K-line families share nothing with the E90's D-CAN modules.
        Assert.Null(Db.LookupShared(M("E46.DME"), null, "29CD", "M54"));
    }

    private static string FirstCode(string module)
    {
        foreach (var code in new[] { "9315", "5D90", "A0A9", "A8C5" })
            if (Db.LookupBmw(module, code) is not null) return code;
        throw new InvalidOperationException($"no researched code found for {module}");
    }

    [Fact]
    public void TheE36IsABetaKLineCarWhoseModulesAreAllUnconfirmed()
    {
        var e36 = Db.FindVehicle("E36")!;
        Assert.True(e36.IsBeta);
        Assert.Equal("KLine", e36.Cable);
        Assert.Contains("1996", e36.Note!);
        var engine = e36.Modules.Single(m => m.IsEngine);
        Assert.Equal(0x12, engine.Address);
        Assert.StartsWith("ms41", engine.Variants[0]);
        Assert.All(e36.Modules, m => Assert.False(m.Verified));
        Assert.All(Db.Vehicles.Where(v => v.Id is not ("E36" or "E83")), v => Assert.False(v.IsBeta));
        Assert.True(Db.FindVehicle("E83")!.IsBeta);
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
    public void CasSteeringLockCodeIsDescribedAndPlaced()
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

        // Hand-checked advice: the junction box relay code is flagged as usually harmless and explains the radio-swap case.
        var relay = Db.LookupBmw("E9x.JBE", "A731", "N52")!;
        Assert.True(relay.UsuallyHarmless);
        Assert.Contains("aftermarket radio", relay.Hint);
        Assert.False(Db.LookupBmw("E9x.DSC", "5E40", "N52")!.UsuallyHarmless);
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
