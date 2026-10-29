using Klemme15.Core;
using Klemme15.Data;
using Xunit;

namespace Klemme15.Tests;

public class FrmGuardTests
{
    private static readonly EcuDefinition Frm = new() { Id = "E9x.FRM", Name = "Footwell module (FRM)", Address = 0x72, Protocol = BusProtocol.BmwFast };
    private static readonly EcuDefinition Kombi = new() { Id = "E9x.KOMBI", Name = "Instrument cluster", Address = 0x60, Protocol = BusProtocol.BmwFast };

    [Fact]
    public void Classifies_footwell_modules_by_part_number_and_variant()
    {
        Assert.Equal(FrmGeneration.Frm1, FrmGuard.Classify("6961130", "frm_87"));
        Assert.Equal(FrmGeneration.Frm1, FrmGuard.Classify("61.35-6 987 998", null));
        Assert.Equal(FrmGeneration.Frm2, FrmGuard.Classify("9166711", "frm_70"));
        Assert.Equal(FrmGeneration.Frm3, FrmGuard.Classify("9224596", "frm_70"));
        Assert.Equal(FrmGeneration.Frm3, FrmGuard.Classify("61359241002", null));
        Assert.Equal(FrmGeneration.Frm3, FrmGuard.Classify("8886836", null));
        Assert.Equal(FrmGeneration.Frm3, FrmGuard.Classify(null, "FRM3_E70"));
        // FRM_70 without a known part number could be an FRM2 or an FRM3: unknown, so treated as fragile.
        Assert.Equal(FrmGeneration.Unknown, FrmGuard.Classify(null, "frm_70"));
        Assert.True(FrmGuard.Assess(null, "frm_70").Fragile);
        Assert.False(FrmGuard.Assess("6961130", null).Fragile);
    }

    [Fact]
    public void A_fragile_footwell_module_needs_charger_voltage_for_coding_and_service()
    {
        var frm3 = new EcuIdentity { PartNumber = "9224596", Variant = "frm_70" };
        Assert.False(WriteGuard.Decide(Frm, frm3, WriteKind.Coding, 12.6).Allowed);
        Assert.Contains("charger", WriteGuard.Decide(Frm, frm3, WriteKind.ServiceFunction, 12.6).Reason);
        Assert.True(WriteGuard.Decide(Frm, frm3, WriteKind.Coding, 13.6).Allowed);
        // Clearing its faults needs a healthy battery, not a charger.
        Assert.True(WriteGuard.Decide(Frm, frm3, WriteKind.ClearFaults, 12.6).Allowed);
        Assert.False(WriteGuard.Decide(Frm, frm3, WriteKind.ClearFaults, 12.1).Allowed);
        // Coding is never done without a voltage reading.
        Assert.False(WriteGuard.Decide(Frm, frm3, WriteKind.Coding, null).Allowed);
    }

    [Fact]
    public void Other_modules_keep_the_normal_rules()
    {
        var frm1 = new EcuIdentity { PartNumber = "6961130" };
        Assert.True(WriteGuard.Decide(Frm, frm1, WriteKind.ClearFaults, 11.8).Allowed);
        Assert.True(WriteGuard.Decide(Frm, frm1, WriteKind.Coding, 12.6).Allowed);
        Assert.True(WriteGuard.Decide(Kombi, null, WriteKind.ClearFaults, 11.5).Allowed);
        Assert.False(WriteGuard.Decide(Kombi, null, WriteKind.Coding, 12.2).Allowed);
        Assert.True(WriteGuard.Decide(Kombi, null, WriteKind.Coding, 12.7).Allowed);
    }

    [Fact]
    public void The_e60_family_has_a_body_module_not_a_footwell_module()
    {
        var db = VehicleDatabase.Load(Path.Combine(Path.GetTempPath(), "klemme15-no-overrides"));
        var e60 = db.FindVehicle("E60")!;
        Assert.DoesNotContain(e60.Modules, m => FrmGuard.IsFootwellModule(m));
        Assert.Contains(db.FindVehicle("E9x")!.Modules, m => FrmGuard.IsFootwellModule(m));
    }
}
