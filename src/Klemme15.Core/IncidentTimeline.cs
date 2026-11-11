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

/// <summary>A fault a module stored at a known mileage.</summary>
/// <param name="Supply">The fault is about low or interrupted supply voltage.</param>
public sealed record IncidentFault(string Module, string Code, string Text, bool Active, long Km, bool Supply = false);

/// <summary>
/// One moment in the car's life: what the dash showed (when the cluster remembers it) together with the faults modules
/// stored at about the same mileage.
/// </summary>
public sealed record Incident(long? FromKm, long? ToKm, StoryEvent? Event, IReadOnlyList<IncidentFault> Faults)
{
    public string When => Event is { } e && Faults.Count == 0 ? e.When
        : FromKm is not { } a ? "Earlier"
        : ToKm is { } b && b - a >= IncidentTimeline.SameMomentKm ? $"{DisplayUnits.Distance(a).Replace(" km", "").Replace(" mi", "")} – {DisplayUnits.Distance(b)}"
        : $"~{DisplayUnits.Distance(a)}";

    public string Title => Event?.Title ?? (Modules.Count == 1 ? $"{Modules[0]} stored {Count(Faults.Count)}" : $"{Modules.Count} modules stored faults at the same time");

    public string Explanation
    {
        get
        {
            if (Event is { } e)
                return Faults.Count == 0 ? e.Explanation
                    : $"{e.Explanation} At the same mileage {(Modules.Count == 1 ? Modules[0] : $"{Modules.Count} modules")} stored {Count(Faults.Count)}, so the warning and {(Faults.Count == 1 ? "that fault" : "those faults")} most likely belong to one event.";
            if (Faults.All(f => f.Supply))
                return "Every fault here is about low or interrupted supply voltage: most likely one flat battery, jump start or battery change, not separate defects.";
            return Modules.Count >= 3
                ? "Several modules storing faults at the same mileage usually share one cause. Look for what they have in common (the voltage supply, or one module the others mention) before replacing parts."
                : Modules.Count == 2 ? "Two modules stored faults at the same mileage, so they may share a cause." : "Stored at this mileage.";
        }
    }

    public IReadOnlyList<string> Modules => Faults.Select(f => f.Module).Distinct().ToList();

    /// <summary>"Stability control (DSC) 5DC0 · Instrument cluster (KOMBI) A3B9", or empty.</summary>
    public string FaultLine => string.Join("  ·  ", Faults.Select(f => $"{f.Module} {f.Code}"));

    private static string Count(int n) => n == 1 ? "1 fault" : $"{n} faults";
}

/// <param name="FaultsWithoutMileage">Faults that carry no mileage, so they can't be placed (most faults read without BMW files).</param>
public sealed record IncidentTimelineResult(IReadOnlyList<Incident> Incidents, int FaultsWithMileage, int FaultsWithoutMileage);

/// <summary>
/// Puts the cluster's warning history and the faults modules stored onto one line by mileage, so "what happened at
/// 98,112 km" is answered in one place. It only arranges what the scan already read. Modules record the mileage with a
/// fault in their freeze frame; Klemme15 reads that in EDIABAS mode.
/// </summary>
public static class IncidentTimeline
{
    /// <summary>Modules round and delay the mileage they store, so entries within this distance count as one moment.</summary>
    public const long SameMomentKm = 16;

    public static IncidentTimelineResult Build(VehicleScanResult scan)
    {
        var faults = new List<IncidentFault>();
        var without = 0;
        foreach (var e in scan.Ecus)
            foreach (var f in e.Faults.Where(f => !f.IsInfo && !f.IsShadow))
            {
                if (f.Details.TryGetValue("Mileage", out var text) && MileageReader.Parse(text) is { } km)
                    faults.Add(new(e.Ecu.Name, f.Code, f.DisplayDescription, f.State == FaultState.Present, km, FaultTriage.IsSupply(f)));
                else without++;
            }

        var incidents = new List<Incident>();
        var left = faults.OrderBy(f => f.Km).ToList();
        foreach (var ev in scan.Story?.Events ?? [])
        {
            var mine = ev.FromKm is { } from
                ? left.Where(f => f.Km >= from - SameMomentKm && f.Km <= (ev.ToKm ?? from) + SameMomentKm).ToList()
                : [];
            foreach (var f in mine) left.Remove(f);
            incidents.Add(new(ev.FromKm, ev.ToKm, ev, mine));
        }
        // Faults no dash message goes with: each run of faults within the same few kilometres is one moment.
        for (var i = 0; i < left.Count;)
        {
            var j = i + 1;
            while (j < left.Count && left[j].Km - left[j - 1].Km <= SameMomentKm) j++;
            incidents.Add(new(left[i].Km, left[j - 1].Km, null, left.GetRange(i, j - i)));
            i = j;
        }
        // Oldest first, like the story; moments without a mileage (very old entries) come first.
        return new(incidents.OrderBy(x => x.FromKm ?? long.MinValue).ToList(), faults.Count, without);
    }
}
