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
using System.Text;
using System.Text.RegularExpressions;

namespace Klemme15.Core;

/// <summary>
/// A log shared to build a community baseline: only the engine values the baselines use, under their standard names,
/// air mass in g/s, time from zero, and a few comment lines naming the engine. No VIN, no date of the drive, no
/// location. The same rules are checked again by the intake pipeline on GitHub (scripts/baseline_intake.py).
/// </summary>
public static partial class BaselineContribution
{
    public const string Marker = "# klemme15-baseline-contribution v1";

    /// <summary>The values a contribution may carry, in this order.</summary>
    public static readonly string[] Columns = ["rpm", "load", "coolant", "maf", "stft1", "ltft1", "stft2", "ltft2", "iat", "throttle"];

    // Engine codes look like N52B30, N54B30, M57D30, N47D20, S54B32, M54B25.
    [GeneratedRegex("^[A-Z][0-9]{2}[A-Z0-9]{0,6}$")]
    public static partial Regex EngineCode();

    public const int MinWarmSamples = 120, MinIdleSamples = 30, MinTopRpm = 3000, MinBins = 6;

    /// <summary>What's missing for this log to make a useful baseline (empty when it's ready to share).</summary>
    public static IReadOnlyList<string> Check(DataLog log, string? engine)
    {
        var problems = new List<string>();
        if (engine is null || !EngineCode().IsMatch(engine.ToUpperInvariant()))
            problems.Add("Klemme15 needs the engine code (for example N52B30). Connect to the car, or type it in.");
        if (!BaselineSignals.Has(log, "rpm")) problems.Add("The log has no engine speed.");
        if (!BaselineSignals.Has(log, "coolant")) problems.Add("The log has no coolant temperature, so warm-engine readings can't be told apart.");
        if (!BaselineSignals.Has(log, "stft1+ltft1") && !BaselineSignals.Has(log, "maf"))
            problems.Add("The log needs fuel trims (short- and long-term, bank 1) or air mass.");
        if (problems.Count > 0) return problems;

        if (log.Duration < TimeSpan.FromSeconds(60)) problems.Add($"The log is only {log.Duration.TotalSeconds:0} s long. Record at least a minute, ideally a 10-minute drive.");
        var cache = new Dictionary<string, LogChannel?>();
        var warm = Enumerable.Range(0, log.Count)
            .Select(i => (rpm: BaselineSignals.At(log, "rpm", i, cache), coolant: BaselineSignals.At(log, "coolant", i, cache)))
            .Where(x => x.rpm is > 300 && x.coolant is > 70).Select(x => x.rpm!.Value).ToList();
        if (warm.Count < MinWarmSamples)
            problems.Add($"Only {warm.Count} readings with a warm engine (coolant above 70 °C, engine running); at least {MinWarmSamples} are needed. Record after the engine has warmed up.");
        else
        {
            if (warm.Count(r => r < 1000) < MinIdleSamples)
                problems.Add($"Not enough warm idle: let it idle for at least {MinIdleSamples / 2} seconds in the log.");
            if (warm.Max() < MinTopRpm)
                problems.Add($"The log only reaches {warm.Max():0} rpm with a warm engine. Include some driving that goes past {MinTopRpm} rpm: the wider the range, the more useful the baseline.");
            var bins = warm.Select(r => Math.Round(r / 250) * 250).Distinct().Count();
            if (bins < MinBins) problems.Add($"The log covers only {bins} engine speed steps of 250 rpm; at least {MinBins} are needed. Drive through a range of speeds.");
        }
        return problems;
    }

    /// <summary>The contribution file: comment lines naming the engine, then the values Klemme15's baselines use.</summary>
    public static string ToCsv(DataLog log, string engine, string? chassis, string? model, string version)
    {
        var cache = new Dictionary<string, LogChannel?>();
        var present = Columns.Where(c => BaselineSignals.Has(log, c)).ToList();
        var sb = new StringBuilder();   // always \n line ends, so the file is the same on every system
        sb.Append(Marker).Append('\n');
        sb.Append($"# engine: {engine.ToUpperInvariant()}").Append('\n');
        if (chassis is { Length: > 0 }) sb.Append($"# chassis: {Clean(chassis)}").Append('\n');
        if (model is { Length: > 0 }) sb.Append($"# model: {Clean(model)}").Append('\n');
        sb.Append($"# klemme15: {Clean(version)}").Append('\n');
        sb.Append("time," + string.Join(",", present)).Append('\n');
        for (var i = 0; i < log.Count; i++)
        {
            var values = present.Select(c => BaselineSignals.At(log, c, i, cache)).ToList();
            if (values.All(v => v is null)) continue;
            sb.Append(log.Times[i].TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
            foreach (var v in values) sb.Append(',').Append(v?.ToString("0.###", CultureInfo.InvariantCulture) ?? "");
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string Clean(string s) => new(s.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '.' or '-').Take(40).ToArray());

    /// <summary>The form on GitHub where the file is attached; the intake pipeline does the rest.</summary>
    public static string IssueUrl(string engine) =>
        "https://github.com/Expos-AI/Klemme15/issues/new?template=healthy-baseline.yml&title=" + Uri.EscapeDataString($"Baseline: {engine.ToUpperInvariant()}");
}
