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
using Klemme15.Protocols.Transport;

namespace Klemme15.Protocols.Channels;

internal static class BusReader
{
    /// <summary>
    /// Accumulate bytes until <paramref name="complete"/> says the buffer holds what we need,
    /// or the deadline passes. Polls in short slices so a slow ECU never stalls the caller.
    /// </summary>
    public static byte[] ReadUntil(ITransport transport, TimeSpan timeout, Func<byte[], bool> complete, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var buffer = new List<byte>(128);
        var chunk = new byte[512];
        while (sw.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();
            var slice = timeout - sw.Elapsed;
            if (slice > TimeSpan.FromMilliseconds(25)) slice = TimeSpan.FromMilliseconds(25);
            if (slice <= TimeSpan.Zero) break;
            var n = transport.Read(chunk, slice);
            if (n <= 0) continue;
            for (var i = 0; i < n; i++) buffer.Add(chunk[i]);
            var snapshot = buffer.ToArray();
            if (complete(snapshot)) return snapshot;
        }
        return buffer.ToArray();
    }
}
