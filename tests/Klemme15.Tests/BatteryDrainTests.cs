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
using Klemme15.Data;
using Xunit;

namespace Klemme15.Tests;

public class BatteryDrainTests
{
    private static readonly VehicleDatabase Db = VehicleDatabase.Load(Path.Combine(Path.GetTempPath(), "klemme15-no-overrides"));

    [Fact]
    public void Every_code_and_message_it_looks_for_is_researched()
    {
        foreach (var (module, code) in BatteryDrain.KnownCodes)
            Assert.True(Db.DescribeBmw(module, code) is not null, $"{module} {code} isn't in bmw-codes.json");
        foreach (var id in BatteryDrain.KnownMessages)
            Assert.True(Db.LookupCheckControl(id) is not null, $"Check Control {id} isn't in the catalogue");
    }

    [Fact]
    public void Parked_current_turns_into_days_until_it_wont_start()
    {
        // 85 mA from a full 70 Ah battery: 35 Ah down to half charge takes about 17 days.
        var high = BatteryDrain.Estimate(85, 70);
        Assert.Equal(CheckLevel.Warn, high.Level);
        Assert.InRange(high.DaysUntilNoStart!.Value, 16.5, 17.5);
        Assert.Contains("about 17 days", high.Detail);

        Assert.Equal(CheckLevel.Good, BatteryDrain.Estimate(35, 70).Level);
        Assert.Equal(CheckLevel.Ask, BatteryDrain.Estimate(65, 70).Level);

        // Starting from 12.5 V (75 % charged) leaves half as much: about 8.6 days.
        var part = BatteryDrain.Estimate(85, 70, 12.5);
        Assert.InRange(part.DaysUntilNoStart!.Value, 8, 9);
        Assert.InRange(part.StateOfCharge!.Value, 0.74, 0.76);

        // A battery already at half charge.
        Assert.Equal(0, BatteryDrain.Estimate(85, 70, 12.2).DaysUntilNoStart);
        Assert.Contains("charge it", BatteryDrain.Estimate(85, 70, 12.2).Detail);

        // A large drain reads in hours.
        Assert.Contains("hours", BatteryDrain.Estimate(2000, 70).Detail);
        Assert.Null(BatteryDrain.Estimate(0, 70).DaysUntilNoStart);
    }

    [Fact]
    public void Clues_come_from_faults_dash_warnings_and_voltage()
    {
        var dme = Db.Modules.Single(m => m.Id == "E9x.DME");
        var scan = new VehicleScanResult
        {
            Started = DateTimeOffset.Now, Backend = "t", BatteryVoltage = 12.1,
            Ecus = [new EcuScanResult { Ecu = dme, Status = EcuScanStatus.Faults, Faults = [new Fault { EcuId = dme.Id, Code = "2DED", Source = FaultSource.Bmw }] }],
            CheckControl =
            [
                new CheckControlMessage { Id = 229, Title = "Charge the battery", Active = false, MileageKm = 98_112 },
                new CheckControlMessage { Id = 229, Title = "Charge the battery", Active = false, MileageKm = 97_000 },
                new CheckControlMessage { Id = 67, Title = "Key battery low" },
            ],
        };
        var clues = BatteryDrain.Clues(scan);
        Assert.Contains(clues, c => c.Title.EndsWith("2DED") && c.Level == CheckLevel.Warn && c.Detail.Contains("parked"));
        Assert.Contains(clues, c => c.Title == "Dash warning: Charge the battery" && c.Detail.Contains("2 times") && c.Detail.Contains("98,112"));
        Assert.DoesNotContain(clues, c => c.Title.Contains("Key battery"));     // the key's battery isn't the car's
        Assert.Contains(clues, c => c.Title.StartsWith("Battery at 12.1"));
        Assert.Equal(CheckLevel.Warn, clues[0].Level);
    }

    [Fact]
    public void The_hunt_never_breaks_the_circuit_and_mentions_the_tailgate_on_an_X5()
    {
        var steps = BatteryDrain.Hunt("E9x");
        Assert.True(steps.Count >= 6);
        Assert.Contains(steps, s => s.Detail.Contains("jumper"));
        Assert.Contains("tailgate", BatteryDrain.Hunt("E53")[1].Detail);
        Assert.DoesNotContain("tailgate", steps[1].Detail);
    }
}
