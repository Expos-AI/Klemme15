using Klemme15.Core;
using Xunit;
using MF = Klemme15.Core.NoStartTriage.ModuleFault;

namespace Klemme15.Tests;

public class NoStartTriageTests
{
    private static Fault F(string code, string text, FaultState state = FaultState.Present) =>
        new() { EcuId = "x", Code = code, Source = FaultSource.Bmw, State = state, Description = text };

    [Fact]
    public void Classifies_start_chain_faults_into_buckets()
    {
        Assert.Equal(NoStartCause.ElectricSteeringLock, NoStartTriage.Classify(F("A0C8", "Electric steering lock: not unlocked")));
        Assert.Equal(NoStartCause.Immobiliser, NoStartTriage.Classify(F("A0A9", "Drive-away protection (EWS) no release")));
        Assert.Equal(NoStartCause.Starter, NoStartTriage.Classify(F("4B01", "Terminal 50 starter control, open circuit")));
        Assert.Equal(NoStartCause.Sensor, NoStartTriage.Classify(F("2A30", "Crankshaft position sensor, no signal")));
        Assert.Equal(NoStartCause.Fuel, NoStartTriage.Classify(F("29E0", "Low-pressure fuel pump (EKPS)")));
        Assert.Equal(NoStartCause.Ignition, NoStartTriage.Classify(F("29C1", "Ignition coil cylinder 1")));
        Assert.Equal(NoStartCause.Other, NoStartTriage.Classify(F("A3B2", "Something unrelated")));
    }

    [Fact]
    public void Low_battery_leads_and_is_flagged_as_the_problem()
    {
        var v = NoStartTriage.Assess(10.8, false, []);
        Assert.Equal(TestSeverity.Problem, v[0].Severity);
        Assert.Contains("Battery", v[0].Title);
    }

    [Fact]
    public void A_cranking_engine_points_at_spark_or_fuel_not_the_starter()
    {
        var v = NoStartTriage.Assess(12.4, cranked: true, [new MF("DME", F("29E0", "Fuel pump, low pressure"))]);
        Assert.Contains(v, f => f.Title.Contains("turning over"));
        Assert.Contains(v, f => f.Title.Contains("fuel-supply", StringComparison.OrdinalIgnoreCase));
        // No crank was fine, so no starter/steering-lock headline.
        Assert.DoesNotContain(v, f => f.Title.Contains("starter", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void No_crank_with_a_steering_lock_fault_routes_to_the_lock()
    {
        var v = NoStartTriage.Assess(12.6, cranked: false, [new MF("CAS", F("A0C8", "Electric steering lock did not release"))]);
        var lock_ = v.First(f => f.Title.Contains("steering lock"));
        Assert.Contains("clear the fault counters", lock_.Explanation);
    }

    [Fact]
    public void An_immobiliser_fault_points_to_a_shop_and_never_claims_a_fix()
    {
        var v = NoStartTriage.Assess(12.6, cranked: false, [new MF("CAS", F("A0A9", "Engine start enable (EWS) withheld"))]);
        var imm = v.First(f => f.Title.Contains("immobiliser", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("locksmith", imm.Explanation);
        Assert.Contains("does not touch", imm.Explanation);
    }

    [Fact]
    public void No_faults_at_all_still_gives_guidance()
    {
        var v = NoStartTriage.Assess(12.5, cranked: true, []);
        Assert.Contains(v, f => f.Title.Contains("No start-chain faults"));
    }

    [Fact]
    public void Info_memory_entries_are_ignored()
    {
        var info = new Fault { EcuId = "x", Code = "1234", Source = FaultSource.Bmw, State = FaultState.Stored, IsInfo = true, Description = "Crankshaft sensor info" };
        var v = NoStartTriage.Assess(12.6, null, [new MF("DME", info)]);
        Assert.Contains(v, f => f.Title.Contains("No start-chain faults"));
    }
}
