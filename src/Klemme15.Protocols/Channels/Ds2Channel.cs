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
using Klemme15.Protocols.Framing;
using Klemme15.Protocols.Transport;

namespace Klemme15.Protocols.Channels;

/// <summary>BMW DS2 on the K-line (9600 baud, 8 data bits, even parity). Used by E46-era modules.</summary>
public sealed class Ds2Channel(ITransport transport)
{
    public const int BaudRate = 9600;
    public TimeSpan ResponseTimeout { get; set; } = TimeSpan.FromMilliseconds(800);

    public void Open()
    {
        if (!transport.IsOpen) transport.Open();
        transport.Configure(BaudRate, SerialParity.Even);
        transport.SetBreak(false);
        transport.SetDtr(false);
        transport.SetRts(false);
        transport.DiscardInput();
    }

    /// <summary>Returns the response frame (Data[0] is the status byte) or null on no answer.</summary>
    public Ds2Frame? Request(byte ecu, ReadOnlySpan<byte> data, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var request = Ds2Frame.Build(ecu, data);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            transport.DiscardInput();
            transport.Write(request);
            Ds2Frame? response = null;
            BusReader.ReadUntil(transport, timeout ?? ResponseTimeout, buf =>
            {
                response = Find(Echo.Strip(request, buf), ecu, request);
                return response is not null;
            }, ct);

            if (response is null) return null;
            if (response.Data.Length > 0 && response.Data[0] == Ds2Frame.StatusBusy)
            {
                PreciseTime.Delay(100);
                continue;
            }
            return response;
        }
        return null;
    }

    private static Ds2Frame? Find(ReadOnlySpan<byte> buf, byte ecu, byte[] request)
    {
        foreach (var f in Ds2Frame.ParseAll(buf))
        {
            // A leftover echo parses as a valid frame too, so skip anything identical to our request.
            if (f.Address == ecu && f.Data.Length > 0 && !Ds2Frame.Build(f.Address, f.Data).AsSpan().SequenceEqual(request))
                return f;
        }
        return null;
    }

    public static void EnsureOk(Ds2Frame? response, string what)
    {
        if (response is null) throw new DiagnosticException($"{what}: no answer from the module. Check the ignition is on and try again.");
        var status = response.Data[0];
        if (status == Ds2Frame.StatusOk) return;
        var text = status switch
        {
            Ds2Frame.StatusRejected => "the module refused",
            Ds2Frame.StatusError => "the module reported an error",
            Ds2Frame.StatusBusy => "the module is busy; try again in a moment",
            _ => $"the module answered with status {status:X2}",
        };
        throw new DiagnosticException($"{what}: {text}");
    }
}

/// <summary>DS2 command bytes common to most E-series K-line modules.</summary>
public static class Ds2
{
    public const byte Identify = 0x00;
    public const byte ReadFaultMemory = 0x04;
    public const byte ClearFaultMemory = 0x05;
}
