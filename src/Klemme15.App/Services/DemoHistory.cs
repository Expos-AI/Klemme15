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

using Klemme15.Core;

namespace Klemme15.App.Services;

/// <summary>
/// A made-up two-month history for the demo car, so the "Health over time" card can be tried without a car:
/// a battery resting a little lower each time, fuel trims creeping lean, and one fault that keeps coming back.
/// </summary>
public static class DemoHistory
{
    public static IReadOnlyList<HealthSnapshot> For(VehicleScanResult scan)
    {
        var faults = scan.Ecus.SelectMany(e => e.Faults.Where(f => FaultTriage.Assess(f).Priority != FaultPriority.Background)
            .Select(f => $"{e.Ecu.Id}:{f.Code}")).Distinct().ToList();
        var recurring = faults.Take(1).ToList();
        var km = scan.Story?.LastKm ?? 184_000;
        double[] rest = [12.64, 12.58, 12.55, 12.47, 12.41, 12.36];
        double[] trim = [0.8, 1.2, 2.6, 4.1, 6.2, 9.6];
        var list = new List<HealthSnapshot>();
        for (var i = 0; i < rest.Length; i++)
        {
            var last = i == rest.Length - 1;
            var running = i % 2 == 1;
            list.Add(new HealthSnapshot
            {
                Time = scan.Started.AddDays(-12 * (rest.Length - 1 - i)),
                CarKey = "demo",
                Km = km - 450 * (rest.Length - 1 - i),
                Voltage = last ? rest[i] : running ? 14.1 : rest[i],
                Rpm = running && !last ? 720 : 0,
                Ltft1 = trim[i],
                Ltft2 = trim[i] - 0.6,
                Faults = last ? faults : i >= 2 ? recurring : [],
                DashMessages = scan.CheckControl.Where(m => m.Active).Select(m => m.Id).ToList(),
            });
        }
        // The trims are only meaningful with the engine running: the demo's last scan is engine-off,
        // so add a running scan just before it.
        list.Insert(list.Count - 1, list[^1] with { Time = list[^1].Time.AddMinutes(-20), Voltage = 14.0, Rpm = 740, Faults = recurring });
        return list;
    }
}
