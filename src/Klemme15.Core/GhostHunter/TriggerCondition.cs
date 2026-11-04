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
using System.Text.Json.Serialization;

namespace Klemme15.Core.GhostHunter;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TriggerOperator { Above, AtOrAbove, Below, AtOrBelow, Equal, NotEqual }

/// <summary>
/// One condition on one live value: "rpm above 4000", "voltage below 11.5 for 1 second". The value has to hold for
/// <see cref="HoldDuration"/> (measured on the readings' own timestamps) before it counts, so a single noisy reading
/// can't fire a capture.
/// </summary>
public sealed record TriggerCondition
{
    /// <summary>A live signal id, e.g. "rpm", "coolant", "voltage", "rough3", "stft1".</summary>
    public required string Parameter { get; init; }
    public required TriggerOperator Operator { get; init; }
    public required double Threshold { get; init; }
    public TimeSpan HoldDuration { get; init; } = TimeSpan.FromSeconds(1);

    public bool Test(double value) => Operator switch
    {
        TriggerOperator.Above => value > Threshold,
        TriggerOperator.AtOrAbove => value >= Threshold,
        TriggerOperator.Below => value < Threshold,
        TriggerOperator.AtOrBelow => value <= Threshold,
        TriggerOperator.Equal => Math.Abs(value - Threshold) < 1e-9,
        TriggerOperator.NotEqual => Math.Abs(value - Threshold) >= 1e-9,
        _ => false,
    };

    public static string Symbol(TriggerOperator op) => op switch
    {
        TriggerOperator.Above => ">", TriggerOperator.AtOrAbove => ">=", TriggerOperator.Below => "<",
        TriggerOperator.AtOrBelow => "<=", TriggerOperator.Equal => "=", _ => "!=",
    };

    public override string ToString() =>
        $"{Parameter} {Symbol(Operator)} {Threshold.ToString(CultureInfo.InvariantCulture)}" +
        (HoldDuration > TimeSpan.Zero ? $" for {HoldDuration.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s" : "");
}

/// <summary>
/// A named rule: all of its conditions must hold at once (AND). When it fires, the monitor keeps <see cref="Before"/>
/// of history and records <see cref="After"/> more, then writes a snapshot.
/// </summary>
public sealed record TriggerRule
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyList<TriggerCondition> Conditions { get; init; }
    public TimeSpan Before { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan After { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>After a capture, the rule rests this long, so one long event doesn't produce a burst of files.</summary>
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Captures per drive, so a stuck condition can't fill the disk.</summary>
    public int MaxCaptures { get; init; } = 5;

    public IEnumerable<string> Parameters => Conditions.Select(c => c.Parameter).Distinct();

    public string Describe() => string.Join(" and ", Conditions.Select(c => c.ToString()));

    /// <summary>Problems that stop the rule from being armed (empty when it's fine).</summary>
    public IReadOnlyList<string> Validate(IReadOnlySet<string>? available = null)
    {
        var problems = new List<string>();
        if (Conditions.Count == 0) problems.Add("Add at least one condition.");
        foreach (var c in Conditions)
        {
            if (string.IsNullOrWhiteSpace(c.Parameter)) problems.Add("A condition has no value picked.");
            else if (available is not null && !available.Contains(c.Parameter)) problems.Add($"This car doesn't report {c.Parameter} on this connection.");
            if (double.IsNaN(c.Threshold) || double.IsInfinity(c.Threshold)) problems.Add($"{c.Parameter}: the threshold isn't a number.");
            if (c.HoldDuration < TimeSpan.Zero || c.HoldDuration > TimeSpan.FromMinutes(5)) problems.Add($"{c.Parameter}: hold between 0 and 300 seconds.");
        }
        if (Before < TimeSpan.Zero || Before > TimeSpan.FromSeconds(120) || After < TimeSpan.Zero || After > TimeSpan.FromSeconds(120))
            problems.Add("Keep between 0 and 120 seconds before and after.");
        if (MaxCaptures is < 1 or > 50) problems.Add("Between 1 and 50 captures per drive.");
        return problems;
    }
}

/// <summary>
/// A ready-made rule written for owners, not engineers: what it catches, when to use it, and a tip for the drive.
/// Picking one fills the rule builder, so the numbers can still be adjusted.
/// </summary>
public sealed record TriggerTemplate(string Title, string WhenToUse, string DriveTip, TriggerRule Rule);

/// <summary>Ready-made rules for the faults owners chase most. They only use values the engine computer reports.</summary>
public static class TriggerPresets
{
    private static TriggerCondition C(string p, TriggerOperator op, double t, double hold = 1) =>
        new() { Parameter = p, Operator = op, Threshold = t, HoldDuration = TimeSpan.FromSeconds(hold) };

    public static readonly IReadOnlyList<TriggerTemplate> Templates =
    [
        new("Catch an intermittent misfire",
            "A stumble, shudder or flashing engine light that comes and goes.",
            "Drive the way it usually happens: often under load, uphill or accelerating. Captures name the rough cylinder.",
            new() { Id = "misfire", Title = "Intermittent misfire (one cylinder much rougher than the rest)",
                    Conditions = [C("rpm", TriggerOperator.Above, 500, 0), C(DerivedSignals.RoughRatio, TriggerOperator.AtOrAbove, 3, 0.3)] }),
        new("Catch a voltage dip",
            "Random dash warnings, a flickering cluster, iDrive restarting, several unrelated faults at once.",
            "Drive normally, use the heated seats, rear demister and fan. Dips often show on startup or at idle with everything on.",
            new() { Id = "voltage-dip", Title = "Battery voltage dip (random dash warnings)", Conditions = [C("voltage", TriggerOperator.Below, 11.5, 0.5)] }),
        new("Catch a lean condition while driving",
            "Hesitation, lean fault codes (2A82, 2A87, P0171, P0174) that come back after clearing.",
            "Cruise and accelerate gently. A lean spike under way points to fuel delivery or the air mass meter.",
            new() { Id = "lean-spike", Title = "Lean condition under way (either bank)",
                    Conditions = [C("rpm", TriggerOperator.Above, 1500, 0), C(DerivedSignals.StftMax, TriggerOperator.Above, 15, 2)] }),
        new("Catch a lean idle",
            "A bouncing or hunting idle, lean codes that only set when the car sits idling.",
            "Let it idle warm, at lights or in the driveway. Lean only at idle is the classic vacuum leak sign.",
            new() { Id = "lean-idle", Title = "Lean at idle (vacuum leak hunting)",
                    Conditions = [C("rpm", TriggerOperator.Below, 1000, 0), C("coolant", TriggerOperator.Above, 70, 0), C(DerivedSignals.StftMax, TriggerOperator.Above, 12, 3)] }),
        new("Catch a rich condition",
            "Fuel smell, black soot at the tailpipe, rich codes that come and go.",
            "Drive normally. Rich under way can be a leaking injector or a lazy oxygen sensor.",
            new() { Id = "rich-spike", Title = "Rich condition under way (either bank)",
                    Conditions = [C("rpm", TriggerOperator.Above, 1000, 0), C(DerivedSignals.StftMin, TriggerOperator.Below, -15, 2)] }),
        new("Catch overheating",
            "The temperature warning comes on now and then, or the fan roars after a drive.",
            "Drive the way it happens: traffic, a hot day, a long climb.",
            new() { Id = "overheat", Title = "Running hot", Conditions = [C("coolant", TriggerOperator.Above, 112, 2)] }),
        new("Catch a charging dropout",
            "The battery light flickers, or the battery is flat again after driving.",
            "Drive with lights and fan on. Below 13 V for a few seconds while running means the alternator wasn't charging.",
            new() { Id = "charging-low", Title = "Not charging while driving", Conditions = [C("rpm", TriggerOperator.Above, 1000, 0), C("voltage", TriggerOperator.Below, 13.0, 3)] }),
        new("Catch a stall",
            "The engine dies while rolling, at a junction or coming to a stop.",
            "Drive normally. The capture shows what speed, trims and voltage did in the minute before.",
            new() { Id = "stall", Title = "Engine stalls while rolling", Conditions = [C("speed", TriggerOperator.Above, 3, 0), C("rpm", TriggerOperator.Below, 300, 0.5)] }),
    ];

    /// <summary>The rules the page lists for ticking, one per template.</summary>
    public static readonly IReadOnlyList<TriggerRule> All = Templates.Select(t => t.Rule).ToList();

    public static TriggerTemplate? For(string ruleId) => Templates.FirstOrDefault(t => t.Rule.Id == ruleId);
}

/// <summary>
/// Values worked out from others, so a rule can say "any cylinder" or "either bank" without needing an OR:
/// <list type="bullet">
/// <item><c>roughratio</c>: the roughest cylinder against the others (3 or more = a misfire-like cylinder), the same
/// measure as the cylinder roughness test, so it works whatever unit the engine computer reports. Averaged over the last
/// few seconds: single readings jump around on a healthy engine (on a real N52 at idle one reading in ten looked
/// "3× rougher", on a different cylinder each time), while a misfiring cylinder stays out in front.</item>
/// <item><c>roughcyl</c>: which cylinder that is.</item>
/// <item><c>stftmax</c> / <c>stftmin</c>: the leaner / richer bank's short-term trim.</item>
/// </list>
/// </summary>
public sealed class DerivedSignals(ISignalSource inner, TimeProvider? clock = null) : ISignalSource
{
    /// <summary>How far back roughness readings are averaged, and how many it takes before there's a verdict.</summary>
    public static readonly TimeSpan RoughWindow = TimeSpan.FromSeconds(3);
    public const int RoughMinReadings = 3;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Queue<(DateTimeOffset At, double[] Abs)> _rough = new();

    /// <summary>
    /// True once the engine computer has reported roughness for 3 or more cylinders. The roughness values themselves
    /// only appear after a few readings, so a single check of what the car reports asks this instead.
    /// </summary>
    public bool ReportsRoughness { get; private set; }

    public const string RoughRatio = "roughratio", RoughCylinder = "roughcyl", StftMax = "stftmax", StftMin = "stftmin";
    public static readonly IReadOnlySet<string> Ids = new HashSet<string> { RoughRatio, RoughCylinder, StftMax, StftMin };
    private static readonly string[] Rough = Enumerable.Range(1, 8).Select(n => $"rough{n}").ToArray();

    public async Task<IReadOnlyDictionary<string, double>> ReadAsync(IReadOnlyCollection<string> signals, CancellationToken ct = default)
    {
        var wantRough = signals.Contains(RoughRatio) || signals.Contains(RoughCylinder);
        var wantTrim = signals.Contains(StftMax) || signals.Contains(StftMin);
        var request = signals.Where(s => !Ids.Contains(s)).ToList();
        if (wantRough) request.AddRange(Rough);
        if (wantTrim) request.AddRange(["stft1", "stft2"]);
        var read = await inner.ReadAsync(request.Distinct().ToList(), ct);
        var result = read.Where(kv => signals.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (wantRough && Averaged(read) is { } avg && Roughest(avg) is { } r)
        {
            if (signals.Contains(RoughRatio)) result[RoughRatio] = r.Ratio;
            if (signals.Contains(RoughCylinder)) result[RoughCylinder] = r.Cylinder;
        }
        if (wantTrim)
        {
            var trims = new[] { "stft1", "stft2" }.Where(read.ContainsKey).Select(k => read[k]).ToList();
            if (trims.Count > 0)
            {
                if (signals.Contains(StftMax)) result[StftMax] = trims.Max();
                if (signals.Contains(StftMin)) result[StftMin] = trims.Min();
            }
        }
        return result;
    }

    /// <summary>
    /// Adds this reading to the last few seconds' and returns each cylinder's average size (rough1..roughN), or null
    /// until there are enough readings to judge.
    /// </summary>
    private Dictionary<string, double>? Averaged(IReadOnlyDictionary<string, double> read)
    {
        var abs = Enumerable.Range(1, 8).Select(n => read.TryGetValue($"rough{n}", out var v) ? Math.Abs(v) : double.NaN).ToArray();
        if (abs.Count(v => !double.IsNaN(v)) < 3) return null;
        ReportsRoughness = true;
        var now = _clock.GetUtcNow();
        _rough.Enqueue((now, abs));
        while (_rough.Count > 0 && now - _rough.Peek().At > RoughWindow) _rough.Dequeue();
        if (_rough.Count < RoughMinReadings) return null;
        var result = new Dictionary<string, double>();
        for (var i = 0; i < 8; i++)
        {
            var seen = _rough.Select(r => r.Abs[i]).Where(v => !double.IsNaN(v)).ToList();
            if (seen.Count > 0) result[$"rough{i + 1}"] = seen.Average();
        }
        return result;
    }

    /// <summary>The roughest cylinder and how many times rougher it is than the others (needs 3 or more cylinders).</summary>
    public static (int Cylinder, double Ratio)? Roughest(IReadOnlyDictionary<string, double> values)
    {
        var cyl = Enumerable.Range(1, 8).Where(n => values.ContainsKey($"rough{n}")).Select(n => (n, v: Math.Abs(values[$"rough{n}"]))).ToList();
        if (cyl.Count < 3) return null;
        var worst = cyl.MaxBy(c => c.v);
        return (worst.n, RoughnessRatio.Of(worst.v, cyl.Select(c => c.v).ToList()));
    }
}
