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

using System.Globalization;
using System.Runtime.CompilerServices;
using Klemme15.Core;
using Klemme15.Core.GhostHunter;
using Xunit;

namespace Klemme15.Tests;

/// <summary>
/// Every Ghost Hunter template, run through the real pipeline (worked-out values, then the monitor) on 77 seconds of
/// values read from a real E90 325i (N52, MSV70) idling with no faults: none may fire. Then each template's fault is
/// put into the same real readings, and it must fire. The car was parked, so the templates that need driving get
/// engine speed and road speed set for their fault.
/// </summary>
public class GhostHunterReplayTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 17, 55, 54, TimeSpan.Zero);

    private static string Root([CallerFilePath] string here = "") => Path.GetDirectoryName(here)!;

    private static List<(double Seconds, Dictionary<string, double> Values)> RealIdle()
    {
        var lines = File.ReadAllLines(Path.Combine(Root(), "Fixtures", "real", "e90-n52-msv70-idle.csv"));
        var header = lines[0].Split(',');
        return lines.Skip(1).Where(l => l.Length > 0).Select(l =>
        {
            var cells = l.Split(',').Select(c => double.Parse(c, CultureInfo.InvariantCulture)).ToArray();
            var values = new Dictionary<string, double>();
            for (var i = 1; i < header.Length; i++) values[header[i]] = cells[i];
            return (cells[0], values);
        }).ToList();
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = T0;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Rows(List<(double Seconds, Dictionary<string, double> Values)> rows) : ISignalSource
    {
        public int Next;
        public Task<IReadOnlyDictionary<string, double>> ReadAsync(IReadOnlyCollection<string> signals, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, double>>(rows[Next].Values.Where(kv => signals.Contains(kv.Key)).ToDictionary());
    }

    /// <summary>Feeds the rows through DerivedSignals and the monitor the way the poller does, with the rows' own times.</summary>
    private static async Task<List<TriggerCapture>> Replay(TriggerRule rule, List<(double Seconds, Dictionary<string, double> Values)> rows)
    {
        var clock = new Clock();
        var source = new Rows(rows);
        var derived = new DerivedSignals(source, clock);
        var ids = TriggerMonitor.SignalsFor([rule]);
        var captures = new List<TriggerCapture>();
        var monitor = new TriggerMonitor([rule], (c, _) => { captures.Add(c); return Task.CompletedTask; });
        var run = monitor.RunAsync(CancellationToken.None);
        for (var i = 0; i < rows.Count; i++)
        {
            source.Next = i;
            clock.Now = T0.AddSeconds(rows[i].Seconds);
            await monitor.Input.WriteAsync(new DatalogFrame(clock.Now, await derived.ReadAsync(ids)));
        }
        monitor.Input.Complete();
        await run;
        return captures;
    }

    /// <summary>The real rows with <paramref name="always"/> applied to every row, and <paramref name="fault"/> from <paramref name="from"/> to <paramref name="to"/> seconds.</summary>
    private static List<(double, Dictionary<string, double>)> With(Action<Dictionary<string, double>>? always, Action<Dictionary<string, double>> fault, double from, double to) =>
        RealIdle().Select(r =>
        {
            var v = new Dictionary<string, double>(r.Values);
            always?.Invoke(v);
            if (r.Seconds >= from && r.Seconds <= to) fault(v);
            return (r.Seconds, v);
        }).ToList();

    private static TriggerRule Template(string id) => TriggerPresets.All.Single(r => r.Id == id);

    public static IEnumerable<object[]> Ids => TriggerPresets.All.Select(r => new object[] { r.Id });

    [Theory]
    [MemberData(nameof(Ids))]
    public async Task A_healthy_real_engine_sets_off_no_template(string id) =>
        Assert.Empty(await Replay(Template(id), RealIdle().Select(r => (r.Seconds, r.Values)).ToList()));

    [Fact]
    public void The_real_readings_cover_every_value_the_templates_need()
    {
        var row = RealIdle()[0].Values;
        foreach (var id in TriggerMonitor.SignalsFor(TriggerPresets.All).Where(s => !DerivedSignals.Ids.Contains(s)))
            Assert.True(row.ContainsKey(id) || id is "speed", $"{id} missing from the real readings");
        Assert.InRange(row["voltage"], 13.8, 14.8);
    }

    // Each fault, put into the real readings for a few seconds in the middle.
    [Fact]
    public async Task Misfire_fires_and_names_the_cylinder()
    {
        var capture = Assert.Single(await Replay(Template("misfire"), With(null, v => v["rough5"] = 2500 + Math.Abs(v["rough5"]), 30, 45)));
        var atEvent = capture.Frames.First(f => f.Time >= capture.Event.At);
        Assert.Equal(5, atEvent.Values[DerivedSignals.RoughCylinder]);
    }

    [Fact]
    public async Task Voltage_dip_fires() =>
        Assert.Single(await Replay(Template("voltage-dip"), With(null, v => v["voltage"] = 11.0, 30, 33)));

    [Fact]
    public async Task Lean_while_driving_fires_on_either_bank() =>
        Assert.Single(await Replay(Template("lean-spike"), With(v => { v["rpm"] = 2200; v["speed"] = 60; }, v => v["stft2"] = 20, 30, 36)));

    [Fact]
    public async Task Lean_idle_waits_for_a_warm_engine_then_fires()
    {
        // This car was at 52-63 °C: the template waits for 70 °C, so a lean idle on a cold engine isn't caught.
        Assert.Empty(await Replay(Template("lean-idle"), With(null, v => v["stft1"] = 15, 30, 40)));
        Assert.Single(await Replay(Template("lean-idle"), With(v => v["coolant"] = 88, v => v["stft1"] = 15, 30, 40)));
    }

    [Fact]
    public async Task Rich_while_driving_fires() =>
        Assert.Single(await Replay(Template("rich-spike"), With(v => v["rpm"] = 2000, v => v["stft1"] = -20, 30, 36)));

    [Fact]
    public async Task Overheating_fires() =>
        Assert.Single(await Replay(Template("overheat"), With(null, v => v["coolant"] = 115, 30, 36)));

    [Fact]
    public async Task Charging_dropout_fires_only_with_the_engine_above_1000_rpm()
    {
        Assert.Empty(await Replay(Template("charging-low"), With(null, v => v["voltage"] = 12.4, 30, 40)));   // real idle is ~730 rpm
        Assert.Single(await Replay(Template("charging-low"), With(v => v["rpm"] = 2000, v => v["voltage"] = 12.4, 30, 40)));
    }

    [Fact]
    public async Task Stall_while_rolling_fires() =>
        Assert.Single(await Replay(Template("stall"), With(v => v["speed"] = 25, v => v["rpm"] = 0, 30, 33)));

    [Fact]
    public void Right_now_says_what_each_template_waits_for_on_the_real_parked_car()
    {
        var row = new Dictionary<string, double>(RealIdle()[0].Values) { [DerivedSignals.RoughRatio] = 1.3, [DerivedSignals.StftMax] = 1.6, [DerivedSignals.StftMin] = 0.8 };
        var lines = TriggerPresets.All.ToDictionary(r => r.Id, r => RuleProgress.Describe(r, row));

        // Idling, parked, 58 °C: what a confused owner needs to hear.
        Assert.Equal(RuleReadiness.Waiting, lines["lean-idle"].State);
        Assert.Contains("coolant temperature above 70 °C (now 58 °C)", lines["lean-idle"].Text);
        Assert.Equal(RuleReadiness.Waiting, lines["stall"].State);
        Assert.Contains("vehicle speed above 3 km/h (now 0 km/h)", lines["stall"].Text);
        Assert.Equal(RuleReadiness.Waiting, lines["lean-spike"].State);
        Assert.Contains("engine speed above 1500 rpm (now 730 rpm)", lines["lean-spike"].Text);
        Assert.Equal(RuleReadiness.Waiting, lines["charging-low"].State);

        // Nothing else to wait for: ready, and nothing wrong right now.
        Assert.Equal(RuleReadiness.Ready, lines["voltage-dip"].State);
        Assert.StartsWith("Ready. Battery voltage is 14.6 V, caught if it goes below 11.5 V", lines["voltage-dip"].Text);
        Assert.Equal(RuleReadiness.Ready, lines["misfire"].State);
        Assert.Equal(RuleReadiness.Ready, lines["overheat"].State);

        Assert.Equal(RuleReadiness.NoReading, RuleProgress.Describe(TriggerPresets.All.Single(r => r.Id == "misfire"), RealIdle()[0].Values).State);
        Assert.Equal(RuleReadiness.Holding, RuleProgress.Describe(TriggerPresets.All.Single(r => r.Id == "overheat"), new Dictionary<string, double> { ["coolant"] = 118 }).State);
    }
}
