using Klemme15.Core;
using Xunit;

namespace Klemme15.Tests;

public class ComponentTestTests
{
    private sealed class FakeSignals(Func<int, Dictionary<string, double>> at) : ISignalSource
    {
        public int Reads;
        public Task<IReadOnlyDictionary<string, double>> ReadAsync(IReadOnlyCollection<string> signals, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, double>>(at(Reads++).Where(kv => signals.Contains(kv.Key)).ToDictionary());
    }

    private sealed class FakeActuator(Action<double?> set) : IActuator
    {
        public int Stops;
        public Task SetAsync(double fraction, int seconds, CancellationToken ct) { set(fraction); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) { Stops++; set(null); return Task.CompletedTask; }
    }

    [Fact]
    public void PumpTrackingVerdicts()
    {
        static List<(double, double)> Pairs(double ratio) => Enumerable.Range(0, 20).Select(i => (50.0, 50 * ratio)).ToList();
        Assert.Equal(TestSeverity.Ok, PumpAnalysis.Tracking(Pairs(0.95)).Severity);
        Assert.Equal(TestSeverity.Watch, PumpAnalysis.Tracking(Pairs(0.78)).Severity);
        Assert.Equal(TestSeverity.Problem, PumpAnalysis.Tracking(Pairs(0.5)).Severity);
        Assert.Equal("The water pump isn't turning", PumpAnalysis.Tracking(Pairs(0.02)).Title);
        Assert.Equal(TestSeverity.Unknown, PumpAnalysis.Tracking([(0, 0), (2, 0)]).Severity);
    }

    [Fact]
    public void SweepVerdicts()
    {
        Assert.Equal(TestSeverity.Ok, PumpAnalysis.Sweep([(0.3, 28), (0.6, 55), (0.9, 80)]).Severity);
        Assert.Equal(TestSeverity.Watch, PumpAnalysis.Sweep([(0.3, 28), (0.6, 55), (0.9, 54)]).Severity);
        Assert.Equal(TestSeverity.Problem, PumpAnalysis.Sweep([(0.3, 0), (0.6, 0.2), (0.9, 0)]).Severity);
    }

    [Fact]
    public async Task WaterPumpTestGraphsRequestedAndActual()
    {
        var signals = new FakeSignals(i => new() { ["rpm"] = i is > 24 and < 56 ? 3000 : 750, ["coolant"] = 90, ["pump_target"] = 40, ["pump_speed"] = 38 });
        var report = await GuidedTests.RunAsync("water-pump", signals, null, CancellationToken.None, speed: 1000);
        Assert.Equal(TestSeverity.Ok, report.Verdict);
        Assert.Contains(report.Traces, t => t.Name == "Pump requested" && t.Target);
        Assert.Contains(report.Traces, t => t.Name == "Pump actual" && t.Values.Count > 50);
        Assert.Contains(report.Traces, t => t.Unit == "rpm");
    }

    [Fact]
    public async Task SweepNeedsAnActuatorAndAlwaysStopsThePump()
    {
        var signals0 = new FakeSignals(_ => new() { ["rpm"] = 0 });
        var none = await GuidedTests.RunAsync("water-pump-sweep", signals0, null, CancellationToken.None, speed: 1000);
        Assert.Equal(TestSeverity.Unknown, none.Verdict);

        double? cmd = null;
        var signals = new FakeSignals(_ => new() { ["rpm"] = 0, ["pump_speed"] = (cmd ?? 0) * 90 });
        var actuator = new FakeActuator(c => cmd = c);
        var report = await GuidedTests.RunAsync("water-pump-sweep", signals, null, CancellationToken.None, speed: 1000, actuator: actuator);
        Assert.Equal(TestSeverity.Ok, report.Verdict);
        Assert.Equal(1, actuator.Stops);
        Assert.Null(cmd);

        // Stopped half way: the pump is still switched off.
        using var cts = new CancellationTokenSource();
        var stopping = new FakeSignals(i => { if (i == 10) cts.Cancel(); return new() { ["rpm"] = 0, ["pump_speed"] = 20 }; });
        var actuator2 = new FakeActuator(_ => { });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GuidedTests.RunAsync("water-pump-sweep", stopping, null, cts.Token, speed: 1000, actuator: actuator2));
        Assert.Equal(1, actuator2.Stops);

        // Never with the engine running.
        var running = await GuidedTests.RunAsync("water-pump-sweep", new FakeSignals(_ => new() { ["rpm"] = 800 }), null, CancellationToken.None, speed: 1000, actuator: new FakeActuator(_ => { }));
        Assert.Equal("Switch the engine off first", running.Findings[0].Title);
    }

    [Fact]
    public async Task VanosTestRecordsTargetAndActual()
    {
        var signals = new FakeSignals(_ => new() { ["rpm"] = 800, ["coolant"] = 90, ["vanos_in_target"] = 100, ["vanos_in_actual"] = 99 });
        var report = await GuidedTests.RunAsync("vanos", signals, null, CancellationToken.None, speed: 1000);
        Assert.Contains(report.Traces, t => t.Name == "Intake target" && t.Target);
        Assert.Contains(report.Traces, t => t.Name == "Intake actual" && !t.Target);
    }

    [Fact]
    public void TestResultsBecomeTrends()
    {
        var info = GuidedTests.All.First(t => t.Id == "water-pump");
        TestRecord Run(int day, TestSeverity s) => TestRecord.From(info, new TestReport([new TestFinding(s, s.ToString(), "")], [new("x", 1)]), new DateTimeOffset(2026, 10, day, 0, 0, 0, TimeSpan.Zero));
        var worse = HealthTrends.AnalyzeTests([Run(1, TestSeverity.Ok), Run(9, TestSeverity.Problem)]);
        Assert.Single(worse);
        Assert.Equal(TrendLevel.Act, worse[0].Level);
        Assert.True(worse[0].Title.EndsWith("getting worse"));
        Assert.Empty(HealthTrends.AnalyzeTests([Run(1, TestSeverity.Ok), Run(9, TestSeverity.Ok)]));
        Assert.Equal(TrendLevel.Watch, HealthTrends.AnalyzeTests([Run(1, TestSeverity.Watch)])[0].Level);
    }

    [Fact]
    public void TestRecordsAreStoredApartFromSnapshots()
    {
        var dir = Path.Combine(Path.GetTempPath(), "k15-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new HealthHistoryStore(dir);
            var info = GuidedTests.All[0];
            store.AddTest("car1", TestRecord.From(info, new TestReport([new TestFinding(TestSeverity.Ok, "fine", "")], []), DateTimeOffset.Now));
            Assert.Single(store.LoadTests("car1"));
            Assert.Empty(store.Cars());
            store.Forget("car1");
            Assert.Empty(store.LoadTests("car1"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
