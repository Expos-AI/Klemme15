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
using Xunit;

namespace Klemme15.Tests;

public class FaultTriageTests
{
    private static Fault F(FaultState state, string text, bool info = false, bool shadow = false, int count = 0) => new()
    {
        EcuId = "E9x.DME", Code = "2A82", Source = FaultSource.Bmw, State = state, Description = text, IsInfo = info || shadow, IsShadow = shadow,
        Details = count > 0 ? new Dictionary<string, string> { ["Occurrences"] = count.ToString() } : new Dictionary<string, string>(),
    };

    [Fact]
    public void Active_faults_need_fixing_now() =>
        Assert.Equal(FaultPriority.FixNow, FaultTriage.Assess(F(FaultState.Present, "Intake VANOS: position not reached")).Priority);

    [Fact]
    public void Info_and_shadow_memory_are_background()
    {
        Assert.Equal(FaultPriority.Background, FaultTriage.Assess(F(FaultState.Present, "Fuel cap", info: true)).Priority);
        Assert.Equal(FaultPriority.Background, FaultTriage.Assess(F(FaultState.Stored, "Exhaust flap", shadow: true)).Priority);
    }

    [Fact]
    public void Stored_leftovers_of_battery_work_are_background_unless_frequent()
    {
        Assert.Equal(FaultPriority.Background, FaultTriage.Assess(F(FaultState.Stored, "Supply voltage too low (Unterspannung)")).Priority);
        Assert.Equal(FaultPriority.Background, FaultTriage.Assess(F(FaultState.Stored, "PT-CAN message from DSC missing")).Priority);
        Assert.Equal(FaultPriority.CheckIt, FaultTriage.Assess(F(FaultState.Stored, "Undervoltage detected", count: 25)).Priority);
    }

    [Fact]
    public void Other_stored_faults_are_worth_checking() =>
        Assert.Equal(FaultPriority.CheckIt, FaultTriage.Assess(F(FaultState.Stored, "Oxygen sensor heater, bank 1")).Priority);

    [Fact]
    public void Active_supply_faults_point_at_the_battery()
    {
        var v = FaultTriage.Assess(F(FaultState.Present, "Battery voltage too low"));
        Assert.Equal(FaultPriority.FixNow, v.Priority);
        Assert.Contains("battery", v.Advice);
    }

    [Fact]
    public void Usually_harmless_codes_are_not_fix_now()
    {
        var active = F(FaultState.Present, "Junction box: latching relay, terminal 30g_f") with { UsuallyHarmless = true };
        Assert.Equal(FaultPriority.CheckIt, FaultTriage.Assess(active).Priority);
        var stored = F(FaultState.Stored, "Odometer difference") with { UsuallyHarmless = true };
        Assert.Equal(FaultPriority.Background, FaultTriage.Assess(stored).Priority);
        var frequent = F(FaultState.Stored, "Odometer difference", count: 30) with { UsuallyHarmless = true };
        Assert.Equal(FaultPriority.CheckIt, FaultTriage.Assess(frequent).Priority);
    }
}
