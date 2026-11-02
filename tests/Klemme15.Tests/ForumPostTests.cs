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

public class ForumPostTests
{
    [Fact]
    public void Removes_the_vin_everywhere()
    {
        var dme = new EcuDefinition { Id = "E9x.DME", Name = "Engine (DME)", Address = 0x12, Protocol = BusProtocol.BmwFast, IsEngine = true };
        var scan = new VehicleScanResult
        {
            Started = DateTimeOffset.Now,
            Backend = "test",
            Vin = "WBAPH5C55BA123456",
            Chassis = "E90",
            BatteryVoltage = 12.4,
            Ecus =
            [
                new EcuScanResult
                {
                    Ecu = dme,
                    Status = EcuScanStatus.Faults,
                    Identity = new EcuIdentity { Variant = "MSV80" },
                    Faults =
                    [
                        new Fault
                        {
                            EcuId = dme.Id, Code = "2A82", Source = FaultSource.Bmw, State = FaultState.Present,
                            Description = "Intake VANOS, car WBAPH5C55BA123456 (short BA12345)",
                            Details = new Dictionary<string, string> { ["mileage"] = "142000 km", ["status"] = "0x60" },
                        },
                    ],
                },
            ],
        };
        var post = ReportWriter.ToForumPost(scan, "E90 325i");
        Assert.DoesNotContain("WBAPH5C55BA123456", post);
        Assert.DoesNotContain("BA12345", post);
        Assert.Contains("2A82", post);
        Assert.Contains("MSV80", post);
        Assert.Contains("142000 km", post);
        Assert.DoesNotContain("0x60", post);
        Assert.Contains("12.4 V", post);
    }
}
