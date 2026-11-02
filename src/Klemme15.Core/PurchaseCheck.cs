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

public enum CheckLevel { Good, Info, Ask, Warn }

/// <summary>One line of a pre-purchase check.</summary>
public sealed record PurchaseItem(CheckLevel Level, string Title, string Detail);

/// <summary>A buyer's read of the car: what looks right, what to ask the seller, what to have checked.</summary>
public sealed record PurchaseReport(IReadOnlyList<PurchaseItem> Items, IReadOnlyList<string> NotChecked)
{
    public int Warnings => Items.Count(i => i.Level == CheckLevel.Warn);
    public int Questions => Items.Count(i => i.Level == CheckLevel.Ask);
    public string Headline => Warnings > 0 ? $"{Warnings} thing{(Warnings == 1 ? "" : "s")} to have checked before buying"
        : Questions > 0 ? $"Nothing alarming; {Questions} question{(Questions == 1 ? "" : "s")} for the seller"
        : "Nothing alarming in what Klemme15 could read";
}

/// <summary>
/// A pre-purchase check from one quick test, written for someone looking at a used car. Read-only, and honest about its
/// limits: it reads what the car's modules remember, it can't see rust, leaks or worn parts, and its mileage check only
/// works with what the modules report.
/// </summary>
public static class PurchaseCheck
{
    /// <summary>Records are stored in steps (the cluster: 8 km); more than this above the odometer can't be rounding.</summary>
    public const long MileageTolerance = 500;

    public static PurchaseReport Assess(VehicleScanResult scan, VehicleModel? model, long? odometerKm = null)
    {
        var items = new List<PurchaseItem>();
        var notChecked = new List<string> { "Rust, leaks, tyres, brakes and suspension wear: look at the car, or have a workshop inspect it." };
        var answered = scan.Ecus.Where(e => e.Status is EcuScanStatus.Ok or EcuScanStatus.Faults).ToList();
        var faults = answered.SelectMany(e => e.Faults.Select(f => (e.Ecu, Fault: f, Verdict: FaultTriage.Assess(f)))).ToList();

        // 1. Mileage: the highest mileage any module or the cluster's history recorded, against the odometer.
        var records = new List<(long Km, string Where)>();
        foreach (var (ecu, f, _) in faults)
            if (f.Details.TryGetValue("Mileage", out var km) && ParseKm(km) is { } v && v > 0) records.Add((v, $"{ecu.Name} fault {f.Code}"));
        foreach (var m in scan.CheckControl.Where(m => m.MileageKm is > 0)) records.Add((m.MileageKm!.Value, $"dash message {m.Code}"));
        if (records.Count == 0)
            notChecked.Add("Mileage cross-check: no module reported mileage with its records on this connection. With your own BMW files (EDIABAS mode), faults carry the mileage they happened at.");
        else
        {
            var highest = records.MaxBy(r => r.Km);
            if (odometerKm is { } odo && highest.Km > odo + MileageTolerance)
                items.Add(new(CheckLevel.Warn, "Mileage doesn't add up",
                    $"{highest.Where} was recorded at {highest.Km:N0} km, but the odometer shows {odo:N0} km. A record can't come from the future: the odometer has very likely been wound back, or the cluster replaced. Ask for the service history and walk away if it doesn't explain this."));
            else
                items.Add(new(CheckLevel.Good, "Mileage records are consistent",
                    $"The highest mileage the car remembers is {highest.Km:N0} km ({highest.Where})." +
                    (odometerKm is null ? " Compare it with the odometer: it should be the same or a little less." : $" The odometer shows {odometerKm:N0} km.")));
        }

        // 2. Faults that matter to a buyer.
        var active = faults.Where(x => x.Verdict.Priority == FaultPriority.FixNow).ToList();
        foreach (var group in active.GroupBy(x => x.Ecu.Category))
        {
            var safety = group.Key is "Safety" or "Chassis";
            items.Add(new(safety ? CheckLevel.Warn : CheckLevel.Ask,
                $"{group.Count()} active fault{(group.Count() == 1 ? "" : "s")} in {group.Key.ToLowerInvariant()} modules",
                string.Join("; ", group.Take(4).Select(x => $"{ShortName(x.Ecu)} {x.Fault.Code}: {x.Fault.DisplayDescription}")) +
                (safety ? ". Airbag, brake and stability faults are safety items: have them fixed or priced before buying." : ". Ask the seller about them, or price the repair in.")));
        }
        if (active.Count == 0)
            items.Add(new(CheckLevel.Good, "No active faults", faults.Count == 0 ? "No module has any fault stored." : "Nothing is wrong right now; stored entries are history."));

        // 3. Memories that look freshly cleared: every module empty and almost no dash history.
        var history = scan.CheckControl.Count(m => !m.Active);
        if (answered.Count >= 5 && faults.Count == 0 && history <= 1)
            items.Add(new(CheckLevel.Ask, "Fault memories look recently cleared",
                "Every module is empty and the cluster remembers almost no warnings. That's normal right after a service or a battery change, but sellers sometimes clear codes to hide a problem. Ask when and why, and check again after a long test drive."));

        // 4. Modules the model should have that didn't answer: failed, unplugged, or removed.
        if (model is { ModuleIds.Count: > 0 })
        {
            var expected = scan.Ecus.Where(e => model.ModuleIds.Contains(e.Ecu.Id) && e.Status is EcuScanStatus.NoResponse).Select(e => e.Ecu.Name).ToList();
            if (expected.Count > 0)
                items.Add(new(CheckLevel.Ask, $"{expected.Count} module{(expected.Count == 1 ? "" : "s")} this model usually has didn't answer",
                    string.Join(", ", expected.Take(6)) + (expected.Count > 6 ? "…" : "") + ". The car may not have that option, or the module is dead or unplugged. Check those features work."));
        }

        // 5. The footwell module: FRM3 is the expensive one that corrupts itself.
        if (answered.FirstOrDefault(e => ShortName(e.Ecu) == "FRM") is { } frm && FrmGuard.Assess(frm.Identity) is { Generation: FrmGeneration.Frm3 })
            items.Add(new(CheckLevel.Info, "Has the FRM3 footwell module",
                "Fine when it works, but it's known to corrupt itself after a voltage dip (lights, windows and mirrors stop). Check every light, window and mirror works, and keep the battery healthy."));

        // 6. Battery.
        if (scan.BatteryVoltage is { } volts)
            items.Add(volts < 12.2
                ? new(CheckLevel.Ask, $"Battery at {volts:0.0} V", "Low for a resting battery. It may just need a drive, or it's worn. A battery for these cars is expensive, and a weak one causes odd faults.")
                : new(CheckLevel.Good, $"Battery at {volts:0.0} V", "A healthy reading."));
        else notChecked.Add("Battery voltage: the engine computer didn't report it.");

        // 7. Dash messages showing now.
        foreach (var m in scan.CheckControl.Where(m => m.Active && m.Severity == CcSeverity.Critical))
            items.Add(new(CheckLevel.Warn, $"Red warning on the dash: {m.Title}", m.Description ?? "Have it checked before buying."));

        // 8. What the car's story says about its life.
        if (scan.Story is { Events.Count: > 0 } story)
        {
            var dips = story.Events.Count(e => e.Kind == StoryKind.LowVoltage);
            if (dips >= 3) items.Add(new(CheckLevel.Ask, $"The battery ran low {dips} times", "The cluster remembers several voltage dips. Ask whether the battery was replaced, and whether the car stood for long periods."));
        }
        else notChecked.Add("The car's story: the cluster gave no warning history on this connection.");

        return new(items.OrderByDescending(i => i.Level).ToList(), notChecked);
    }

    private static string ShortName(EcuDefinition ecu) => ecu.Id.Contains('.') ? ecu.Id[(ecu.Id.LastIndexOf('.') + 1)..] : ecu.Name;

    /// <summary>"123456 km", "123.456 km", "123,456", "123456.0 km" → 123456.</summary>
    public static long? ParseKm(string text)
    {
        var m = Regex.Match(text, @"\d{1,3}(?:[.,\u00A0 ]\d{3})+(?!\d)|\d+");
        if (!m.Success) return null;
        var digits = Regex.Replace(m.Value, @"[^\d]", "");
        return digits.Length is > 0 and <= 7 && long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
