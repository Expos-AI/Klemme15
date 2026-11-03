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

/// <summary>One thing the car already says about a battery going flat.</summary>
public sealed record DrainClue(CheckLevel Level, string Title, string Detail);

/// <summary>What a measured parked current means for this battery.</summary>
public sealed record DrainEstimate(CheckLevel Level, string Headline, string Detail, double? DaysUntilNoStart, double? StateOfCharge);

/// <summary>One step of the fuse-pull hunt.</summary>
public sealed record DrainStep(string Title, string Detail);

/// <summary>
/// The battery drain finder: what the car itself recorded about draining its battery (faults, dash warnings, the
/// health history), what a measured parked current means in days until it won't start, and a guided hunt with a
/// multimeter for the circuit responsible. Read-only: nothing is sent to the car.
/// </summary>
public static class BatteryDrain
{
    // Fault codes researched in db/bmw-codes.json, explained here for someone chasing a flat battery.
    private static readonly Dictionary<(string Module, string Code), (CheckLevel Level, string Why)> Codes = new()
    {
        [("E9x.DME", "2DED")] = (CheckLevel.Warn, "The car's power management measured too much current while the car was parked and asleep. This is the car's own parasitic-drain record: the hunt below finds which circuit."),
        [("E9x.DME", "2DEC")] = (CheckLevel.Ask, "Power management logged a battery problem: a tired battery, or one drained by something staying awake."),
        [("E9x.DME", "2E8B")] = (CheckLevel.Ask, "The intelligent battery sensor (IBS, on the battery's negative terminal) isn't giving a valid signal. Without it the car can't manage the battery properly, and it can miss a drain."),
        [("E9x.DME", "2E8C")] = (CheckLevel.Ask, "The intelligent battery sensor reports an internal fault. A faulty sensor can itself keep the car from sleeping."),
        [("E9x.DME", "2E8D")] = (CheckLevel.Ask, "Signals from the intelligent battery sensor are disturbed. Check its plug and wire at the battery's negative terminal."),
        [("E9x.DME", "2E8E")] = (CheckLevel.Ask, "The engine computer lost contact with the intelligent battery sensor. Check its plug and wire at the battery's negative terminal."),
        [("E9x.FRM", "9CB5")] = (CheckLevel.Warn, "The footwell module recorded that the battery ran flat."),
        [("E9x.FRM", "9CB8")] = (CheckLevel.Warn, "The footwell module switched the parking or side lights off to save a battery that was running down."),
        [("E9x.JBE", "A731")] = (CheckLevel.Ask, "The junction box reports a problem with the circuit it cuts off when the parked car keeps waking up or draws too much. A sign the car has been fighting a drain."),
        [("E9x.CIC", "E18D")] = (CheckLevel.Ask, "The head unit failed to wake the network: a module that misbehaves at wake-up can also keep the car from going back to sleep."),
    };

    // Standard OBD-II codes for the charging system voltage, on any car.
    private static readonly Dictionary<string, string> ObdCodes = new()
    {
        ["P0562"] = "The engine computer saw low system voltage: a weak or drained battery, or a charging problem.",
        ["P0560"] = "The engine computer saw a system voltage problem: check the battery, its terminals and the charging.",
    };

    // Check Control messages about the battery (db/check-control.json).
    private static readonly HashSet<int> BatteryMessages = [220, 229, 230, 247, 304, 305, 306, 415];

    /// <summary>The codes and messages above, for the tests that check they're all in the databases.</summary>
    public static IEnumerable<(string Module, string Code)> KnownCodes => Codes.Keys;
    public static IReadOnlyCollection<int> KnownMessages => BatteryMessages;

    public static IReadOnlyList<DrainClue> Clues(VehicleScanResult scan)
    {
        var clues = new List<DrainClue>();
        foreach (var e in scan.Ecus)
            foreach (var f in e.Faults)
            {
                if (Codes.TryGetValue((e.Ecu.Id, f.Code.ToUpperInvariant()), out var c))
                    clues.Add(new(c.Level, $"{e.Ecu.Name}: {f.Code}", c.Why));
                else if (e.Ecu.IsEngine && ObdCodes.TryGetValue(f.Code.ToUpperInvariant(), out var why))
                    clues.Add(new(CheckLevel.Ask, $"{e.Ecu.Name}: {f.Code}", why));
            }

        var warnings = scan.CheckControl.Where(m => BatteryMessages.Contains(m.Id)).ToList();
        foreach (var group in warnings.GroupBy(m => m.Id))
        {
            var first = group.First();
            var active = group.Any(m => m.Active);
            var km = group.Where(m => m.MileageKm is not null).Select(m => m.MileageKm!.Value).DefaultIfEmpty().Max();
            var when = active ? "showing now" : km > 0 ? $"last at about {km.ToString("N0", CultureInfo.InvariantCulture)} km" : "in the cluster's history";
            clues.Add(new(active ? CheckLevel.Warn : CheckLevel.Ask, $"Dash warning: {first.Title}",
                $"The instrument cluster showed it {(group.Count() > 1 ? $"{group.Count()} times, " : "")}{when}."));
        }

        foreach (var t in scan.Health?.Trends ?? [])
            if ((t.Title.Contains("battery", StringComparison.OrdinalIgnoreCase) || t.Title.Contains("charging", StringComparison.OrdinalIgnoreCase))
                && !t.Title.Contains("key", StringComparison.OrdinalIgnoreCase))   // the key's battery isn't the car's
                clues.Add(new(t.Level == TrendLevel.Act ? CheckLevel.Warn : t.Level == TrendLevel.Watch ? CheckLevel.Ask : CheckLevel.Info, t.Title, t.Detail));

        if (scan.BatteryVoltage is { } v && v < 12.4 && !clues.Any(c => c.Title.Contains("resting", StringComparison.OrdinalIgnoreCase)))
            clues.Add(new(v < 12.2 ? CheckLevel.Warn : CheckLevel.Ask, $"Battery at {v.ToString("0.0#", CultureInfo.InvariantCulture)} V",
                "A healthy, charged battery rests at about 12.6 V or more with everything off. If the engine was off for a while before this reading, the battery is low."));
        return clues.OrderByDescending(c => c.Level).ToList();
    }

    /// <summary>
    /// The state of charge of a lead-acid battery at rest (engine off for a few hours), from its voltage: about 12.7 V
    /// full and 11.9 V empty, roughly in a straight line between. A rule of thumb, good to about 10 %.
    /// </summary>
    public static double StateOfCharge(double restingVolts) => Math.Clamp((restingVolts - 11.9) / (12.7 - 11.9), 0, 1);

    /// <summary>
    /// What a parked current means. Rule of thumb for E-series BMWs once asleep: up to about 50 mA is normal, 50 to
    /// 80 mA is worth chasing, more than that drains a battery noticeably. "Days until it won't start" counts down to
    /// half charge, below which a cold start gets doubtful.
    /// </summary>
    public static DrainEstimate Estimate(double drainMilliamps, double batteryAh, double? restingVolts = null)
    {
        if (drainMilliamps <= 0 || batteryAh <= 0 || double.IsNaN(drainMilliamps) || double.IsNaN(batteryAh))
            return new(CheckLevel.Info, "Enter the current and the battery size", "Type the current your meter shows once the car is asleep, in milliamps, and the battery's capacity in Ah (on its label).", null, null);
        var soc = restingVolts is { } rv ? StateOfCharge(rv) : 1.0;
        var usableAh = Math.Max(0, (soc - 0.5) * batteryAh);
        var days = usableAh / (drainMilliamps / 1000.0) / 24;
        string Days(double d) => d >= 60 ? "more than two months" : d >= 1.5 ? $"about {d:0} days" : $"about {d * 24:0} hours";
        var fromWhere = restingVolts is null ? "From full" : $"From {soc:P0} charged ({restingVolts.Value.ToString("0.0#", CultureInfo.InvariantCulture)} V)";
        var (level, headline) = drainMilliamps switch
        {
            <= 50 => (CheckLevel.Good, $"{drainMilliamps:0} mA is a normal parked current"),
            <= 80 => (CheckLevel.Ask, $"{drainMilliamps:0} mA is on the high side: worth chasing"),
            _ => (CheckLevel.Warn, $"{drainMilliamps:0} mA is too much: something is staying awake"),
        };
        var detail = soc <= 0.5
            ? $"The battery is already at or below half charge ({soc:P0}): charge it before measuring, or it may not start at all."
            : $"{fromWhere}, a {batteryAh:0} Ah battery reaches half charge in {Days(days)}, after which a cold start gets doubtful." +
              (level == CheckLevel.Good ? " If it still goes flat, test the battery itself (a tired battery holds far less than its label) and the charging." : "");
        return new(level, headline, detail, soc <= 0.5 ? 0 : days, restingVolts is null ? null : soc);
    }

    /// <summary>The fuse-pull hunt, in order. Same idea on every E-series; the fuse chart is inside each fuse panel's cover.</summary>
    public static IReadOnlyList<DrainStep> Hunt(string? chassis) =>
    [
        new("Charge the battery and get a meter that reads amps",
            "Charge the battery fully first: a drain hunt on a flat battery wakes things and misleads. You need a multimeter with a 10 A range and a mA range, or a DC clamp meter (easiest: nothing to disconnect)."),
        new("Make the car think it's closed",
            $"Open the boot{(chassis is "E53" ? " / tailgate" : "")}, bonnet and a window, then push each open latch closed with a screwdriver so the car sees them shut. Close every door, with a window down so you can reach in, and take the key away from the car (several metres, out of range)."),
        new("Put the meter in the circuit without waking the car",
            "Find the battery (on most of these cars it's in the boot or under the boot floor). With a clamp meter, clamp the negative cable and skip to the next step. With a multimeter on its 10 A range: connect it between the negative battery post and the negative cable with a jumper wire across it first, then loosen and lift the cable, then remove the jumper. The circuit is never broken, so the modules don't restart."),
        new("Wait for the car to fall asleep",
            "The current starts high (hundreds of mA, sometimes amps) and drops in steps as modules go to sleep. Wait until it has been steady for 10 minutes: usually 15 to 30 minutes, sometimes an hour. Don't open a door or press the key meanwhile: that wakes everything and starts the wait again. Once it's low, switch the meter to its mA range the same way (jumper across, switch, jumper off) for a precise reading."),
        new("Read the current and enter it above",
            "Up to about 50 mA is normal once asleep. If it's higher, carry on: one circuit is drawing the extra."),
        new("Pull fuses one at a time and watch the meter",
            "Fuse panels: behind the glovebox, and at the back near the battery (the chart inside each cover says what each fuse feeds). Pull one fuse, watch the meter for 10 seconds, put it back, then the next. Note any fuse whose removal drops the current noticeably: that circuit holds the drain. If pulling a fuse makes the current jump up, a module woke; wait for it to sleep again."),
        new("Narrow it down on that circuit",
            "Common culprits: aftermarket electronics (dashcam, alarm, radio, trackers), a head unit, amplifier or telephone module that doesn't sleep, a boot, glovebox or door light whose switch sticks, a module stuck awake by a fault (check this car's faults on that module), and after a battery change on newer cars, a battery that wasn't registered. Unplug the suspect part and measure again to confirm."),
    ];
}
