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

using System.Diagnostics;
using System.IO.Ports;
using Klemme15.Protocols.Transport;

namespace Klemme15.Serial;

/// <summary>A K+DCAN cable on a serial port (COM3, /dev/ttyUSB0, /dev/cu.usbserial-...).</summary>
public sealed class SerialPortTransport(string portName) : ITransport
{
    private readonly SerialPort _port = new(portName)
    {
        BaudRate = 115200,
        DataBits = 8,
        StopBits = StopBits.One,
        Parity = Parity.None,
        Handshake = Handshake.None,
        ReadTimeout = 50,
        WriteTimeout = 1000,
        ReadBufferSize = 8192,
    };

    /// <summary>
    /// K+DCAN cables report ignition on DSR, but some clones leave it unconnected (always false).
    /// Only trust it when the user has confirmed their cable supports it.
    /// </summary>
    public bool TrustDsrForIgnition { get; init; }

    public string Name => portName;
    public bool IsOpen => _port.IsOpen;

    public bool? IgnitionOn
    {
        get
        {
            if (!_port.IsOpen) return null;
            try
            {
                var dsr = _port.DsrHolding;
                return dsr || TrustDsrForIgnition ? dsr : null;
            }
            catch (Exception) { return null; }
        }
    }

    public void Open()
    {
        try { _port.Open(); }
        catch (UnauthorizedAccessException e)
        {
            throw new Klemme15.Core.DiagnosticException(OperatingSystem.IsLinux()
                ? $"Linux won't let Klemme15 open {portName}. Add yourself to the group that may use serial ports (Ubuntu, Debian, Fedora: sudo usermod -aG dialout $USER; Arch: uucp), then log out and back in."
                : $"{portName} is in use by another program (INPA, ISTA, NCS Expert, a terminal). Close it and try again.", e);
        }
        catch (Exception e) when (e is IOException or ArgumentException)
        {
            throw new Klemme15.Core.DiagnosticException($"{portName} can't be opened: the cable may have been unplugged. Plug it back in, press Refresh and pick its port again. ({e.Message})", e);
        }
        _port.DtrEnable = false;
        _port.RtsEnable = false;
        _port.DiscardInBuffer();
        _port.DiscardOutBuffer();
    }

    public void Close()
    {
        if (_port.IsOpen) _port.Close();
    }

    public void Configure(int baudRate, SerialParity parity)
    {
        _port.BaudRate = baudRate;
        _port.Parity = parity switch
        {
            SerialParity.Even => Parity.Even,
            SerialParity.Odd => Parity.Odd,
            _ => Parity.None,
        };
    }

    public void SetBreak(bool on) => _port.BreakState = on;
    public void SetDtr(bool on) => _port.DtrEnable = on;
    public void SetRts(bool on) => _port.RtsEnable = on;
    public void DiscardInput() => _port.DiscardInBuffer();

    // One request at a time uses the port (the backend serialises the bus), so these two buffers are reused for every
    // write and read instead of allocating a new array each time.
    private byte[] _tx = new byte[512];
    private readonly byte[] _rx = new byte[8192];

    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.Length > _tx.Length) _tx = new byte[data.Length];
        data.CopyTo(_tx);
        _port.Write(_tx, 0, data.Length);
    }

    public int Read(Span<byte> buffer, TimeSpan timeout)
    {
        if (buffer.IsEmpty) return 0;
        var start = Stopwatch.GetTimestamp();
        while (true)
        {
            var available = _port.BytesToRead;
            if (available > 0)
            {
                var n = _port.Read(_rx, 0, Math.Min(Math.Min(available, buffer.Length), _rx.Length));
                _rx.AsSpan(0, n).CopyTo(buffer);
                return n;
            }
            var waited = Stopwatch.GetElapsedTime(start);
            if (waited >= timeout) return 0;
            // Most answers arrive within a couple of milliseconds. Thread.Sleep(1) can take 15 ms on Windows, which would
            // add that to every request, so wait the first 2 ms without sleeping and only then yield the processor.
            if (waited.TotalMilliseconds < 2) Thread.SpinWait(40);
            else Thread.Sleep(1);
        }
    }

    public void Dispose()
    {
        Close();
        _port.Dispose();
    }
}

public sealed record PortInfo(string Name, string Description, bool LikelyKDCan);

public static class PortDiscovery
{
    /// <summary>List serial ports, flagging FTDI adapters (what K+DCAN cables use).</summary>
    public static IReadOnlyList<PortInfo> List()
    {
        var ports = new List<PortInfo>();
        var names = SerialPort.GetPortNames().Distinct().ToList();
        // A Mac lists every adapter twice: /dev/tty.X waits for the cable to raise carrier detect before it opens (a
        // K+DCAN cable never does, so it would hang), /dev/cu.X opens at once. Offer only the cu one.
        if (OperatingSystem.IsMacOS()) names = MacCallOutOnly(names);
        foreach (var name in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var (desc, ftdi) = Describe(name);
            ports.Add(new PortInfo(name, desc, ftdi));
        }
        return ports.OrderByDescending(p => p.LikelyKDCan).ToList();
    }

    /// <summary>Drops /dev/tty.X when /dev/cu.X is listed too.</summary>
    internal static List<string> MacCallOutOnly(IReadOnlyCollection<string> names) =>
        names.Where(n => !(n.StartsWith("/dev/tty.", StringComparison.Ordinal) && names.Contains("/dev/cu." + n["/dev/tty.".Length..]))).ToList();

    private static (string, bool) Describe(string name)
    {
        try
        {
            if (OperatingSystem.IsLinux() && CableInspector.SysfsDirs(Path.GetFileName(name)).Usb is { } usb)
            {
                var vendor = File.ReadAllText(Path.Combine(usb, "idVendor")).Trim();
                var product = File.Exists(Path.Combine(usb, "product")) ? File.ReadAllText(Path.Combine(usb, "product")).Trim() : "USB serial";
                return (product, vendor == "0403");
            }
            if (OperatingSystem.IsMacOS())
                return ("USB serial", name.Contains("usbserial", StringComparison.OrdinalIgnoreCase) && !name.Contains("wchusbserial", StringComparison.OrdinalIgnoreCase));
            if (OperatingSystem.IsWindows() && CableInspector.IsFtdiOnWindows(name))
                return ("USB Serial Port (FTDI)", true);
        }
        catch (Exception) { /* description is cosmetic */ }
        return ("Serial port", false);
    }
}
