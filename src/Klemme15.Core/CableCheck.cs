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

public enum HostOs { Windows, Linux, MacOs, Other }

/// <summary>What the computer knows about the USB serial adapter behind a port, before anything is sent on it.</summary>
public sealed record CableFacts
{
    public required string Port { get; init; }
    public HostOs Os { get; init; }

    /// <summary>USB vendor and product id ("0403", "6001"), when the system says.</summary>
    public string? VendorId { get; init; }
    public string? ProductId { get; init; }
    public string? Serial { get; init; }

    /// <summary>The FTDI latency timer in milliseconds, when it can be read (Windows and Linux).</summary>
    public int? LatencyMs { get; init; }

    /// <summary>False when the port couldn't be opened (another program has it); null when not tried.</summary>
    public bool? PortFree { get; init; }
    public string? PortError { get; init; }
}

public sealed record CableFinding(CheckLevel Level, string Title, string Detail);

/// <summary>
/// The cable check run before connecting: is it an FTDI chip (what K+DCAN cables use), does it show the known signs
/// of a cloned chip, is its latency timer set for speed, is the port free. A clone can only be suspected from these
/// signs, never proven, and the check says so.
/// </summary>
public static class CableCheck
{
    public const string Ftdi = "0403";

    /// <summary>The serial number burnt into a huge number of cloned FT232R chips.</summary>
    public const string ClonedSerial = "A50285BI";

    private static readonly Dictionary<string, string> OtherChips = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1A86"] = "WCH CH340/CH341", ["067B"] = "Prolific PL2303", ["10C4"] = "Silicon Labs CP210x",
    };

    public static IReadOnlyList<CableFinding> Assess(CableFacts f)
    {
        var list = new List<CableFinding>();
        if (f.PortFree == false)
            list.Add(new(CheckLevel.Warn, $"{f.Port} is in use by another program",
                $"Close INPA, Tool32, NCS Expert, a terminal or any other diagnostic app that has the cable open, then try again.{(f.PortError is { Length: > 0 } e ? $" ({e})" : "")}"));

        var isFtdi = string.Equals(f.VendorId, Ftdi, StringComparison.OrdinalIgnoreCase);
        if (isFtdi && f.ProductId == "0000")
            list.Add(new(CheckLevel.Warn, "This chip looks like a counterfeit FTDI that FTDI's driver disabled",
                "Its USB product ID reads 0000: an FTDI driver released in 2014 reprogrammed counterfeit chips this way. It's a cloned chip and won't connect reliably. A cable with a genuine FTDI chip is the fix."));
        // Windows adds the interface letter to the serial ("A50285BIA"), hence StartsWith.
        else if (isFtdi && f.Serial?.StartsWith(ClonedSerial, StringComparison.OrdinalIgnoreCase) == true)
            list.Add(new(CheckLevel.Ask, "This is probably a cloned FTDI chip",
                $"Its serial number, {ClonedSerial}, is shared by a huge number of cloned FT232R chips. Many clones work fine, but they're the most common cause of dropped connections and timeouts. If you see those, try a cable with a genuine FTDI chip before blaming the car."));
        else if (f.VendorId is { } v && OtherChips.TryGetValue(v, out var chip))
            list.Add(new(CheckLevel.Ask, $"This adapter uses a {chip} chip, not FTDI",
                "BMW K+DCAN cables are built around an FTDI chip, and that's what Klemme15 is tested with. Other chips can work on K-line cars but their timing is less dependable. If connections drop, an FTDI cable is the fix."));
        else if (isFtdi)
            list.Add(new(CheckLevel.Good, "FTDI chip", f.Serial is { Length: > 0 } s ? $"An FTDI USB serial chip (serial {s}), as K+DCAN cables use." : "An FTDI USB serial chip, as K+DCAN cables use."));
        else if (f.VendorId is null && f.Os != HostOs.MacOs)
            list.Add(new(CheckLevel.Info, "Couldn't identify the adapter",
                "The system didn't say which chip is behind this port. If it's a Bluetooth or built-in serial port, pick the USB cable's port instead."));

        if (isFtdi && f.ProductId != "0000")
        {
            if (f.LatencyMs is { } ms and > 1)
                list.Add(new(CheckLevel.Ask, $"Your cable latency is set to {ms} ms: set it to 1 ms",
                    f.Os switch
                    {
                        HostOs.Windows => $"The cable holds incoming data for up to {ms} ms before passing it on, which slows every exchange with the car and can cause timeouts on slower modules. Device Manager → Ports (COM & LPT) → USB Serial Port ({f.Port}) → Properties → Port Settings → Advanced → Latency Timer (msec): choose 1, then OK, unplug the cable and plug it back in.",
                        HostOs.Linux => $"The cable holds incoming data for up to {ms} ms before passing it on. Set it to 1 ms with: echo 1 | sudo tee /sys/bus/usb-serial/devices/{Path.GetFileName(f.Port)}/latency_timer (it resets when the cable is unplugged; a udev rule makes it permanent).",
                        _ => $"The cable holds incoming data for up to {ms} ms before passing it on. Set its latency timer to 1 ms in the driver settings.",
                    }));
            else if (f.LatencyMs is 1)
                list.Add(new(CheckLevel.Good, "Latency set to 1 ms", "The cable passes data on immediately: the fastest, most reliable setting."));
            else if (f.Os == HostOs.MacOs)
                list.Add(new(CheckLevel.Info, "Latency can't be checked on macOS",
                    "macOS doesn't let apps read the cable's latency timer. Klemme15 works with the default; if connections drop, try the cable on Windows or Linux to rule it out."));
        }
        return list.OrderByDescending(x => x.Level).ToList();
    }

    /// <summary>Why a car that should answer didn't, for the quick test's "no module answered" message.</summary>
    public static string NoAnswerHelp(bool dcan) => dcan
        ? "No module answered. Check that the cable switch is on D-CAN, the ignition is on (key position 2), and the cable passes its check in the Garage. Some cheap cables have pins 7 and 8 permanently bridged: they work on K-line cars (E46 era) but not on D-CAN cars like the E90 family. If moving the switch changes nothing, the cable is the likely cause."
        : "No module answered. Check that the cable switch is on K-line, the ignition is on (key position 2), and the cable passes its check in the Garage. On E46-era cars the cable needs pins 7 and 8 connected, which is what the K-line position of the switch does.";
}
