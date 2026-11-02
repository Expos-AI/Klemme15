using Klemme15.Core;
using Klemme15.Data;
using Xunit;

namespace Klemme15.Tests;

public class ChainTests
{
    private static readonly VehicleDatabase Db = VehicleDatabase.Load(Path.Combine(Path.GetTempPath(), "klemme15-no-overrides"));
    private static DiagnosticChain Chain(string id) => Db.Chains.Single(c => c.Id == id);

    /// <summary>A car whose values depend on how many readings have been taken (idle first, then the "driver" revs it).</summary>
    private sealed class Car(Func<int, Dictionary<string, double>> at) : ISignalSource
    {
        private int _n;
        public Task<IReadOnlyDictionary<string, double>> ReadAsync(IReadOnlyCollection<string> signals, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, double>>(at(_n++).Where(kv => signals.Contains(kv.Key)).ToDictionary());
    }

    private sealed class Answer(string value) : IChainPrompt
    {
        public string? Asked;
        public Task<string> AskAsync(string question, IReadOnlyList<ChainChoice> choices, CancellationToken ct) { Asked = question; return Task.FromResult(value); }
    }

    [Fact]
    public void ShippedChainsAllValidate()
    {
        Assert.True(Db.Chains.Count >= 2);
        Assert.DoesNotContain(Db.LoadWarnings, w => w.StartsWith("chains.json"));
        Assert.Contains(Db.ChainsFor("29E0"), c => c.Id == "vacuum-leak-hunter");
    }

    [Fact]
    public void ExpressionsAreSafeArithmeticOnly()
    {
        var vars = new Dictionary<string, double> { ["a.x"] = 12, ["b"] = 3 };
        Assert.True(ChainExpression.Parse("a.x > 10 and b < 4").IsTrue(vars));
        Assert.Equal(15, ChainExpression.Parse("a.x + b").Evaluate(vars));
        Assert.Equal(12, ChainExpression.Parse("max(a.x, missing)").Evaluate(vars));
        Assert.False(ChainExpression.Parse("missing > 0").IsTrue(vars));
        Assert.True(ChainExpression.Parse("not (b > 5)").IsTrue(vars));
        Assert.Throws<ChainExpressionException>(() => ChainExpression.Parse("System.IO.File.Delete(x)"));
        Assert.Throws<ChainExpressionException>(() => ChainExpression.Parse("a.x >"));
        Assert.Throws<ChainExpressionException>(() => ChainExpression.Parse("exec(1)"));
    }

    [Fact]
    public void BrokenChainsAreRefusedWithAReason()
    {
        var (chains, problems) = ChainLoader.Load("""
            { "chains": [ { "id": "bad", "title": "t", "summary": "s",
              "steps": [ { "type": "launch-rockets" } ],
              "verdicts": [ { "when": "nothing.measured > 1", "title": "x", "text": "y" } ] } ] }
            """);
        Assert.Empty(chains);
        Assert.Contains(problems, p => p.Contains("unknown type"));
        Assert.Contains(problems, p => p.Contains("isn't measured"));
    }

    [Fact]
    public async Task VacuumLeakIsFoundAndTheCrankcaseValveNamed()
    {
        // Warm, lean at idle (+15 %), fine at 2,500 rpm (+2 %).
        var car = new Car(n => n < 25
            ? new() { ["rpm"] = 750, ["coolant"] = 90, ["stft1"] = 5, ["ltft1"] = 10, ["maf"] = 3.4 }
            : new() { ["rpm"] = 2500, ["coolant"] = 90, ["stft1"] = 1, ["ltft1"] = 1, ["maf"] = 12 });
        var prompt = new Answer("suction");
        var report = await ChainRunner.RunAsync(Chain("vacuum-leak-hunter"), car, prompt, null, CancellationToken.None, speed: 1000, displacement: 3.0);
        Assert.NotNull(prompt.Asked);
        Assert.Equal("Points to the crankcase vent valve", report.Findings[0].Title);
        Assert.Contains("+15.0 %", report.Findings[0].Explanation);
        Assert.Contains(report.Traces, t => t.Name == "Bank 1 total trim");
    }

    [Fact]
    public async Task UnderReadingAirMassMeterIsFlagged()
    {
        var car = new Car(n => n < 25
            ? new() { ["rpm"] = 750, ["coolant"] = 90, ["stft1"] = 6, ["ltft1"] = 8, ["maf"] = 1.2 }
            : new() { ["rpm"] = 2500, ["coolant"] = 90, ["stft1"] = 5, ["ltft1"] = 8, ["maf"] = 6 });
        var report = await ChainRunner.RunAsync(Chain("vacuum-leak-hunter"), car, new Answer("normal"), null, CancellationToken.None, speed: 1000, displacement: 3.0);
        Assert.Contains(report.Findings, f => f.Title == "The air mass reading looks low");
        Assert.Contains(report.Findings, f => f.Title == "Lean everywhere: fuel delivery or air mass meter");
    }

    [Fact]
    public async Task ColdEngineStopsTheChain()
    {
        var car = new Car(_ => new() { ["rpm"] = 750, ["coolant"] = 30 });
        var report = await ChainRunner.RunAsync(Chain("vacuum-leak-hunter"), car, new Answer("skip"), null, CancellationToken.None, speed: 1000);
        Assert.Equal(TestSeverity.Unknown, report.Verdict);
        Assert.StartsWith("Warm the engine up first", report.Findings[0].Title);
    }

    [Fact]
    public async Task WeakAlternatorIsCaught()
    {
        // Rest 12.5 V; idle 13.9 V; under load it sags to 13.1 V.
        var car = new Car(n => n < 7 ? new() { ["rpm"] = 0, ["voltage"] = 12.5 }
            : n < 23 ? new() { ["rpm"] = 760, ["voltage"] = 13.9 }
            : new() { ["rpm"] = 2000, ["voltage"] = 13.1 });
        var report = await ChainRunner.RunAsync(Chain("charging-check"), car, null, null, CancellationToken.None, speed: 1000);
        Assert.Equal("The alternator can't keep up under load", report.Findings[0].Title);
        Assert.Equal(TestSeverity.Problem, report.Verdict);
    }

    [Fact]
    public async Task HealthyChargingSaysSo()
    {
        var car = new Car(n => n < 7 ? new() { ["rpm"] = 0, ["voltage"] = 12.6 }
            : n < 23 ? new() { ["rpm"] = 760, ["voltage"] = 14.2 }
            : new() { ["rpm"] = 2000, ["voltage"] = 14.0 });
        var report = await ChainRunner.RunAsync(Chain("charging-check"), car, null, null, CancellationToken.None, speed: 1000);
        Assert.Equal("Charging system healthy", report.Findings[0].Title);
    }
}
