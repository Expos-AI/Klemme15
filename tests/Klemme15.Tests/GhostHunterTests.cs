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

    private sealed class FakeSource(Dictionary<string, double> values) : ISignalSource
    {
        public List<string> Asked { get; } = [];
        public Task<IReadOnlyDictionary<string, double>> ReadAsync(IReadOnlyCollection<string> signals, CancellationToken ct = default)
        {
            Asked.AddRange(signals);
            return Task.FromResult<IReadOnlyDictionary<string, double>>(signals.Where(values.ContainsKey).ToDictionary(s => s, s => values[s]));
        }
    }

    [Fact]
    public async Task Derived_values_name_the_rough_cylinder_and_the_leaner_bank()
    {
        var car = new FakeSource(new()
        {
            ["rpm"] = 2000, ["stft1"] = 4, ["stft2"] = 17,
            ["rough1"] = 1.0, ["rough2"] = 1.2, ["rough3"] = 4.5, ["rough4"] = 0.9, ["rough5"] = 1.1, ["rough6"] = 1.0,
        });
        var derived = new DerivedSignals(car);
        var v = await derived.ReadAsync(["rpm", DerivedSignals.RoughRatio, DerivedSignals.RoughCylinder, DerivedSignals.StftMax, DerivedSignals.StftMin]);
        Assert.Equal(3, v[DerivedSignals.RoughCylinder]);
        Assert.InRange(v[DerivedSignals.RoughRatio], 3.5, 4.5);
        Assert.Equal(17, v[DerivedSignals.StftMax]);
        Assert.Equal(4, v[DerivedSignals.StftMin]);
        Assert.Equal(2000, v["rpm"]);
        Assert.False(v.ContainsKey("rough3"), "only what was asked for comes back");
        Assert.False(car.Asked.Contains(DerivedSignals.RoughRatio), "derived ids are never sent to the car");
    }

    [Fact]
    public async Task Derived_values_are_left_out_when_the_car_lacks_them()
    {
        var derived = new DerivedSignals(new FakeSource(new() { ["rpm"] = 800, ["rough1"] = 1, ["rough2"] = 1 }));
        var v = await derived.ReadAsync(["rpm", DerivedSignals.RoughRatio, DerivedSignals.StftMax]);
        Assert.False(v.ContainsKey(DerivedSignals.RoughRatio), "two cylinders aren't enough to compare");
        Assert.False(v.ContainsKey(DerivedSignals.StftMax));
    }

    [Fact]
    public async Task The_misfire_template_fires_on_one_rough_cylinder()
    {
        var rule = TriggerPresets.For("misfire")!.Rule with { Before = TimeSpan.FromSeconds(2), After = TimeSpan.FromSeconds(1) };
        var frames = Enumerable.Range(0, 40).Select(i => new DatalogFrame(T0.AddSeconds(i * 0.2), new Dictionary<string, double>
        {
            ["rpm"] = 2500, [DerivedSignals.RoughRatio] = i is >= 20 and < 26 ? 4.2 : 1.1,
        }));
        var captures = await Run(rule, frames);
        Assert.Single(captures);
    }

    [Fact]
    public void Every_template_reads_like_an_owner_wrote_it()
    {
        Assert.True(TriggerPresets.Templates.Count >= 6);
        Assert.All(TriggerPresets.Templates, t =>
        {
            Assert.StartsWith("Catch", t.Title);
            Assert.False(string.IsNullOrWhiteSpace(t.WhenToUse));
            Assert.False(string.IsNullOrWhiteSpace(t.DriveTip));
            Assert.Empty(t.Rule.Validate());
            Assert.All(t.Rule.Parameters, p => Assert.NotEqual(p, SignalLabels.Name(p)));
        });
        Assert.Equal(TriggerPresets.Templates.Count, TriggerPresets.All.Select(r => r.Id).Distinct().Count());
    }
}
