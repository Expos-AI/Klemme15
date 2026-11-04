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

/// <summary>What the engine did during one part of the idle check.</summary>
public sealed record IdlePhase
{
    public required IReadOnlyList<double> Rpm { get; init; }

    /// <summary>The idle speed the engine computer aimed for (average), when it reports it.</summary>
    public double? Target { get; init; }

    /// <summary>Average size of each cylinder's roughness readings, when reported.</summary>
    public IReadOnlyList<double> Roughness { get; init; } = [];

    /// <summary>Total mixture correction per bank (short + long term, %), when reported.</summary>
    public double? Trim1 { get; init; }
    public double? Trim2 { get; init; }

    public double Mean => Rpm.Average();
    public double Min => Rpm.Min();
    public double Max => Rpm.Max();
    public double Swing => Max - Min;
}

/// <summary>
/// The verdicts of the rough idle and air conditioning check. The limits are Klemme15's own rules of thumb, stated in
/// every finding with the measured numbers, so an owner can judge for themselves: a steady warm idle stays within a
/// few dozen rpm (a real E90 325i at idle swung by 22 rpm at most over 3 seconds), so a swing over 50 rpm is worth a
/// look and over 150 rpm, or a dip 150 rpm below the engine computer's own target, is a problem.
/// </summary>
public static class IdleAnalysis
{
    public const double SwingWatch = 50, SwingProblem = 150, DipProblem = 150;

    public static IReadOnlyList<TestFinding> Analyze(IdlePhase off, IdlePhase? on, double? lowestAfterEngage, bool acReported)
    {
        var findings = new List<TestFinding>();
        Steadiness(findings, off, "with the air conditioning off");
        if (on is not null) Steadiness(findings, on, "with the air conditioning on");

        // The moment the compressor engages: the engine computer should catch the extra load.
        if (on is not null && lowestAfterEngage is { } low)
        {
            var reference = on.Target ?? off.Target ?? off.Mean;
            var drop = reference - low;
            if (drop >= DipProblem)
                findings.Add(new(TestSeverity.Problem, "The engine chugs when the air conditioning engages",
                    $"Engine speed fell to {low:0} rpm, {drop:0} rpm below {((on.Target ?? off.Target) is not null ? "the engine computer's target" : "the idle before")}. The engine computer raises idle torque when the compressor switches in; when it can't keep up, look for intake air leaks (crankcase vent valve, intake boot, oil filler cap seal), a dirty throttle body, and the compressor itself: a compressor or clutch that drags loads the engine hard."));
            else if (drop >= SwingWatch)
                findings.Add(new(TestSeverity.Watch, "The engine dips a little when the air conditioning engages",
                    $"Engine speed fell to {low:0} rpm, {drop:0} rpm below where it should be, and recovered. A small dip is normal; a bigger one each time is worth watching."));
        }
        else if (on is not null && !acReported)
            findings.Add(new(TestSeverity.Unknown, "The engine computer doesn't report the air conditioning",
                "Klemme15 compared the two halves by time instead. If the engine chugged when you switched it on, the Ghost Hunter's \"Catch a chug at idle\" captures the moment."));

        // A cylinder that only stands out under the compressor's load is a weak cylinder (coil, plug, injector).
        Cylinders(findings, off, on);

        // Mixture at idle.
        foreach (var (phase, when) in new[] { (off, "at idle"), (on, "at idle with the air conditioning on") })
        {
            if (phase is null) continue;
            var worst = new[] { phase.Trim1, phase.Trim2 }.OfType<double>().DefaultIfEmpty(0).MaxBy(Math.Abs);
            if (Math.Abs(worst) >= 10)
            {
                findings.Add(new(TestSeverity.Problem, worst > 0 ? $"Running lean {when}" : $"Running rich {when}",
                    $"The engine computer corrects the mixture by {worst:+0.0;-0.0} % {when}. " + (worst > 0
                        ? "Lean at idle is the classic sign of an intake air leak, which also makes the idle rough and hunting: crankcase vent valve, intake boot, oil filler cap seal, vacuum lines. The fuel trim test on this page narrows it down."
                        : "Rich at idle points to a leaking injector, the purge valve or an oxygen sensor. The fuel trim test on this page narrows it down.")));
                break;
            }
        }

        if (findings.All(f => f.Severity == TestSeverity.Ok))
            findings.Add(new(TestSeverity.Ok, "The engine itself idles cleanly",
                "Steady speed, even cylinders and a normal mixture. If the car still shakes at idle, especially with the air conditioning on or in Drive, the engine may be fine and the shake reaching you through worn engine or gearbox mounts, which is common on these cars."));
        return findings;
    }

    private static void Steadiness(List<TestFinding> findings, IdlePhase p, string when)
    {
        var below = p.Target is { } t ? t - p.Min : 0;
        var targetText = p.Target is { } tt ? $" The engine computer aimed for {tt:0} rpm." : "";
        if (p.Swing >= SwingProblem || below >= DipProblem)
            findings.Add(new(TestSeverity.Problem, $"Idle hunts {when}",
                $"Engine speed moved between {p.Min:0} and {p.Max:0} rpm.{targetText} A warm idle should hold within a few dozen rpm. Common causes on these engines: an intake air leak (crankcase vent valve, intake boot, oil filler cap seal), a dirty throttle body, a misfiring cylinder, or on Valvetronic engines a worn or dirty eccentric shaft sensor or motor."));
        else if (p.Swing >= SwingWatch)
            findings.Add(new(TestSeverity.Watch, $"Idle a little unsteady {when}",
                $"Engine speed moved between {p.Min:0} and {p.Max:0} rpm.{targetText} Not bad, but steadier than this is normal for a warm engine."));
        else
            findings.Add(new(TestSeverity.Ok, $"Idle steady {when}", $"Engine speed held between {p.Min:0} and {p.Max:0} rpm.{targetText}"));
    }

    private static void Cylinders(List<TestFinding> findings, IdlePhase off, IdlePhase? on)
    {
        if (off.Roughness.Count < 3) return;
        int Worst(IReadOnlyList<double> r) => Enumerable.Range(0, r.Count).MaxBy(i => r[i]);
        double Ratio(IReadOnlyList<double> r, int i) => RoughnessRatio.Of(r[i], r);
        var iOff = Worst(off.Roughness);
        var rOff = Ratio(off.Roughness, iOff);
        if (on is { Roughness.Count: >= 3 } && on.Roughness.Count == off.Roughness.Count)
        {
            var iOn = Worst(on.Roughness);
            var rOn = Ratio(on.Roughness, iOn);
            if (rOn >= 2 && rOff < 2)
            {
                findings.Add(new(rOn >= 3 ? TestSeverity.Problem : TestSeverity.Watch, $"Cylinder {iOn + 1} struggles under the air conditioning's load",
                    $"It runs about {rOn:0.0}× rougher than the others with the air conditioning on, but evenly without it. A cylinder that only falters under load is typically a weak ignition coil or spark plug: swap its coil with a neighbouring cylinder and test again. If the problem moves, it's the coil."));
                return;
            }
        }
        if (rOff >= 2)
            findings.Add(new(rOff >= 3 ? TestSeverity.Problem : TestSeverity.Watch, $"Cylinder {iOff + 1} runs rougher than the others",
                $"About {rOff:0.0}× the others at idle. Swap its ignition coil with a neighbouring cylinder and test again: if the problem moves, it's the coil; if not, check the spark plug, then the injector."));
    }
}

public static partial class GuidedTests
{
    private static readonly string[] IdleSignals =
        ["rpm", "speed", "idle_target", "ac", "ac_compressor", "stft1", "ltft1", "stft2", "ltft2", "rough1", "rough2", "rough3", "rough4", "rough5", "rough6", "rough7", "rough8"];

    /// <summary>
    /// The rough idle and air conditioning check: 20 seconds of idle with the air conditioning off, then on. Compares
    /// how steady the engine speed is against the engine computer's own target, catches the dip when the compressor
    /// engages, and compares the cylinders and the mixture in both. Read-only.
    /// </summary>
    public static async Task<TestReport> IdleAcAsync(ISignalSource signals, IProgress<TestProgress>? progress, CancellationToken ct, double speed = 1)
    {
        if (await Ready(signals, progress, ct) is { } stop) return stop;
        var graph = new TraceRecorder();
        var clock = 0;
        const int phaseTicks = 40;   // 20 s each
        bool? AcOn(IReadOnlyDictionary<string, double> v) =>
            v.TryGetValue("ac_compressor", out var c) && c > 0.5 ? true :
            v.TryGetValue("ac", out var a) ? a > 0.5 : v.ContainsKey("ac_compressor") ? false : null;

        async Task<IReadOnlyDictionary<string, double>> Read(string instruction, string status, double fraction)
        {
            var v = await signals.ReadAsync(IdleSignals, ct);
            var sec = clock++ * Tick.TotalSeconds;
            graph.Add("Engine speed", "rpm", sec, v, "rpm");
            graph.Add("Idle target", "rpm", sec, v, "idle_target", target: true);
            progress?.Report(new(instruction, status, fraction, graph.Snapshot()));
            return v;
        }

        // 1. The air conditioning off (waits up to 30 s for it to be switched off).
        var first = await signals.ReadAsync(IdleSignals, ct);
        var acReported = AcOn(first) is not null;
        if (AcOn(first) == true)
            for (var i = 0; i < 60; i++)
            {
                var v = await Read("Switch the air conditioning OFF and keep the engine idling.", "Waiting for the air conditioning to switch off", 0);
                if (AcOn(v) == false) break;
                if (i == 59) return TestReport.Stop("The air conditioning stayed on", "The test starts with the air conditioning off. Switch it off (the AC button's light goes out) and run the test again.");
                await Wait(speed, ct);
            }

        async Task<IdlePhase?> Phase(string instruction, double start)
        {
            var rpm = new List<double>(); var target = new List<double>(); var t1 = new List<double>(); var t2 = new List<double>();
            var rough = new double[8]; var roughN = new int[8];
            for (var tick = 0; tick < phaseTicks; tick++)
            {
                var v = await Read(instruction, $"{v0(rpm)}measuring ({tick + 1}/{phaseTicks})", start + 0.5 * tick / phaseTicks);
                if (v.TryGetValue("rpm", out var r) && r is > 300 and < 1500 && v.GetValueOrDefault("speed") < 3) rpm.Add(r);
                if (v.TryGetValue("idle_target", out var it)) target.Add(it);
                if (v.TryGetValue("stft1", out var s1) && v.TryGetValue("ltft1", out var l1)) t1.Add(s1 + l1);
                if (v.TryGetValue("stft2", out var s2) && v.TryGetValue("ltft2", out var l2)) t2.Add(s2 + l2);
                for (var c = 0; c < 8; c++) if (v.TryGetValue($"rough{c + 1}", out var d)) { rough[c] += Math.Abs(d); roughN[c]++; }
                await Wait(speed, ct);
            }
            if (rpm.Count < phaseTicks / 2) return null;
            return new IdlePhase
            {
                Rpm = rpm, Target = target.Count > 0 ? target.Average() : null,
                Roughness = Enumerable.Range(0, 8).Where(c => roughN[c] > 0).Select(c => rough[c] / roughN[c]).ToList(),
                Trim1 = t1.Count > 0 ? t1.Average() : null, Trim2 = t2.Count > 0 ? t2.Average() : null,
            };
            static string v0(List<double> r) => r.Count > 0 ? $"{r[^1]:0} rpm: " : "";
        }

        var off = await Phase("Let the engine idle with the air conditioning OFF. Don't touch the pedal.", 0);
        if (off is null) return TestReport.Stop("No steady idle reading", "Engine speed couldn't be read at idle. Keep the car stopped with the engine idling and try again.");

        // 2. Switch it on, and watch the moment the compressor engages.
        double? lowest = null;
        var engaged = false;
        for (var i = 0; i < (acReported ? 60 : 16); i++)
        {
            var v = await Read("Now switch the air conditioning ON: coldest setting, fan on. Don't touch the pedal.",
                acReported ? "Waiting for the compressor to engage" : "Switch it on now", 0.5);
            if (acReported && AcOn(v) == true) { engaged = true; break; }
            await Wait(speed, ct);
        }
        if (acReported && !engaged)
            return TestReport.Stop("The air conditioning didn't come on", "Klemme15 waited 30 seconds for the compressor to engage. Press the AC button (its light comes on), set the coldest temperature and the fan on, and run the test again. On a cold day the compressor may not run at all.");
        for (var i = 0; i < 10; i++)   // the 5 seconds after it engages
        {
            var v = await Read("Air conditioning on. Don't touch the pedal.", "Watching the engine take the load", 0.5);
            if (v.TryGetValue("rpm", out var r) && r > 300) lowest = lowest is { } l ? Math.Min(l, r) : r;
            await Wait(speed, ct);
        }
        var on = await Phase("Keep idling with the air conditioning ON. Don't touch the pedal.", 0.5);
        progress?.Report(new("Done. You can switch the air conditioning back as you like it.", "", 1, graph.Snapshot()));

        var values = new List<KeyValuePair<string, double>>
        {
            new("Idle, AC off: average rpm", Math.Round(off.Mean)), new("Idle, AC off: swing rpm", Math.Round(off.Swing)),
        };
        if (off.Target is { } t) values.Add(new("Idle target rpm", Math.Round(t)));
        if (on is not null) { values.Add(new("Idle, AC on: average rpm", Math.Round(on.Mean))); values.Add(new("Idle, AC on: swing rpm", Math.Round(on.Swing))); }
        if (lowest is { } low) values.Add(new("Lowest rpm as the AC engaged", Math.Round(low)));
        return new TestReport(IdleAnalysis.Analyze(off, on, lowest, acReported), values) { Traces = graph.Snapshot() };
    }
}
