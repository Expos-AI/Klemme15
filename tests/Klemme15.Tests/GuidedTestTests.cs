using Klemme15.Core;
using Xunit;

namespace Klemme15.Tests;

public class GuidedTestTests
{
    [Fact]
    public void Lean_at_idle_only_points_at_a_vacuum_leak()
    {
        var f = GuidedAnalysis.FuelTrims(new TrimSample(15, null), new TrimSample(3, null)).Single();
        Assert.Equal(TestSeverity.Problem, f.Severity);
        Assert.Contains("vacuum leak", f.Title);
    }

    [Fact]
    public void Lean_everywhere_points_at_fuel_delivery() =>
        Assert.Contains("not enough fuel", GuidedAnalysis.FuelTrims(new TrimSample(16, null), new TrimSample(15, null)).Single().Title);

    [Fact]
    public void Rich_at_idle_points_at_purge_valve_or_injector() =>
        Assert.Contains("purge valve", GuidedAnalysis.FuelTrims(new TrimSample(-15, null), new TrimSample(-2, null)).Single().Title);

    [Fact]
    public void Healthy_trims_are_ok() =>
        Assert.All(GuidedAnalysis.FuelTrims(new TrimSample(2, -3), new TrimSample(1, 0)), f => Assert.Equal(TestSeverity.Ok, f.Severity));

    [Fact]
    public void One_bank_lean_is_called_out()
    {
        var findings = GuidedAnalysis.FuelTrims(new TrimSample(14, 1), new TrimSample(3, 0));
        Assert.Contains(findings, f => f.Title.Contains("banks disagree"));
    }

    [Fact]
    public void Vanos_lag_is_graded()
    {
        Assert.Equal(TestSeverity.Ok, GuidedAnalysis.Vanos("Intake", [(90, 91), (100, 99), (110, 110.5)]).Severity);
        Assert.Equal(TestSeverity.Problem, GuidedAnalysis.Vanos("Intake", [(90, 70), (100, 80), (110, 85)]).Severity);
    }

    [Fact]
    public void Rough_cylinder_stands_out()
    {
        var findings = GuidedAnalysis.Roughness([0.4, 0.5, 2.1, 0.45, 0.4, 0.5]);
        Assert.Contains(findings, f => f.Severity == TestSeverity.Problem && f.Title.StartsWith("Cylinder 3"));
        Assert.Equal(TestSeverity.Ok, GuidedAnalysis.Roughness([0.4, 0.5, 0.45, 0.42]).Single().Severity);
    }

    private sealed class Scripted(Func<int, Dictionary<string, double>> at) : ISignalSource
    {
        private int _n;
        public Task<IReadOnlyDictionary<string, double>> ReadAsync(IReadOnlyCollection<string> signals, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, double>>(at(_n++).Where(kv => signals.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    [Fact]
    public async Task Fuel_trim_test_measures_idle_then_2500()
    {
        var car = new Scripted(n => n < 40
            ? new() { ["rpm"] = 750, ["coolant"] = 90, ["stft1"] = 5, ["ltft1"] = 10 }
            : new() { ["rpm"] = 2500, ["coolant"] = 90, ["stft1"] = 1, ["ltft1"] = 2 });
        var report = await GuidedTests.FuelTrimsAsync(car, null, CancellationToken.None, speed: 500);
        Assert.Contains("vacuum leak", report.Findings[0].Title);
        Assert.Equal(15.0, Math.Round(report.Values[0].Value, 1));
    }

    [Fact]
    public async Task Bleed_assistant_waits_for_the_pump_and_times_the_cycle()
    {
        // The pump starts 5 s after the test begins (pedal held) and runs about 11.7 minutes.
        var car = new Scripted(n => new()
        {
            ["rpm"] = 0, ["coolant"] = 25, ["voltage"] = 13.6,
            ["pump_speed"] = n is >= 10 and < 1410 ? 80 : 0,
        });
        var report = await GuidedTests.CoolingBleedAsync(car, null, CancellationToken.None, speed: 1e6);
        Assert.Equal(TestSeverity.Ok, report.Findings[0].Severity);
        Assert.Contains("bleed cycle", report.Findings[0].Title);

        var running = new Scripted(_ => new() { ["rpm"] = 800, ["coolant"] = 30 });
        Assert.Contains("engine off", (await GuidedTests.CoolingBleedAsync(running, null, CancellationToken.None, 1e6)).Findings[0].Title);
        var never = new Scripted(_ => new() { ["rpm"] = 0, ["coolant"] = 20, ["pump_speed"] = 0 });
        Assert.Contains("didn't start", (await GuidedTests.CoolingBleedAsync(never, null, CancellationToken.None, 1e6)).Findings[0].Title);
    }

    [Fact]
    public async Task Tests_refuse_a_cold_or_stopped_engine()
    {
        var cold = new Scripted(_ => new() { ["rpm"] = 800, ["coolant"] = 30 });
        Assert.Contains("Warm", (await GuidedTests.FuelTrimsAsync(cold, null, CancellationToken.None, 500)).Findings[0].Title);
        var off = new Scripted(_ => new() { ["rpm"] = 0, ["coolant"] = 80 });
        Assert.Contains("Start the engine", (await GuidedTests.RoughnessAsync(off, null, CancellationToken.None, 500)).Findings[0].Title);
    }

    [Fact]
    public void Bmw_mixture_factors_become_percent()
    {
        Assert.Equal(5.0, Math.Round(EngineSignals.TrimPercent(1.05), 3));
        Assert.Equal(-3.0, EngineSignals.TrimPercent(-3));
    }
}
