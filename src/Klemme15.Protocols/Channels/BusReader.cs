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
