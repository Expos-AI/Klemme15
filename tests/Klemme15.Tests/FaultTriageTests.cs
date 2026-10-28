using Klemme15.Core;
using Xunit;

namespace Klemme15.Tests;

public class FaultTriageTests
{
    private static Fault F(FaultState state, string text, bool info = false, bool shadow = false, int count = 0) => new()
    {
        EcuId = "E9x.DME", Code = "2A82", Source = FaultSource.Bmw, State = state, Description = text, IsInfo = info || shadow, IsShadow = shadow,
        Details = count > 0 ? new Dictionary<string, string> { ["Occurrences"] = count.ToString() } : new Dictionary<string, string>(),
    };

    [Fact]
    public void Active_faults_need_fixing_now() =>
        Assert.Equal(FaultPriority.FixNow, FaultTriage.Assess(F(FaultState.Present, "Intake VANOS: position not reached")).Priority);

    [Fact]
    public void Info_and_shadow_memory_are_background()
    {
        Assert.Equal(FaultPriority.Background, FaultTriage.Assess(F(FaultState.Present, "Fuel cap", info: true)).Priority);
        Assert.Equal(FaultPriority.Background, FaultTriage.Assess(F(FaultState.Stored, "Exhaust flap", shadow: true)).Priority);
    }

    [Fact]
    public void Stored_leftovers_of_battery_work_are_background_unless_frequent()
    {
        Assert.Equal(FaultPriority.Background, FaultTriage.Assess(F(FaultState.Stored, "Supply voltage too low (Unterspannung)")).Priority);
        Assert.Equal(FaultPriority.Background, FaultTriage.Assess(F(FaultState.Stored, "PT-CAN message from DSC missing")).Priority);
        Assert.Equal(FaultPriority.CheckIt, FaultTriage.Assess(F(FaultState.Stored, "Undervoltage detected", count: 25)).Priority);
    }

    [Fact]
    public void Other_stored_faults_are_worth_checking() =>
        Assert.Equal(FaultPriority.CheckIt, FaultTriage.Assess(F(FaultState.Stored, "Oxygen sensor heater, bank 1")).Priority);

    [Fact]
    public void Active_supply_faults_point_at_the_battery()
    {
        var v = FaultTriage.Assess(F(FaultState.Present, "Battery voltage too low"));
        Assert.Equal(FaultPriority.FixNow, v.Priority);
        Assert.Contains("battery", v.Advice);
    }
}
