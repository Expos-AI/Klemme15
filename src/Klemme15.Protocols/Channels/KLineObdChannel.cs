using System.Diagnostics;
using Klemme15.Core;
using Klemme15.Protocols.Framing;
using Klemme15.Protocols.Transport;

namespace Klemme15.Protocols.Channels;

public enum KLineObdProtocol { None, Iso14230, Iso9141 }

/// <summary>
/// Generic OBD-II on the K-line at 10400 baud. Tries ISO 14230 fast init first, then ISO 9141-2
/// 5-baud init. Keeps the session alive automatically and re-initializes after another protocol
/// (DS2) has used the line.
/// </summary>
public sealed class KLineObdChannel(ITransport transport)
{
    public const int BaudRate = 10400;
    private const byte Functional = 0x33;
    private const byte Tester = 0xF1;

    private readonly Stopwatch _sinceLastTx = new();

    public KLineObdProtocol Protocol { get; private set; }
    public bool IsConnected => Protocol != KLineObdProtocol.None && _sinceLastTx.Elapsed < TimeSpan.FromSeconds(4.5);

    /// <summary>Call when another protocol used the K-line; the OBD session is gone.</summary>
    public void Invalidate() => Protocol = KLineObdProtocol.None;

    public bool Connect(CancellationToken ct = default)
    {
        if (!transport.IsOpen) transport.Open();
        transport.Configure(BaudRate, SerialParity.None);
        transport.SetDtr(false);
        transport.SetRts(false);
        if (FastInit(ct)) return true;
        ct.ThrowIfCancellationRequested();
        return SlowInit(ct);
    }

    private bool FastInit(CancellationToken ct)
    {
        transport.SetBreak(false);
        PreciseTime.Delay(350);                 // W5: bus idle before init
        transport.DiscardInput();
        transport.SetBreak(true);               // TiniL: 25 ms low
        PreciseTime.Delay(25);
        transport.SetBreak(false);              // then 25 ms high
        PreciseTime.Delay(25);
        var start = KwpFrame.Build(Functional, Tester, [0x81], 0xC0);
        transport.Write(start);
        byte[]? positive = null;
        BusReader.ReadUntil(transport, TimeSpan.FromMilliseconds(600), buf =>
        {
            positive = KwpFrame.ParseAll(Echo.Strip(start, buf))
                .FirstOrDefault(f => f.Target == Tester && f.Data.Length > 0 && f.Data[0] == 0xC1)?.Data;
            return positive is not null;
        }, ct);
        if (positive is null) return false;
        Protocol = KLineObdProtocol.Iso14230;
        _sinceLastTx.Restart();
        PreciseTime.Delay(60);
        return true;
    }

    private bool SlowInit(CancellationToken ct)
    {
        transport.SetBreak(false);
        PreciseTime.Delay(2600);                // let ECUs drop any half-started session
        // 0x33 at 5 baud: start bit (0), 8 data bits LSB first, stop bit (1). 200 ms per bit.
        transport.SetBreak(true);
        PreciseTime.Delay(200);
        for (var bit = 0; bit < 8; bit++)
        {
            ct.ThrowIfCancellationRequested();
            transport.SetBreak(((Functional >> bit) & 1) == 0);
            PreciseTime.Delay(200);
        }
        transport.SetBreak(false);
        transport.DiscardInput();

        var sync = BusReader.ReadUntil(transport, TimeSpan.FromMilliseconds(1500), b => b.Length >= 3 && b[^3] == 0x55, ct);
        var idx = Array.LastIndexOf(sync, (byte)0x55);
        if (idx < 0 || idx + 2 >= sync.Length) return false;
        var kb2 = sync[idx + 2];
        PreciseTime.Delay(35);                  // W4
        var inverted = new[] { (byte)~kb2 };
        transport.Write(inverted);
        const byte invertedAddress = unchecked((byte)~Functional);   // 0xCC
        var ack = BusReader.ReadUntil(transport, TimeSpan.FromMilliseconds(500), b => b.Contains(invertedAddress), ct);
        if (!ack.Contains(invertedAddress)) return false;
        Protocol = KLineObdProtocol.Iso9141;
        _sinceLastTx.Restart();
        PreciseTime.Delay(60);
        return true;
    }

    /// <summary>Send an OBD request; returns one payload per answering ECU.</summary>
    public IReadOnlyList<byte[]> Request(ReadOnlySpan<byte> data, CancellationToken ct = default)
    {
        if (!IsConnected && !Connect(ct))
            throw new DiagnosticException("OBD-II init failed: no ECU answered on the K-line. Check ignition and that the cable switch is in K-line mode.");

        var wait = 60 - _sinceLastTx.Elapsed.TotalMilliseconds;      // P3min between requests
        if (wait > 0) PreciseTime.Delay(wait);

        var request = Protocol == KLineObdProtocol.Iso14230
            ? KwpFrame.Build(Functional, Tester, data, 0xC0)
            : Iso9141Frame.BuildRequest(data);
        transport.DiscardInput();
        transport.Write(request);
        var proto = Protocol;
        // Wait for the first answer, then keep listening briefly: the gearbox ECU may answer too.
        var raw = BusReader.ReadUntil(transport, TimeSpan.FromMilliseconds(300),
            buf => Parse(proto, Echo.Strip(request, buf)).Count > 0, ct);
        var more = transport.ReadUntilIdle(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(30));
        _sinceLastTx.Restart();
        return Parse(proto, Echo.Strip(request, [.. raw, .. more]));
    }

    private static List<byte[]> Parse(KLineObdProtocol proto, ReadOnlySpan<byte> body) =>
        proto == KLineObdProtocol.Iso14230
            ? KwpFrame.ParseAll(body).Where(f => f.Target == Tester).Select(f => f.Data).ToList()
            : Iso9141Frame.ParseResponses(body);
}
