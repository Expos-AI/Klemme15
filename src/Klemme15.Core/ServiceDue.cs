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

namespace Klemme15.Core;

public enum ServiceDueLevel { Ok, Soon, Due, Unknown }

/// <summary>One item the instrument cluster tracks for Condition Based Service (oil, brake pads, brake fluid...).</summary>
/// <param name="Key">BMW's short name for the item ("Oel", "Br_v"), as the cluster reports it.</param>
/// <param name="Percent">How much of the interval is left, 100 right after a reset. Null when the cluster didn't say.</param>
/// <param name="Remaining">The figure the cluster reports for what's left, in <paramref name="Unit"/> (as reported).</param>
/// <param name="Resets">How many times the item has been reset.</param>
public sealed record ServiceDueItem(string Key, string Name, int? Percent, long? Remaining, string Unit, int? Resets, int? DueMonth, int? DueYear)
{
    public ServiceDueLevel Level => Percent switch
    {
        null => ServiceDueLevel.Unknown,
        <= 0 => ServiceDueLevel.Due,
        <= ServiceDue.SoonPercent => ServiceDueLevel.Soon,
        _ => ServiceDueLevel.Ok,
    };

    /// <summary>"March 2027" for items the cluster tracks by date (brake fluid, vehicle check), or empty.</summary>
    public string DueDate => DueMonth is >= 1 and <= 12 && DueYear is { } y
        ? new DateTime(y, DueMonth.Value, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture) : "";

    public string LevelText => Level switch
    {
        ServiceDueLevel.Due => "Due now",
        ServiceDueLevel.Soon => "Due soon",
        ServiceDueLevel.Ok => "OK",
        _ => "Not reported",
    };

    /// <summary>One line: "62 % of the interval left · 14,000 km · by March 2027 · reset 3 times".</summary>
    public string Summary()
    {
        var parts = new List<string>();
        if (Percent is { } p) parts.Add($"{Math.Max(0, p)} % of the interval left");
        if (Remaining is { } r && Unit.Trim().Equals("km", StringComparison.OrdinalIgnoreCase))
            parts.Add(r < 0 ? $"overdue by {DisplayUnits.Distance(-r)}" : $"{DisplayUnits.Distance(r)} to go");
        if (DueDate.Length > 0) parts.Add($"by {DueDate}");
        if (Resets is > 0) parts.Add($"reset {Resets} time{(Resets == 1 ? "" : "s")}");
        return parts.Count == 0 ? "The cluster didn't report a figure for this item." : string.Join(" · ", parts);
    }
}

/// <summary>
/// Reads what the instrument cluster says is due for service (BMW's Condition Based Service, on the E9x, E8x, E60, E70
/// and E84/E89 families). It only reads: the job is the cluster's own CBS_DATEN_LESEN, run from the owner's BMW files in
/// EDIABAS mode. Resetting an item is a separate service function.
/// </summary>
public static class ServiceDue
{
    /// <summary>The one job this runs. It reads; nothing is changed.</summary>
    public const string Job = "CBS_DATEN_LESEN";

    /// <summary>At or below this share of the interval, an item counts as due soon (Klemme15's own rule of thumb).</summary>
    public const int SoonPercent = 15;

    /// <summary>BMW's short names for the items, in plain English (from the cluster's own item table).</summary>
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Oel"] = "Engine oil",
        ["Br_v"] = "Front brake pads",
        ["Br_h"] = "Rear brake pads",
        ["Brfl"] = "Brake fluid",
        ["Filt"] = "Cabin microfilter",
        ["CSF"] = "Diesel particulate filter",
        ["Batt"] = "Battery",
        ["VTG"] = "Transfer case oil",
        ["ZKrz"] = "Spark plugs",
        ["Sic"] = "Vehicle check",
        ["Kfl"] = "Coolant",
        ["Ueb"] = "Pre-delivery check",
        ["DAD"] = "Particulate filter additive",
        ["TUV"] = "Statutory vehicle inspection",
        ["AU"] = "Statutory emissions inspection",
    };

    /// <summary>
    /// The item numbers the modules report (the cluster's own item table). A 2006 E90's cluster reports the number and
    /// the long German name, not the short one, so items are recognised by number first.
    /// </summary>
    public static readonly IReadOnlyDictionary<int, string> KeysByNumber = new Dictionary<int, string>
    {
        [0x01] = "Oel", [0x02] = "Br_v", [0x03] = "Brfl", [0x04] = "Filt", [0x06] = "Br_h", [0x07] = "CSF", [0x08] = "Batt", [0x09] = "VTG",
        [0x10] = "ZKrz", [0x11] = "Sic", [0x12] = "Kfl", [0x14] = "Ueb", [0x16] = "DAD", [0x20] = "TUV", [0x21] = "AU",
    };

    /// <summary>
    /// Germany's statutory inspections (TUV, AU). Every cluster carries them, with placeholder dates on cars sold
    /// elsewhere, so they are left off the list of what's due.
    /// </summary>
    public static bool IsStatutory(string key) => key.Equals("TUV", StringComparison.OrdinalIgnoreCase) || key.Equals("AU", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Who keeps which items. The cluster's own job documents it: the cluster keeps brake fluid, spark plugs, the
    /// vehicle check and coolant; engine oil is kept by the engine computer, the brake pads by the stability control
    /// and the microfilter by the climate control. Each is read with the same job from its own module.
    /// </summary>
    public static bool KeepsServiceItems(EcuDefinition ecu) =>
        IsCluster(ecu) || ecu.IsEngine || ecu.Id.EndsWith(".DSC", StringComparison.Ordinal) || ecu.Id.EndsWith(".IHKA", StringComparison.Ordinal);

    public static bool IsCluster(EcuDefinition ecu) => ecu.Id.EndsWith(".KOMBI", StringComparison.Ordinal);

    /// <summary>Runs the cluster's read job and returns its items. Throws what the backend throws when the job isn't there.</summary>
    public static async Task<IReadOnlyList<ServiceDueItem>> ReadAsync(IJobRunner jobs, string sgbd, CancellationToken ct = default)
    {
        if (JobPolicy.Refusal(sgbd, Job) is { } refusal) throw new DiagnosticException(refusal);
        return Parse(await jobs.RunJobAsync(sgbd, Job, "", ct));
    }

    /// <summary>
    /// The whole car's list: the cluster's items plus the ones other modules keep (oil, brake pads, microfilter). A
    /// module that doesn't have the job, or doesn't answer, is simply left out. Read-only.
    /// </summary>
    public static async Task<IReadOnlyList<ServiceDueItem>> ReadAllAsync(IJobRunner jobs, IEnumerable<(EcuDefinition Ecu, string Sgbd)> modules, CancellationToken ct = default)
    {
        var sets = new List<IReadOnlyDictionary<string, string>>();
        foreach (var (_, sgbd) in modules)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (JobPolicy.Refusal(sgbd, Job) is null) sets.AddRange(await jobs.RunJobAsync(sgbd, Job, "", ct));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) when (e is DiagnosticException or TimeoutException) { /* this module keeps no service items */ }
        }
        return Parse(sets);
    }

    /// <summary>
    /// One item per result set that names a service item. The cluster's older data layout calls the same fields "BOS"
    /// instead of "CBS", so both spellings are read.
    /// </summary>
    public static IReadOnlyList<ServiceDueItem> Parse(IReadOnlyList<IReadOnlyDictionary<string, string>> sets)
    {
        var items = new List<ServiceDueItem>();
        foreach (var set in sets)
        {
            string? Get(string name)
            {
                foreach (var kind in new[] { "CBS", "BOS" })
                {
                    var key = name.Replace("*", kind, StringComparison.Ordinal);
                    foreach (var kv in set) if (kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
                }
                return null;
            }
            // The number is the reliable name: the text is the short code on some clusters and a German phrase on others.
            var text = Get("ID_FN_*_MESS_TEXT")?.Trim();
            var key = Int(Get("ID_FN_*_MESS_WERT")) is { } number && KeysByNumber.TryGetValue(number, out var known) ? known : text;
            if (string.IsNullOrEmpty(key) || IsStatutory(key) || items.Any(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase))) continue;
            var month = Int(Get("ZIEL_MM_WERT"));
            var year = Int(Get("ZIEL_YY_WERT"));
            if (year is >= 0 and < 100) year += 2000;
            var dated = month is >= 1 and <= 12 && year is >= 2000 and <= 2099;
            // What's left: 0x8000 (-32768) means "not kept for this item". The unit is a number: 0 is percent, 1 is
            // distance in steps of 10 km (BMW's own description of the job: "1hex -> km*10").
            var remaining = Long(Get("RMMI_*_WERT"));
            var unit = Int(Get("ST_UN_*_WERT"));
            var km = remaining is { } r && r != -32768 && r != 32768 && unit == 1 ? r * 10 : (long?)null;
            items.Add(new(key, Names.TryGetValue(key, out var name) ? name : key,
                Int(Get("AVAI_*_WERT")) is { } p and >= -100 and <= 100 ? p : null,
                km, km is null ? "" : "km", Int(Get("COU_RSTG_*_MESS_WERT")) is { } n and >= 0 and < 255 ? n : null,
                dated ? month : null, dated ? year : null));
        }
        return items.OrderBy(i => i.Level switch { ServiceDueLevel.Due => 0, ServiceDueLevel.Soon => 1, ServiceDueLevel.Ok => 2, _ => 3 }).ThenBy(i => i.Percent ?? 0).ToList();
    }

    /// <summary>"2 items are due now: Engine oil, Brake fluid." and so on.</summary>
    public static string Headline(IReadOnlyList<ServiceDueItem> items)
    {
        if (items.Count == 0) return "The cluster didn't report any service items.";
        var due = items.Where(i => i.Level == ServiceDueLevel.Due).ToList();
        var soon = items.Where(i => i.Level == ServiceDueLevel.Soon).ToList();
        if (due.Count > 0) return $"{Count(due.Count)} due now: {string.Join(", ", due.Select(i => i.Name))}.";
        if (soon.Count > 0) return $"Nothing is due yet. {Count(soon.Count)} due soon: {string.Join(", ", soon.Select(i => i.Name))}.";
        return "Nothing is due.";
        static string Count(int n) => n == 1 ? "1 item is" : $"{n} items are";
    }

    private static int? Int(string? s) => Long(s) is { } v and >= int.MinValue and <= int.MaxValue ? (int)v : null;

    private static long? Long(string? s) =>
        double.TryParse(s?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && !double.IsNaN(d) && Math.Abs(d) < 1e12 ? (long)Math.Round(d) : null;
}
