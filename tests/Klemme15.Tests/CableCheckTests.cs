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

public class CableCheckTests
{
    private static CableFacts Ftdi(string serial = "AB0JQ5ZK", int? latency = 1, HostOs os = HostOs.Windows, string pid = "6001") =>
        new() { Port = "COM3", Os = os, VendorId = "0403", ProductId = pid, Serial = serial, LatencyMs = latency, PortFree = true };

    [Fact]
    public void A_genuine_FTDI_at_1_ms_passes()
    {
        var f = CableCheck.Assess(Ftdi());
        Assert.All(f, x => Assert.Equal(CheckLevel.Good, x.Level));
        Assert.Contains(f, x => x.Title == "Latency set to 1 ms");
    }

    [Fact]
    public void Latency_16_ms_says_exactly_what_to_change()
    {
        var win = CableCheck.Assess(Ftdi(latency: 16));
        var latency = Assert.Single(win, x => x.Title.Contains("latency"));
        Assert.Equal("Your cable latency is set to 16 ms: set it to 1 ms", latency.Title);
        Assert.Contains("Device Manager", latency.Detail);
        Assert.Contains("USB Serial Port (COM3)", latency.Detail);

        var linux = CableCheck.Assess(Ftdi(latency: 16, os: HostOs.Linux) with { Port = "/dev/ttyUSB0" });
        Assert.Contains(linux, x => x.Detail.Contains("/sys/bus/usb-serial/devices/ttyUSB0/latency_timer"));

        var mac = CableCheck.Assess(Ftdi(latency: null, os: HostOs.MacOs));
        Assert.Contains(mac, x => x.Title == "Latency can't be checked on macOS");
        var guessed = CableCheck.Assess(Ftdi(latency: null, os: HostOs.MacOs) with { VendorGuessed = true });
        Assert.Contains(guessed, x => x.Title == "Probably an FTDI chip" && x.Level == CheckLevel.Info);
    }

    [Fact]
    public void Known_clone_signs_are_named_but_not_overstated()
    {
        var cloned = CableCheck.Assess(Ftdi(serial: "A50285BIA"));
        Assert.Contains(cloned, x => x.Level == CheckLevel.Ask && x.Title == "This is probably a cloned FTDI chip");

        var bricked = CableCheck.Assess(Ftdi(pid: "0000"));
        Assert.Equal(CheckLevel.Warn, bricked[0].Level);
        Assert.Contains("0000", bricked[0].Detail);
        Assert.DoesNotContain(bricked, x => x.Title.Contains("latency"));

        var ch340 = CableCheck.Assess(new CableFacts { Port = "COM4", Os = HostOs.Windows, VendorId = "1A86", ProductId = "7523" });
        Assert.Contains(ch340, x => x.Title.Contains("CH340") && x.Level == CheckLevel.Ask);
    }

    [Fact]
    public void A_busy_port_comes_first()
    {
        var f = CableCheck.Assess(Ftdi() with { PortFree = false, PortError = "Access denied" });
        Assert.Equal("COM3 is in use by another program", f[0].Title);
        Assert.Contains("INPA", f[0].Detail);
    }

    [Fact]
    public void No_answer_help_names_the_switch_and_pins_7_and_8()
    {
        Assert.Contains("pins 7 and 8", CableCheck.NoAnswerHelp(dcan: true));
        Assert.Contains("D-CAN", CableCheck.NoAnswerHelp(dcan: true));
        Assert.Contains("K-line", CableCheck.NoAnswerHelp(dcan: false));
    }
}
