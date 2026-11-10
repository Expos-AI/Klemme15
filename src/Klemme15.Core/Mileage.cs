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
using System.Text.RegularExpressions;

namespace Klemme15.Core;

/// <summary>What a mileage figure is.</summary>
public enum MileageKind
{
    /// <summary>A module's own running count of the car's mileage (engine computer, cluster).</summary>
    Current,

    /// <summary>The mileage when a module was last programmed: the car has driven at least this far.</summary>
    Programmed,

    /// <summary>The mileage a fault or dash message was recorded at.</summary>
    Event,

    /// <summary>Shown for reference only: how this module stores mileage is pending validation.</summary>
    Reference,
}

/// <summary>One mileage figure a module remembers.</summary>
public sealed record MileageRecord(string Module, string What, MileageKind Kind, long Km)
{
    /// <summary>Whether the figure takes part in the verdict (reference figures are only shown).</summary>
    public bool CountsForVerdict => Kind != MileageKind.Reference;
}

/// <summary>
/// The only jobs Klemme15 runs to read mileage. Every one of them only reads (KWP2000 $21, $22 or $23, per BMW's own
/// job descriptions). Klemme15 never writes, resets or programs mileage: tests fail if a job that could is ever added.
/// </summary>
public static class MileageJobs
{
    /// <summary>A read job, the result that holds the mileage, and which modules it applies to.</summary>
    public sealed record Job(string Name, string Arguments, string Result, MileageKind Kind, string What, Func<EcuDefinition, bool> AppliesTo);

    private static bool IsCluster(EcuDefinition e) => e.Id.EndsWith(".KOMBI", StringComparison.OrdinalIgnoreCase) || e.Id.EndsWith(".IKE", StringComparison.OrdinalIgnoreCase);
    private static bool IsKeyModule(EcuDefinition e) => e.Id.EndsWith(".CAS", StringComparison.OrdinalIgnoreCase);

    public static readonly IReadOnlyList<Job> All =
    [
        // The engine computer's own count ($22 40 22, "Batterie Powermanagement lesen"): survives a wound-back cluster.
        new("STATUS_SYSTEMCHECK_PM_INFO_1", "", "STAT_KM_STAND_AKTUELL_WERT", MileageKind.Current, "Engine computer's own mileage count", e => e.IsEngine),
        // The cluster's stored mileage from its service data ($21 B0), in 20 km steps (E6x, E7x, E8x, E9x clusters).
        new("STATUS_BETRIEBSDATEN_HEADER", "", "STAT_AKT_KM_STAND_WERT", MileageKind.Current, "Cluster's stored mileage", IsCluster),
        // The E46-era cluster's total mileage.
        new("STATUS_CAN_SIGNALE_LESEN", "", "STAT_KILOMETERSTAND_WERT", MileageKind.Current, "Cluster's total mileage", IsCluster),
        // Mileage when the module was last programmed ($23 ReadMemoryByAddress, the "Anwender-Info-Feld"), any module.
        new("AIF_LESEN", "0", "AIF_KM", MileageKind.Programmed, "Mileage when last programmed", _ => true),
        new("AIF_LESEN", "0", "AIF_KM_STAND", MileageKind.Programmed, "Mileage when last programmed", _ => true),
        // The key module's vehicle state ($22 1015). Shown only: its unit is pending validation.
        new("STATUS_FZG_ZUSTAND", "", "MILE_KM", MileageKind.Reference, "Key module's vehicle state", IsKeyModule),
    ];

    /// <summary>Values from Klemme15's own engine value tables (no BMW files), where the engine lists one.</summary>
    public static readonly IReadOnlyList<string> NativeResults = ["STAT_KILOMETERSTAND_WERT"];

    /// <summary>Words that mark a job that writes, resets, programs or adjusts something. None may appear in <see cref="All"/>.</summary>
    public static readonly IReadOnlyList<string> WriteWords =
        ["SCHREIBEN", "STEUERN", "PROGRAMM", "LOESCHEN", "RESET", "ABGLEICH", "SETZEN", "WRITE", "_FS_", "CODIER", "FLASH", "INIT"];

    /// <summary>True when a job name only reads: AIF_LESEN, IDENT_AIF or a STATUS_ job, with none of the write words.</summary>
    public static bool IsReadOnly(string job) =>
        Regex.IsMatch(job, "^(AIF_LESEN|IDENT_AIF|STATUS_[A-Z0-9_]+)$")
        && !WriteWords.Any(w => job.Contains(w, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Reads the mileage every module remembers, using only <see cref="MileageJobs"/>.</summary>
public static class MileageReader
{
    /// <summary>Above this a figure is an unset or damaged field, not mileage.</summary>
    public const long MaxPlausibleKm = 2_000_000;

    /// <summary>
    /// With BMW files (EDIABAS mode) every module's figures; without them, the engine computer's count where Klemme15's
    /// own engine tables list it. Modules that don't have a job simply give no figure. Read-only.
    /// </summary>
    public static async Task<IReadOnlyList<MileageRecord>> ReadAsync(object? backend, IModuleReadings? readings,
        IEnumerable<EcuDefinition> answered, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var records = new List<MileageRecord>();
        var modules = answered.ToList();
        if (backend is IJobRunner jobs)
        {
            foreach (var ecu in modules)
            {
                progress?.Report($"Reading the mileage {ecu.Name} remembers...");
                var sgbd = readings?.VariantOf(ecu) ?? ecu.Sgbd.FirstOrDefault() ?? $"D_{ecu.Address:X4}";
                foreach (var job in MileageJobs.All.Where(j => j.AppliesTo(ecu)))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!MileageJobs.IsReadOnly(job.Name)) continue;   // belt and braces: never run anything else
                    if (records.Any(r => r.Module == ecu.Name && r.What == job.What)) continue;   // AIF_KM or AIF_KM_STAND, not both
                    IReadOnlyList<IReadOnlyDictionary<string, string>> sets;
                    try { sets = await jobs.RunJobAsync(sgbd, job.Name, job.Arguments, ct); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { continue; }   // the module doesn't have this job
                    var value = sets.SelectMany(s => s).FirstOrDefault(kv => kv.Key.Equals(job.Result, StringComparison.OrdinalIgnoreCase)).Value;
                    if (Parse(value) is { } km) records.Add(new(ecu.Name, job.What, job.Kind, km));
                }
            }
        }
        else if (readings is not null && modules.FirstOrDefault(m => m.IsEngine) is { } engine)
        {
            foreach (var result in MileageJobs.NativeResults)
            {
                try
                {
                    var values = await readings.ReadAsync(engine, result, ct);
                    if (values.FirstOrDefault() is { } v && Parse(v.Number?.ToString(CultureInfo.InvariantCulture) ?? v.Text) is { } km)
                        records.Add(new(engine.Name, "Engine computer's own mileage count", MileageKind.Current, km));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { /* not in this engine's table */ }
            }
        }
        return records;
    }

    /// <summary>"182400", "182400.0", "182.400 km", "182,400" → 182400; zero, unset or implausible → null.</summary>
    public static long? Parse(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : PurchaseCheck.ParseKm(text) is { } km && km is > 0 and < MaxPlausibleKm ? km : null;
}

/// <summary>Compares every mileage figure the car remembers with each other and with the odometer.</summary>
public static class MileageAnalysis
{
    /// <summary>Records are stored in steps (cluster: 20 km) and some modules update once a day; more than this can't be rounding.</summary>
    public const long Tolerance = 500;

    /// <summary>Below the odometer by more than this, a running count points at a replaced or reset module.</summary>
    public const long ReplacedBelow = 5_000;

    /// <summary>The mileage stamps on faults and dash messages from a quick test.</summary>
    public static IReadOnlyList<MileageRecord> FromScan(VehicleScanResult scan)
    {
        var records = new List<MileageRecord>();
        foreach (var e in scan.Ecus.Where(e => e.Status is EcuScanStatus.Ok or EcuScanStatus.Faults))
            foreach (var f in e.Faults)
                if (f.Details.TryGetValue("Mileage", out var text) && MileageReader.Parse(text) is { } km)
                    records.Add(new(e.Ecu.Name, $"Fault {f.Code} recorded", MileageKind.Event, km));
        foreach (var m in scan.CheckControl.Where(m => m.MileageKm is > 0 and < MileageReader.MaxPlausibleKm))
            records.Add(new("Instrument cluster", $"Dash message {m.Code} ({m.Title}) shown", MileageKind.Event, m.MileageKm!.Value));
        return records;
    }

    /// <summary>The findings for a buyer, worst first, and what couldn't be checked.</summary>
    public static (IReadOnlyList<PurchaseItem> Items, IReadOnlyList<string> NotChecked) Assess(IReadOnlyList<MileageRecord> records, long? odometerKm, bool miles = false)
    {
        string F(long km) => miles ? $"{km / DisplayUnits.KmPerMile:N0} mi ({km:N0} km)" : $"{km:N0} km";
        var items = new List<PurchaseItem>();
        var notChecked = new List<string>();
        var counted = records.Where(r => r.CountsForVerdict).ToList();
        var current = counted.Where(r => r.Kind == MileageKind.Current).ToList();

        if (current.Count == 0)
            notChecked.Add("The modules' own mileage counts (engine computer and cluster) weren't read. Use \"Read the mileage from every module\" " +
                           "(or klemme15 mileage) with your BMW files (EDIABAS mode); without them, only engines whose value tables list it give a figure.");
        if (counted.Count == 0)
        {
            notChecked.Add("Mileage cross-check: no module reported a mileage figure on this connection.");
            return (items, notChecked);
        }

        // 1. Anything the car remembers above the odometer.
        if (odometerKm is { } odo)
        {
            var above = counted.Where(r => r.Km > odo + Tolerance).OrderByDescending(r => r.Km).ToList();
            if (above.Count > 0)
            {
                var top = above[0];
                var engine = above.FirstOrDefault(r => r.Kind == MileageKind.Current && r.What.StartsWith("Engine", StringComparison.Ordinal));
                var lead = engine is not null
                    ? $"The engine computer has counted {F(engine.Km)}, but the odometer shows {F(odo)}. The engine computer keeps its own count, so this is the strongest sign that the odometer was wound back or the cluster was replaced."
                    : $"{top.Module}: {Lower(top.What)} at {F(top.Km)}, but the odometer shows {F(odo)}.";
                var more = above.Count > 1 ? $" {above.Count} figures in all are higher than the odometer." : "";
                items.Add(new(CheckLevel.Warn, "Mileage doesn't add up",
                    lead + more + " Either the odometer was wound back, or a module came from a higher-mileage car. Ask for the service history and for any cluster or module replacement; if neither explains it, walk away."));
            }
        }

        // 2. The running counts against each other: a cluster far below the engine computer is the classic rollback.
        var engineCount = current.Where(r => r.What.StartsWith("Engine", StringComparison.Ordinal)).MaxBy(r => r.Km);
        var clusterCount = current.Where(r => r.What.StartsWith("Cluster", StringComparison.Ordinal)).MaxBy(r => r.Km);
        if (engineCount is not null && clusterCount is not null)
        {
            var gap = engineCount.Km - clusterCount.Km;
            if (gap > Tolerance)
                items.Add(new(CheckLevel.Warn, "The engine computer has counted more than the cluster",
                    $"Engine computer {F(engineCount.Km)}, cluster {F(clusterCount.Km)}: {F(gap)} apart. Either the cluster's mileage was wound back, or the cluster came from another car. Ask why."));
            else if (-gap > ReplacedBelow)
                items.Add(new(CheckLevel.Info, "The engine computer has counted less than the cluster",
                    $"Engine computer {F(engineCount.Km)}, cluster {F(clusterCount.Km)}. That usually means the engine computer was replaced or reset at some point, not a wound-back odometer. Ask for the repair history."));
        }
        if (odometerKm is { } o && engineCount is not null && o - engineCount.Km > ReplacedBelow && clusterCount is null)
            items.Add(new(CheckLevel.Info, "The engine computer has counted less than the odometer",
                $"Engine computer {F(engineCount.Km)}, odometer {F(o)}. That usually means the engine computer was replaced or reset at some point. Ask for the repair history."));

        // 3. Nothing out of line.
        if (!items.Any(i => i.Level == CheckLevel.Warn))
        {
            var highest = counted.MaxBy(r => r.Km)!;
            var kinds = current.Count > 0 ? $"{counted.Count} mileage figures, including {current.Count} running count{(current.Count == 1 ? "" : "s")}, agree" : $"{counted.Count} mileage figures agree";
            items.Add(new(CheckLevel.Good, "Mileage records are consistent",
                $"{kinds}. The highest is {F(highest.Km)} ({highest.Module}: {Lower(highest.What)})." +
                (odometerKm is null ? " Enter the odometer reading to compare: it should be the same or a little higher." : $" The odometer shows {F(odometerKm.Value)}.") +
                " This is a strong check, not proof: a professional rollback that also resets the engine computer can pass."));
        }
        return (items, notChecked);
    }

    private static string Lower(string s) => s.Length > 0 ? char.ToLowerInvariant(s[0]) + s[1..] : s;
}
