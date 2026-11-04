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

/// <summary>What kind of moment in the car's life an event was.</summary>
public enum StoryKind { LowVoltage, Puncture, TyrePressure, KeyBattery, BatteryWork, Lights, Engine, Brakes, Service, Other }

/// <summary>One moment the cluster remembers: everything it showed at about the same mileage, and what it most likely meant.</summary>
public sealed record StoryEvent(
    long? FromKm,
    long? ToKm,
    StoryKind Kind,
    string Title,
    string Explanation,
    IReadOnlyList<CheckControlMessage> Messages,
    int Times,
    CcSeverity Severity)
{
    public string When => FromKm is not { } a ? "Earlier" : ToKm is { } b && b - a >= 16 ? $"{a:N0} – {b:N0} km" : $"~{a:N0} km";
}

/// <summary>The car's warning history, told as a story.</summary>
public sealed record CarStoryResult(IReadOnlyList<StoryEvent> Events, long? LastKm, int Entries)
{
    public bool IsEmpty => Events.Count == 0;
}

/// <summary>
/// Turns the cluster's Check Control history (every dash message it showed, with the mileage, oldest first) into events a
/// person understands. Messages logged at the same mileage belong together; several unrelated systems complaining at the
/// same instant is the signature of a voltage dip (weak battery, long crank, jump-start), not of several faults; the same
/// tyre warning repeating over a few kilometres is one puncture. The explanations are deliberately "most likely".
/// </summary>
public static class CarStory
{
    private static readonly HashSet<int> Tyre = [50, 63, 139, 140, 141, 142, 143, 147, 265, 384, 608, 609, 610, 611];
    private static readonly HashSet<int> Puncture = [63, 139, 140, 141, 143, 147, 384, 608, 609, 610, 611];
    private static readonly HashSet<int> Key = [65, 66, 67, 68];
    private static readonly HashSet<int> Battery = [213, 220, 229, 230, 247, 304, 305, 306, 415];
    private static readonly HashSet<int> Clock = [167];
    private static readonly HashSet<int> Engine = [27, 28, 29, 30, 31, 33, 39, 212, 257, 367, 427];
    private static readonly HashSet<int> Brakes = [71, 74, 328, 352, 353];
    private static readonly HashSet<int> Service = [41, 281, 284];

    /// <summary>Entries closer together than this (km) count as the same moment. The cluster stores mileage in 8 km steps.</summary>
    public const int SameMoment = 8;

    public static CarStoryResult Build(IReadOnlyList<CheckControlHistoryEntry> history, ICheckControlCatalog? catalog)
    {
        if (history.Count == 0) return new([], null, 0);
        var ordered = history.Select((h, i) => (h, i)).OrderBy(x => x.h.MileageKm ?? long.MaxValue).ThenBy(x => x.i).Select(x => x.h).ToList();

        // 1. Group by moment.
        var moments = new List<List<CheckControlHistoryEntry>>();
        foreach (var h in ordered)
        {
            var last = moments.LastOrDefault();
            if (last is not null && Close(last[^1].MileageKm, h.MileageKm)) last.Add(h);
            else moments.Add([h]);
        }

        // 2. Classify each moment, then merge repeats of the same story over a short stretch (a puncture nagging for 50 km).
        var events = new List<StoryEvent>();
        foreach (var m in moments)
        {
            var e = Classify(m, catalog);
            var prev = events.LastOrDefault();
            if (prev is not null && prev.Kind == e.Kind && e.Kind is StoryKind.Puncture or StoryKind.TyrePressure or StoryKind.KeyBattery
                && e.FromKm - (prev.ToKm ?? prev.FromKm) <= 200)
            {
                events[^1] = prev with
                {
                    ToKm = e.ToKm ?? e.FromKm,
                    Times = prev.Times + e.Times,
                    Messages = prev.Messages.Concat(e.Messages).DistinctBy(x => x.Id).ToList(),
                    Explanation = Explain(prev.Kind, prev.Messages.Concat(e.Messages).DistinctBy(x => x.Id).ToList(), prev.Times + e.Times),
                };
            }
            else events.Add(e);
        }
        return new(events, ordered.LastOrDefault(h => h.MileageKm is not null)?.MileageKm, history.Count);
    }

    /// <summary>"Key battery low" → "key battery low", but "DSC failed" stays "DSC failed".</summary>
    private static string Lower(string t) => t.Length > 1 && char.IsUpper(t[0]) && !t[1..].Any(char.IsUpper) ? char.ToLowerInvariant(t[0]) + t[1..] : t;

    private static bool Close(long? a, long? b) => a is { } x && b is { } y && Math.Abs(y - x) <= SameMoment;

    private static StoryEvent Classify(List<CheckControlHistoryEntry> moment, ICheckControlCatalog? catalog)
    {
        var ids = moment.Select(m => m.Id).Distinct().ToList();
        var messages = ids.Select(id => CheckControlDescriber.Describe(id, catalog, active: false)).ToList();
        var from = moment.Min(m => m.MileageKm);
        var to = moment.Max(m => m.MileageKm);
        var kind = KindOf(ids);
        var worst = messages.Select(m => m.Severity).DefaultIfEmpty(CcSeverity.Advisory).Max();
        var main = kind == StoryKind.BatteryWork ? messages : messages.Where(m => !Clock.Contains(m.Id)).ToList();
        var explanation = Explain(kind, main, moment.Count(m => !Clock.Contains(m.Id) || kind == StoryKind.BatteryWork));
        if (kind != StoryKind.BatteryWork && messages.Any(m => Clock.Contains(m.Id)))
            explanation += " The clock also needed setting, so the car's battery had been disconnected or gone completely flat.";
        return new(from, to, kind, TitleOf(kind, main), explanation, messages, moment.Count, worst);
    }

    private static StoryKind KindOf(List<int> all)
    {
        // "Set the clock" alongside other messages just says the battery had been off; classify by the others.
        var ids = all.Count > 1 && all.Any(Clock.Contains) && !all.All(Clock.Contains) ? all.Where(id => !Clock.Contains(id)).ToList() : all;
        var systems = ids.Count(id => !Clock.Contains(id));
        // Battery messages, or many unrelated systems at the same instant: the voltage dipped.
        if (ids.Any(Battery.Contains) && systems >= 2 || systems >= 4) return StoryKind.LowVoltage;
        if (ids.All(id => Clock.Contains(id))) return StoryKind.BatteryWork;
        if (ids.Any(Puncture.Contains)) return StoryKind.Puncture;
        if (ids.Any(Tyre.Contains)) return StoryKind.TyrePressure;
        if (ids.All(Key.Contains)) return StoryKind.KeyBattery;
        if (ids.Any(Battery.Contains)) return StoryKind.LowVoltage;
        if (ids.Any(Engine.Contains)) return StoryKind.Engine;
        if (ids.Any(Brakes.Contains)) return StoryKind.Brakes;
        if (ids.Any(Service.Contains)) return StoryKind.Service;
        if (ids.All(id => id is >= 87 and <= 138 or >= 345 and <= 381)) return StoryKind.Lights;
        return StoryKind.Other;
    }

    private static string TitleOf(StoryKind kind, IReadOnlyList<CheckControlMessage> messages) => kind switch
    {
        StoryKind.LowVoltage => "Battery ran low",
        StoryKind.Puncture => "Flat tyre",
        StoryKind.TyrePressure => "Tyre pressure warning",
        StoryKind.KeyBattery => "Key battery low",
        StoryKind.BatteryWork => "Battery disconnected",
        StoryKind.Engine => messages.First(m => Engine.Contains(m.Id)).Title,
        StoryKind.Brakes => messages.First(m => Brakes.Contains(m.Id)).Title,
        StoryKind.Service => "Service was due",
        StoryKind.Lights => messages.Count == 1 ? messages[0].Title : "Lights out",
        _ => messages.Count == 1 ? messages[0].Title : $"{messages[0].Title} and {messages.Count - 1} more",
    };

    private static string Explain(StoryKind kind, IReadOnlyList<CheckControlMessage> messages, int times)
    {
        var names = string.Join(", ", messages.Select(m => Lower(m.Title)).Take(6)) + (messages.Count > 6 ? "…" : "");
        return kind switch
        {
            StoryKind.LowVoltage when messages.Count == 1 =>
                $"The car warned that the battery was low ({names}).",
            StoryKind.LowVoltage =>
                $"{messages.Count} systems complained at the same moment ({names}). That's the signature of a voltage dip " +
                "(a weak or flat battery, a long crank, or a jump-start), not of separate faults.",
            StoryKind.Puncture => times > 1
                ? $"The cluster warned {times} times as the tyre lost pressure: most likely a puncture or a slow leak."
                : "A tyre lost pressure: most likely a puncture or a slow leak.",
            StoryKind.TyrePressure => "The tyre monitor saw a pressure difference. Often cold weather, or a tyre that needed air.",
            StoryKind.KeyBattery => times > 1 ? $"The key reported a weak battery {times} times." : "The key reported a weak battery.",
            StoryKind.BatteryWork => "The clock needed setting, which happens after the battery has been disconnected or ran completely flat.",
            StoryKind.Service => "A scheduled service came due.",
            StoryKind.Lights => $"A bulb or light circuit failed: {names}.",
            _ when messages.Count == 1 && messages[0].Description is { Length: > 0 } d && !messages[0].Unknown => d,
            _ => names.Length > 0 ? char.ToUpperInvariant(names[0]) + names[1..] + "." : "",
        };
    }
}
