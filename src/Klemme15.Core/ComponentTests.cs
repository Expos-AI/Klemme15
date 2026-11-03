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

namespace Klemme15.Core;

/// <summary>Judges the electric water pump from requested vs actual speed. Rules of thumb, written down so anyone can check them.</summary>
public static class PumpAnalysis
{
    /// <summary>The pump counts as keeping up when it reaches at least this share of the requested speed.</summary>
    public const double Good = 0.85, Poor = 0.70;

    /// <param name="samples">(requested, actual) pairs, in whatever unit the engine computer reports (both the same).</param>
    public static TestFinding Tracking(IReadOnlyList<(double Target, double Actual)> samples)
    {
        var active = samples.Where(s => s.Target > 5).ToList();
        if (active.Count < 5)
            return new(TestSeverity.Unknown, "The engine computer didn't ask the pump to run",
                "No pump request was seen. On a warm engine it normally runs the pump all the time; make sure the engine is warm and run the test again. If it never asks, check for water pump faults on the Faults page.");
        var dead = active.Count(s => s.Actual < 0.1 * s.Target);
        if (dead >= active.Count / 2)
            return new(TestSeverity.Problem, "The water pump isn't turning",
                "The engine computer asks for speed but the pump reports almost none. Stop driving: the engine can overheat within minutes. Check the pump's connector and fuse, then the pump itself (on N51/N52/N53/N54/N55 the electric pump is a known wear part).");
        var ratios = active.Select(s => s.Actual / s.Target).OrderBy(r => r).ToList();
        var median = ratios[ratios.Count / 2];
        var nums = $"The pump reached {median:P0} of the requested speed (median of {active.Count} readings).";
        if (median >= Good)
            return new(TestSeverity.Ok, "The water pump keeps up with what the engine asks", nums);
        if (median >= Poor)
            return new(TestSeverity.Watch, "The water pump lags behind its request",
                $"{nums} Early wear (bearing, impeller) or air in the cooling system. Bleed the system, then test again; if it stays low, plan a pump replacement before summer or a long trip.");
        return new(TestSeverity.Problem, "The water pump can't reach the requested speed",
            $"{nums} A failing pump is the usual cause. Replace it before it lets the engine overheat, and bleed the cooling system afterwards (Tests > Cooling system bleed assistant).");
    }

    /// <param name="steps">(commanded fraction, average actual speed) per step, lowest command first.</param>
    public static TestFinding Sweep(IReadOnlyList<(double Command, double Actual)> steps)
    {
        if (steps.Count == 0)
            return new(TestSeverity.Unknown, "No pump speed was read", "Klemme15 couldn't read the pump's speed while driving it. Download the engine readings in the Garage and try again.");
        var nums = string.Join(" → ", steps.Select(s => $"{s.Command:P0}: {s.Actual:0}"));
        var max = steps.Max(s => s.Actual);
        if (max < 1)
            return new(TestSeverity.Problem, "The pump didn't turn at any step",
                $"Commanded {nums}. Check the pump's fuse and connector; if it gets power and still doesn't turn, the pump has failed.");
        var rising = true;
        for (var i = 1; i < steps.Count; i++)
            if (steps[i].Actual <= steps[i - 1].Actual * 1.02) rising = false;
        if (!rising)
            return new(TestSeverity.Watch, "The pump didn't speed up with every step",
                $"Commanded → actual: {nums}. A healthy pump turns faster each time it's asked to. A pump that tops out early is often worn; run it again after bleeding the cooling system to rule out air.");
        return new(TestSeverity.Ok, "The pump speeds up step by step", $"Commanded → actual: {nums}.");
    }
}

public static partial class GuidedTests
{
    /// <summary>Read-only: requested vs actual pump speed at idle and at 3,000 rpm, warm engine.</summary>
    public static async Task<TestReport> WaterPumpAsync(ISignalSource signals, IProgress<TestProgress>? progress, CancellationToken ct, double speed = 1)
    {
        if (await Ready(signals, progress, ct) is { } stop) return stop;
        string[] wanted = ["rpm", "pump_target", "pump_speed", "coolant"];
        var graph = new TraceRecorder();
        var samples = new List<(double, double)>();
        const int ticks = 80;
        for (var tick = 0; tick < ticks; tick++)
        {
            var v = await signals.ReadAsync(wanted, ct);
            var sec = tick * Tick.TotalSeconds;
            graph.Add("Pump requested", "pump", sec, v, "pump_target", target: true);
            graph.Add("Pump actual", "pump", sec, v, "pump_speed");
            graph.Add("Engine speed", "rpm", sec, v, "rpm");
            if (v.TryGetValue("pump_target", out var t) && v.TryGetValue("pump_speed", out var a)) samples.Add((t, a));
            var instruction = tick is >= 24 and < 56 ? "Hold the engine at about 3,000 rpm, steady." : tick >= 56 ? "Let it idle again." : "Let the engine idle.";
            progress?.Report(new(instruction, $"{v.GetValueOrDefault("rpm"):0} rpm" + (v.TryGetValue("pump_speed", out var ps) ? $" · pump {ps:0}" : ""),
                (double)tick / ticks, graph.Snapshot()));
            await Wait(speed, ct);
        }
        if (samples.Count == 0)
            return TestReport.Stop("Pump speed isn't available", "This engine computer didn't report the water pump's requested and actual speed with this connection. Download the engine readings in the Garage, or connect with your BMW files. Engines with a belt-driven pump don't have these values.");
        var finding = PumpAnalysis.Tracking(samples);
        var active = samples.Where(s => s.Item1 > 5).ToList();
        var values = new List<KeyValuePair<string, double>>();
        if (active.Count > 0) values.Add(new("Actual vs requested (%)", Math.Round(100 * active.Average(s => s.Item2 / s.Item1), 0)));
        return new TestReport([finding], values) { Traces = graph.Snapshot() };
    }

    /// <summary>Drives the pump at 30, 60 and 90 % (engine off) and records what it actually does.</summary>
    public static async Task<TestReport> WaterPumpSweepAsync(ISignalSource signals, IActuator? actuator, IProgress<TestProgress>? progress, CancellationToken ct, double speed = 1)
    {
        if (actuator is null)
            return TestReport.Stop("This test needs your BMW files",
                "It drives the pump through the engine computer's own test job, which Klemme15 runs only from your BMW files: connect with \"Use my own BMW files (EDIABAS mode)\" ticked (Windows). The read-only water pump health test works with any connection.");
        string[] wanted = ["rpm", "pump_speed", "coolant", "voltage"];
        progress?.Report(new("Checking the car...", "", 0));
        var first = await signals.ReadAsync(wanted, ct);
        if (first.TryGetValue("rpm", out var rpm0) && rpm0 > 300)
            return TestReport.Stop("Switch the engine off first", "The sweep runs with the engine off and the ignition on, so the engine computer isn't driving the pump itself.");
        double[] commands = [0.3, 0.6, 0.9];
        const int stepTicks = 16;
        var graph = new TraceRecorder();
        var steps = new List<(double, double)>();
        var clock = 0;
        try
        {
            for (var i = 0; i < commands.Length; i++)
            {
                await actuator.SetAsync(commands[i], 12, ct);
                var readings = new List<double>();
                for (var t = 0; t < stepTicks; t++)
                {
                    var v = await signals.ReadAsync(wanted, ct);
                    var sec = clock++ * Tick.TotalSeconds;
                    graph.Add("Commanded", "%", sec, commands[i] * 100, target: true);
                    graph.Add("Pump actual", "pump", sec, v, "pump_speed");
                    if (v.TryGetValue("pump_speed", out var a) && t >= stepTicks / 2) readings.Add(a);
                    progress?.Report(new($"Pump at {commands[i]:P0}. Listen at the front of the engine.", v.TryGetValue("pump_speed", out var ps) ? $"pump {ps:0}" : "",
                        (i * stepTicks + t + 1.0) / (commands.Length * stepTicks), graph.Snapshot()));
                    await Wait(speed, ct);
                }
                if (readings.Count > 0) steps.Add((commands[i], readings.Average()));
            }
        }
        finally
        {
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await actuator.StopAsync(stopCts.Token); } catch (Exception) { /* the job's own duration ends it anyway */ }
        }
        progress?.Report(new("Done. The pump has been switched off.", "", 1, graph.Snapshot()));
        var values = steps.Select(s => new KeyValuePair<string, double>($"Actual at {s.Item1:P0}", Math.Round(s.Item2, 0))).ToList();
        return new TestReport([PumpAnalysis.Sweep(steps)], values) { Traces = graph.Snapshot() };
    }
}
