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

namespace Klemme15.Core.GhostHunter;

public enum RuleReadiness
{
    /// <summary>No reading yet for a value the rule needs.</summary>
    NoReading,
    /// <summary>Something has to change first (warm up, drive faster): the wait isn't the fault.</summary>
    Waiting,
    /// <summary>Everything but the fault itself is in place: if it happens now, it's caught.</summary>
    Ready,
    /// <summary>Every condition holds right now: caught if it lasts its hold time.</summary>
    Holding,
}

/// <summary>Where a watched rule stands right now, in plain words, for the "Right now" list while watching.</summary>
public sealed record RuleProgressLine(string RuleId, string Title, RuleReadiness State, string Text);

/// <summary>
/// Says for each watched rule what it's waiting for, from the latest values: "Waiting for engine speed above 1500 rpm
/// (now 730 rpm)". The last condition of a template is the fault itself; the ones before it set the scene (engine
/// warm, car moving). When only the fault is missing, the rule is ready: that's the normal state while nothing is wrong.
/// </summary>
public static class RuleProgress
{
    public static RuleProgressLine Describe(TriggerRule rule, IReadOnlyDictionary<string, double> values)
    {
        var missing = rule.Conditions.Where(c => !values.ContainsKey(c.Parameter)).Select(c => SignalLabels.Name(c.Parameter)).Distinct().ToList();
        if (missing.Count > 0)
            return new(rule.Id, rule.Title, RuleReadiness.NoReading, $"No reading yet for {string.Join(", ", missing).ToLowerInvariant()}.");

        var unmet = rule.Conditions.Where(c => !c.Test(values[c.Parameter])).ToList();
        if (unmet.Count == 0)
        {
            var hold = rule.Conditions.Max(c => c.HoldDuration);
            return new(rule.Id, rule.Title, RuleReadiness.Holding,
                hold > TimeSpan.Zero ? $"Happening now: caught if it lasts {hold.TotalSeconds:0.#} s." : "Happening now: caught.");
        }

        // The fault is the last condition; the earlier ones are the setting it has to happen in.
        var fault = rule.Conditions[^1];
        var scene = unmet.Where(c => !ReferenceEquals(c, fault)).ToList();
        if (scene.Count == 0)
            return new(rule.Id, rule.Title, RuleReadiness.Ready,
                $"Ready. {Now(fault.Parameter, values[fault.Parameter])}, caught if it goes {Op(fault.Operator)} {Value(fault.Parameter, fault.Threshold)}.");
        return new(rule.Id, rule.Title, RuleReadiness.Waiting,
            "Waiting for " + string.Join(" and ", scene.Select(c => $"{SignalLabels.Name(c.Parameter).ToLowerInvariant()} {Op(c.Operator)} {Value(c.Parameter, c.Threshold)} (now {Value(c.Parameter, values[c.Parameter])})")) + ".");
    }

    private static string Now(string id, double value) => $"{SignalLabels.Name(id)} is {Value(id, value)}";

    public static string Value(string id, double v)
    {
        var unit = SignalLabels.Unit(id);
        var n = v.ToString(Math.Abs(v) >= 100 ? "0" : "0.#", CultureInfo.CurrentCulture);
        return unit switch { "" => n, "×" => n + "×", "%" => n + " %", _ => $"{n} {unit}" };
    }

    private static string Op(TriggerOperator op) => op switch
    {
        TriggerOperator.Above => "above",
        TriggerOperator.AtOrAbove => "to or above",
        TriggerOperator.Below => "below",
        TriggerOperator.AtOrBelow => "to or below",
        TriggerOperator.Equal => "to",
        _ => "away from",
    };
}
