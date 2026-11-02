using Klemme15.Core;
using Klemme15.Core.GhostHunter;
using Xunit;

namespace Klemme15.Tests;

public class GhostHunterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static DatalogFrame F(double seconds, double rpm, double voltage = 14.0) =>
        new(T0.AddSeconds(seconds), new Dictionary<string, double> { ["rpm"] = rpm, ["voltage"] = voltage });

    private static TriggerRule DipRule(double hold = 1, double before = 60, double after = 60, int max = 5, double cooldown = 300) => new()
    {
        Id = "dip", Title = "Voltage dip",
        Conditions = [new TriggerCondition { Parameter = "voltage", Operator = TriggerOperator.Below, Threshold = 11.5, HoldDuration = TimeSpan.FromSeconds(hold) }],
        Before = TimeSpan.FromSeconds(before), After = TimeSpan.FromSeconds(after), MaxCaptures = max, Cooldown = TimeSpan.FromSeconds(cooldown),
    };

    private static async Task<List<TriggerCapture>> Run(TriggerRule rule, IEnumerable<DatalogFrame> frames)
    {
        var captures = new List<TriggerCapture>();
        var monitor = new TriggerMonitor([rule], (c, _) => { captures.Add(c); return Task.CompletedTask; });
        var run = monitor.RunAsync(CancellationToken.None);
        foreach (var f in frames) await monitor.Input.WriteAsync(f);
        monitor.Input.Complete();
        await run;
        return captures;
    }

    [Fact]
    public void RingBufferKeepsOnlyTheWindowAndNeverGrows()
    {
        var ring = new LiveRingBuffer<DatalogFrame>(TimeSpan.FromSeconds(60), 100, f => f.Time);
        for (var i = 0; i < 1000; i++) ring.Add(F(i * 0.2, 800));
        var all = ring.Snapshot();
        Assert.True(all.Count <= 100);
        Assert.InRange((all[^1].Time - all[0].Time).TotalSeconds, 0, 60);
        Assert.True(all.Zip(all.Skip(1)).All(p => p.First.Time < p.Second.Time));

        var timed = new LiveRingBuffer<DatalogFrame>(TimeSpan.FromSeconds(60), 10_000, f => f.Time);
        for (var i = 0; i < 1000; i++) timed.Add(F(i * 0.5, 800));
        var w = timed.Snapshot();
        Assert.InRange((w[^1].Time - w[0].Time).TotalSeconds, 60 - 0.5, 60 + 0.5);
    }

    [Fact]
    public async Task RingBufferIsSafeUnderConcurrentUse()
    {
        var ring = new LiveRingBuffer<DatalogFrame>(TimeSpan.FromSeconds(60), 500, f => f.Time);
        var writer = Task.Run(() => { for (var i = 0; i < 20_000; i++) ring.Add(F(i * 0.01, i)); });
        var reader = Task.Run(() =>
        {
            for (var i = 0; i < 2_000; i++)
            {
                var s = ring.Snapshot();
                for (var k = 1; k < s.Count; k++) Assert.True(s[k - 1].Time < s[k].Time);
            }
        });
        await Task.WhenAll(writer, reader);
        Assert.Equal(500, ring.Count);
    }

    [Fact]
    public async Task FiresOnlyWhenTheConditionHoldsAndCapturesBeforeAndAfter()
    {
        // 2 minutes of driving at 5 Hz; a 0.4 s blip at 30 s (too short), a real dip from 90 s to 93 s.
        var frames = Enumerable.Range(0, 5 * 240).Select(i => i * 0.2).Select(t =>
            F(t, 2500, t is >= 30 and < 30.4 || t is >= 90 and < 93 ? 10.8 : 14.1)).ToList();
        var captures = await Run(DipRule(hold: 1), frames);
        var c = Assert.Single(captures);
        Assert.InRange((c.Event.At - T0).TotalSeconds, 91 - 0.5, 91 + 0.5);              // fired after holding 1 s
        Assert.False(c.Event.Partial);
        Assert.InRange((c.Event.At - c.Frames[0].Time).TotalSeconds, 60 - 0.5, 60 + 0.5); // a minute before
        Assert.InRange((c.Frames[^1].Time - c.Event.At).TotalSeconds, 60 - 0.5, 60 + 0.5); // a minute after
        Assert.Equal(10.8, c.Event.ValuesAtTrigger["voltage"]);
    }

    [Fact]
    public async Task CooldownAndMaxCapturesStopABurstOfFiles()
    {
        // Voltage low the whole time: with a 10 s after-window and 20 s cooldown, captures are spaced out and capped.
        var frames = Enumerable.Range(0, 5 * 300).Select(i => F(i * 0.2, 800, 11.0)).ToList();
        var captures = await Run(DipRule(hold: 0, before: 5, after: 10, max: 3, cooldown: 20), frames);
        Assert.Equal(3, captures.Count);
        Assert.True(captures.Zip(captures.Skip(1)).All(p => (p.Second.Event.At - p.First.Event.At).TotalSeconds >= 30));
    }

    [Fact]
    public async Task StoppingEarlyKeepsWhatWasCaught()
    {
        var frames = Enumerable.Range(0, 5 * 40).Select(i => i * 0.2).Select(t => F(t, 2500, t > 20 ? 10.5 : 14.1)).ToList();
        var captures = await Run(DipRule(hold: 0.5), frames);
        var c = Assert.Single(captures);
        Assert.True(c.Event.Partial);
        Assert.InRange(c.Length.TotalSeconds, 15, 40);
    }

    [Fact]
    public async Task ACaptureBecomesASnapshotThatOpensWithTheTrigger()
    {
        var frames = Enumerable.Range(0, 5 * 30).Select(i => i * 0.2).Select(t => F(t, 3000, t is >= 10 and < 12 ? 10.9 : 14.0)).ToList();
        var capture = (await Run(DipRule(hold: 0.5, before: 10, after: 5), frames)).Single();
        var baseSnapshot = new CarSnapshot
        {
            Manifest = new SnapshotManifest { Created = T0, ModelName = "325i" },
            Scan = new VehicleScanResult { Started = T0, Backend = "test", Ecus = [] },
        };
        var opened = SnapshotFile.Read(SnapshotFile.Write(capture.ToSnapshot(baseSnapshot)));
        Assert.Equal("dip", opened.Manifest.Trigger!.RuleId);
        Assert.Contains("Ghost Hunter caught", opened.Manifest.Note);
        var log = DataLog.Parse(opened.LiveCsv);
        Assert.Equal("Engine speed", log.Channels[0].Name);
        Assert.Contains(log.Channels, ch => ch.Name == "Battery voltage" && ch.Unit == "V");
        Assert.InRange(log.Duration.TotalSeconds, 14, 16);
    }

    [Fact]
    public void RulesValidateAgainstWhatTheCarReports()
    {
        Assert.Empty(DipRule().Validate(new HashSet<string> { "voltage", "rpm" }));
        Assert.Contains(DipRule().Validate(new HashSet<string> { "rpm" }), p => p.Contains("doesn't report voltage"));
        Assert.All(TriggerPresets.All, r => Assert.Empty(r.Validate()));
    }
}
