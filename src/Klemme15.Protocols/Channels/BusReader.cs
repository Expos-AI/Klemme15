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
using System.Buffers;
using Klemme15.Protocols.Transport;

namespace Klemme15.Protocols.Channels;

/// <summary>Looks at everything received so far and says whether it holds what the caller waits for.</summary>
internal delegate bool BufferCheck(ReadOnlySpan<byte> received);

internal static class BusReader
{
    /// <summary>No real answer is this long; a cable that floods the line is cut off here.</summary>
    public const int MaxBytes = 64 * 1024;

    /// <summary>
    /// Accumulate bytes until <paramref name="complete"/> says the buffer holds what we need,
    /// or the deadline passes. Polls in short slices so a slow ECU never stalls the caller.
    /// Bytes are collected in one pooled buffer: nothing is copied or allocated per read, only the result at the end.
    /// </summary>
    public static byte[] ReadUntil(ITransport transport, TimeSpan timeout, BufferCheck complete, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var buffer = ArrayPool<byte>.Shared.Rent(1024);
        var count = 0;
        try
        {
            while (sw.Elapsed < timeout && count < MaxBytes)
            {
                ct.ThrowIfCancellationRequested();
                var slice = timeout - sw.Elapsed;
                if (slice > TimeSpan.FromMilliseconds(25)) slice = TimeSpan.FromMilliseconds(25);
                if (slice <= TimeSpan.Zero) break;
                if (count == buffer.Length)
                {
                    var larger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    buffer.AsSpan(0, count).CopyTo(larger);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = larger;
                }
                var n = transport.Read(buffer.AsSpan(count), slice);
                if (n <= 0) continue;
                count += n;
                if (complete(buffer.AsSpan(0, count))) break;
            }
            return buffer.AsSpan(0, count).ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
