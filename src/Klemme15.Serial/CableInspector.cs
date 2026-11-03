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

using System.IO.Ports;
using System.Text.RegularExpressions;
using Klemme15.Core;
using Microsoft.Win32;

namespace Klemme15.Serial;

/// <summary>
/// Reads what the operating system knows about the adapter behind a serial port: USB vendor and product, serial
/// number, and the FTDI latency timer. Windows: the driver's registry entries. Linux: sysfs. macOS: the port name
/// (FTDI's driver puts the serial number in it). Nothing is sent to the cable or the car.
/// </summary>
public static partial class CableInspector
{
    public static CableFacts Read(string port, bool tryOpen)
    {
        var facts = OperatingSystem.IsWindows() ? Windows(port)
            : OperatingSystem.IsLinux() ? Linux(port)
            : OperatingSystem.IsMacOS() ? Mac(port)
            : new CableFacts { Port = port, Os = HostOs.Other };
        if (!tryOpen) return facts;
        try
        {
            using var sp = new SerialPort(port);
            sp.Open();
            sp.Close();
            // Windows can take a moment to release a COM port after Close; give it that before the real connection opens it.
            Thread.Sleep(250);
            return facts with { PortFree = true };
        }
        catch (UnauthorizedAccessException e) { return facts with { PortFree = false, PortError = e.Message }; }
        catch (IOException e) { return facts with { PortFree = false, PortError = e.Message }; }
        catch (Exception) { return facts; }   // can't tell: don't claim it's busy
    }

    // ---------------------------------------------------------------- Windows

    [GeneratedRegex(@"VID_([0-9A-F]{4})[+&]PID_([0-9A-F]{4})(?:\+([^\\]+))?", RegexOptions.IgnoreCase)]
    private static partial Regex UsbIds();

    private static CableFacts Windows(string port)
    {
        var facts = new CableFacts { Port = port, Os = HostOs.Windows };
        if (!OperatingSystem.IsWindows()) return facts;
        try
        {
            // FTDI's own driver: Enum\FTDIBUS\VID_0403+PID_6001+A50285BIA\0000\Device Parameters (PortName, LatencyTimer).
            if (Find(@"SYSTEM\CurrentControlSet\Enum\FTDIBUS", port) is { } ftdi)
                using (ftdi.Parameters)
                    return facts with
                    {
                        VendorId = ftdi.Vid, ProductId = ftdi.Pid, Serial = ftdi.Serial,
                        LatencyMs = ftdi.Parameters.GetValue("LatencyTimer") is int ms ? ms : null,
                    };
            // Other chips: Enum\USB\VID_1A86&PID_7523\<instance>\Device Parameters (PortName).
            if (Find(@"SYSTEM\CurrentControlSet\Enum\USB", port) is { } other)
                using (other.Parameters)
                    return facts with { VendorId = other.Vid, ProductId = other.Pid };
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return facts;
    }

    private sealed record WinDevice(string Vid, string Pid, string? Serial, RegistryKey Parameters);

    private static WinDevice? Find(string root, string port)
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var bus = Registry.LocalMachine.OpenSubKey(root);
        if (bus is null) return null;
        foreach (var device in bus.GetSubKeyNames())
        {
            var m = UsbIds().Match(device);
            if (!m.Success) continue;
            using var dk = bus.OpenSubKey(device);
            if (dk is null) continue;
            foreach (var instance in dk.GetSubKeyNames())
            {
                var parameters = dk.OpenSubKey($@"{instance}\Device Parameters");
                if (parameters?.GetValue("PortName") is string name && name.Equals(port, StringComparison.OrdinalIgnoreCase))
                    return new WinDevice(m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value.ToUpperInvariant(),
                        m.Groups[3].Success ? m.Groups[3].Value : instance, parameters);
                parameters?.Dispose();
            }
        }
        return null;
    }

    /// <summary>Whether a Windows COM port belongs to an FTDI chip (for picking the cable's port by itself).</summary>
    public static bool IsFtdiOnWindows(string port)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var found = Find(@"SYSTEM\CurrentControlSet\Enum\FTDIBUS", port);
            found?.Parameters.Dispose();
            return found is not null;
        }
        catch (Exception) { return false; }
    }

    // ---------------------------------------------------------------- Linux

    private static CableFacts Linux(string port)
    {
        var dev = Path.GetFileName(port);
        string? Read(string? path) { try { return path is not null && File.Exists(path) ? File.ReadAllText(path).Trim() : null; } catch (Exception) { return null; } }
        var (device, usb) = SysfsDirs(dev);
        return new CableFacts
        {
            Port = port, Os = HostOs.Linux,
            VendorId = Read(usb is null ? null : Path.Combine(usb, "idVendor"))?.ToUpperInvariant(),
            ProductId = Read(usb is null ? null : Path.Combine(usb, "idProduct"))?.ToUpperInvariant(),
            Serial = Read(usb is null ? null : Path.Combine(usb, "serial")),
            LatencyMs = int.TryParse(Read(device is null ? null : Path.Combine(device, "latency_timer")), out var ms) ? ms : null,
        };
    }

    /// <summary>
    /// The real sysfs folders for a tty: its "device" (where ftdi_sio keeps latency_timer) and the USB device above it
    /// (idVendor, idProduct, serial). Symlinks are resolved one at a time: .NET collapses ".." in a path before the
    /// system follows links, so "/sys/class/tty/ttyUSB0/device/../.." would point at the wrong folder.
    /// </summary>
    internal static (string? Device, string? Usb) SysfsDirs(string dev, string root = "/sys/class/tty")
    {
        try
        {
            static string Real(string path) => new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
            var tty = Real(Path.Combine(root, dev));
            var device = Path.Combine(tty, "device");
            if (!Directory.Exists(device)) return (null, null);
            device = Real(device);
            for (var d = new DirectoryInfo(device); d is not null && d.FullName.Length > 1; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "idVendor"))) return (device, d.FullName);
            return (device, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return (null, null); }
    }

    // ---------------------------------------------------------------- macOS

    private static CableFacts Mac(string port)
    {
        var name = Path.GetFileName(port);
        // FTDI's driver and Apple's own name FTDI ports /dev/tty.usbserial-<serial>; WCH's driver uses wchusbserial.
        if (name.Contains("wchusbserial", StringComparison.OrdinalIgnoreCase))
            return new CableFacts { Port = port, Os = HostOs.MacOs, VendorId = "1A86" };
        var i = name.IndexOf("usbserial-", StringComparison.OrdinalIgnoreCase);
        return i < 0 ? new CableFacts { Port = port, Os = HostOs.MacOs }
            : new CableFacts { Port = port, Os = HostOs.MacOs, VendorId = CableCheck.Ftdi, VendorGuessed = true, Serial = name[(i + "usbserial-".Length)..] };
    }
}
