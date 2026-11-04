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
using Klemme15.Core;
using Xunit;

namespace Klemme15.Tests;

public class IdleCheckTests
{
    private static string Fixture() => Path.Combine(FindRoot(), "tests", "Klemme15.Tests", "Fixtures", "real", "e90-n52-msv70-idle.csv");

    private static string FindRoot([System.Runtime.CompilerServices.CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    private static IdlePhase Real(double rpmOffset = 0, IReadOnlyList<double>? rough = null, double? target = null)
    {
        var lines = File.ReadAllLines(Fixture()).Skip(1).Where(l => l.Length > 0)
            .Select(l => l.Split(',').Select(c => double.Parse(c, CultureInfo.InvariantCulture)).ToArray()).ToList();
        return new IdlePhase
        {
            Rpm = lines.Select(r => r[1] + rpmOffset).ToList(),
            Roughness = rough ?? Enumerable.Range(9, 6).Select(c => lines.Average(r => Math.Abs(r[c]))).ToList(),
            Trim1 = lines.Average(r => r[5] + r[6]), Trim2 = lines.Average(r => r[7] + r[6]), Target = target,
        };
    }

    [Fact]
    public void The_real_healthy_idle_passes()
    {
        var f = IdleAnalysis.Analyze(Real(), Real(), lowestAfterEngage: 705, acReported: true);
        Assert.All(f, x => Assert.Equal(TestSeverity.Ok, x.Severity));
        Assert.Contains(f, x => x.Title == "The engine itself idles cleanly" && x.Explanation.Contains("mounts"));
    }

    [Fact]
    public void A_hunting_idle_is_a_problem()
    {
        var hunting = Real() with { Rpm = Enumerable.Range(0, 40).Select(i => 700 + 110 * Math.Sin(i / 3.0)).ToList() };
        var f = IdleAnalysis.Analyze(Real(), hunting, null, acReported: true);
        Assert.Contains(f, x => x.Severity == TestSeverity.Problem && x.Title == "Idle hunts with the air conditioning on");
        Assert.DoesNotContain(f, x => x.Title.Contains("AC off") || x.Title == "Idle hunts with the air conditioning off");
    }

    [Fact]
    public void A_chug_when_the_compressor_engages_is_caught_against_the_target()
    {
        var f = IdleAnalysis.Analyze(Real(target: 700), Real(target: 720), lowestAfterEngage: 540, acReported: true);
        var chug = Assert.Single(f, x => x.Title == "The engine chugs when the air conditioning engages");
        Assert.Equal(TestSeverity.Problem, chug.Severity);
        Assert.Contains("180 rpm below the engine computer's target", chug.Explanation);
    }

    [Fact]
    public void A_cylinder_that_only_falters_under_load_is_named()
    {
        var off = Real();
        var loaded = off.Roughness.Select((v, i) => i == 1 ? 1500 : v).ToList();   // about 3.7× the others
        var f = IdleAnalysis.Analyze(off, Real(rough: loaded), 700, acReported: true);
        Assert.Contains(f, x => x.Title == "Cylinder 2 struggles under the air conditioning's load" && x.Severity == TestSeverity.Problem);
    }

    [Fact]
    public void Lean_at_idle_points_to_an_air_leak()
    {
        var f = IdleAnalysis.Analyze(Real() with { Trim1 = 14 }, null, null, acReported: false);
        Assert.Contains(f, x => x.Title == "Running lean at idle" && x.Explanation.Contains("crankcase vent valve"));
    }

    [Fact]
    public async Task The_test_runs_off_then_on_and_catches_the_dip()
    {
        // AC off for the first part, switched on at read 50; the compressor engages and the engine sags to 520 rpm.
        var car = new Scripted(n => new()
        {
            ["rpm"] = n is >= 52 and < 55 ? 520 : 720 + n % 3, ["coolant"] = 88, ["speed"] = 0, ["idle_target"] = 700,
            ["ac"] = n >= 50 ? 1 : 0, ["ac_compressor"] = n >= 51 ? 1 : 0, ["stft1"] = 1, ["ltft1"] = 0,
        });
        var report = await GuidedTests.IdleAcAsync(car, null, CancellationToken.None, speed: 1e6);
        Assert.Contains(report.Findings, f => f.Title == "The engine chugs when the air conditioning engages");
        Assert.Contains(report.Values, v => v.Key == "Lowest rpm as the AC engaged" && v.Value == 520);
        Assert.Contains(report.Traces, t => t.Name == "Idle target" && t.Target);
    }

    [Fact]
    public async Task The_test_waits_for_a_cold_engine_to_warm_up() =>
        Assert.Equal("Warm the engine up first",
            (await GuidedTests.IdleAcAsync(new Scripted(_ => new() { ["rpm"] = 730, ["coolant"] = 58 }), null, CancellationToken.None, 1e6)).Findings[0].Title);

    private sealed class Scripted(Func<int, Dictionary<string, double>> at) : ISignalSource
    {
        private int _n;
        public Task<IReadOnlyDictionary<string, double>> ReadAsync(IReadOnlyCollection<string> signals, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, double>>(at(_n++).Where(kv => signals.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
