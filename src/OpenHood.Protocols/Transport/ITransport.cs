using System.Diagnostics;
using OpenHood.Core;

namespace OpenHood.Protocols.Transport;

public enum SerialParity { None, Even, Odd }

/// <summary>
/// Byte pipe to the cable. Implemented by the real serial port (OpenHood.Serial) and by the
/// virtual car (Simulation). Kept synchronous on purpose: K-line timing is in milliseconds.
/// </summary>
public interface ITransport : IDisposable
{
    string Name { get; }
    bool IsOpen { get; }
    void Open();
    void Close();
    void Configure(int baudRate, SerialParity parity);
    void SetBreak(bool on);
    void SetDtr(bool on);
    void SetRts(bool on);

    /// <summary>K+DCAN cables report ignition (terminal 15) on DSR. Null if unknown.</summary>
    bool? IgnitionOn { get; }

    void Write(ReadOnlySpan<byte> data);

    /// <summary>Read whatever is available, waiting up to <paramref name="timeout"/> for the first byte. Returns 0 on timeout.</summary>
    int Read(Span<byte> buffer, TimeSpan timeout);

    void DiscardInput();
}

/// <summary>Records every byte through a transport into the session's <see cref="TrafficLog"/>.</summary>
public sealed class LoggingTransport(ITransport inner, TrafficLog log) : ITransport
{
    public ITransport Inner => inner;
    public string Name => inner.Name;
    public bool IsOpen => inner.IsOpen;
    public bool? IgnitionOn => inner.IgnitionOn;
    public void Open() { inner.Open(); log.Note($"Opened {inner.Name}"); }
    public void Close() { inner.Close(); log.Note("Closed"); }
    public void Configure(int baudRate, SerialParity parity) { inner.Configure(baudRate, parity); log.Note($"Line {baudRate} baud, parity {parity}"); }
    public void SetBreak(bool on) => inner.SetBreak(on);
    public void SetDtr(bool on) => inner.SetDtr(on);
    public void SetRts(bool on) => inner.SetRts(on);
    public void DiscardInput() => inner.DiscardInput();
    public void Dispose() => inner.Dispose();

    public void Write(ReadOnlySpan<byte> data)
    {
        log.Add(TrafficDirection.Tx, data);
        inner.Write(data);
    }

    public int Read(Span<byte> buffer, TimeSpan timeout)
    {
        var n = inner.Read(buffer, timeout);
        if (n > 0) log.Add(TrafficDirection.Rx, buffer[..n]);
        return n;
    }
}

public static class TransportExtensions
{
    /// <summary>Read until the bus has been quiet for <paramref name="idleGap"/>, or nothing arrives within <paramref name="firstByte"/>.</summary>
    public static byte[] ReadUntilIdle(this ITransport t, TimeSpan firstByte, TimeSpan idleGap, int maxBytes = 4096)
    {
        var result = new List<byte>(64);
        var buf = new byte[512];
        var n = t.Read(buf, firstByte);
        while (n > 0)
        {
            result.AddRange(buf.AsSpan(0, n).ToArray());
            if (result.Count >= maxBytes) break;
            n = t.Read(buf, idleGap);
        }
        return result.ToArray();
    }
}

/// <summary>Millisecond-accurate delays. Thread.Sleep alone can be 15 ms off on Windows, which breaks K-line init.</summary>
public static class PreciseTime
{
    public static void Delay(double milliseconds)
    {
        if (milliseconds <= 0) return;
        var sw = Stopwatch.StartNew();
        var sleepPart = milliseconds - 16;
        if (sleepPart > 0) Thread.Sleep(TimeSpan.FromMilliseconds(sleepPart));
        while (sw.Elapsed.TotalMilliseconds < milliseconds) Thread.SpinWait(50);
    }
}
