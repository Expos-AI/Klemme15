using OpenHood.Core;
using OpenHood.Protocols.Framing;
using OpenHood.Protocols.Transport;

namespace OpenHood.Protocols.Channels;

/// <summary>
/// KWP2000 over D-CAN using BMW-FAST framing. The K+DCAN cable (switch in D-CAN position)
/// converts these serial frames to CAN at 115200 baud, so we only deal with frames here.
/// </summary>
public sealed class BmwFastChannel(ITransport transport)
{
    public const int BaudRate = 115200;
    public byte TesterAddress { get; init; } = 0xF1;

    /// <summary>Normal wait for an answer.</summary>
    public TimeSpan ResponseTimeout { get; set; } = TimeSpan.FromMilliseconds(1000);

    /// <summary>Upper limit when an ECU keeps answering "response pending" (7F xx 78).</summary>
    public TimeSpan PendingLimit { get; set; } = TimeSpan.FromSeconds(10);

    public void Open()
    {
        if (!transport.IsOpen) transport.Open();
        transport.Configure(BaudRate, SerialParity.None);
        transport.SetDtr(false);
        transport.SetRts(false);
        transport.DiscardInput();
    }

    /// <summary>
    /// Send one request; returns the ECU's response payload (positive or negative), or null on no answer.
    /// </summary>
    public byte[]? Request(byte ecu, ReadOnlySpan<byte> data, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var request = KwpFrame.Build(ecu, TesterAddress, data);
        transport.DiscardInput();
        transport.Write(request);

        var wait = timeout ?? ResponseTimeout;
        var pendingDeadline = DateTime.UtcNow + PendingLimit;
        while (true)
        {
            byte[]? payload = null;
            BusReader.ReadUntil(transport, wait, buf =>
            {
                payload = FindResponse(Echo.Strip(request, buf), ecu);
                return payload is not null;
            }, ct);

            if (payload is null) return null;
            if (IsResponsePending(payload) && DateTime.UtcNow < pendingDeadline)
            {
                wait = TimeSpan.FromMilliseconds(5000); // P2* extended
                continue;
            }
            return payload;
        }
    }

    private byte[]? FindResponse(ReadOnlySpan<byte> buf, byte ecu)
    {
        foreach (var f in KwpFrame.ParseAll(buf))
            if (f.Source == ecu && f.Target == TesterAddress && f.Data.Length > 0)
                return f.Data;
        return null;
    }

    internal static bool IsResponsePending(byte[] p) => p.Length >= 3 && p[0] == 0x7F && p[2] == 0x78;

    /// <summary>Throws a readable error for a negative response.</summary>
    public static void EnsurePositive(byte[]? response, byte service, string what)
    {
        if (response is null) throw new DiagnosticException($"{what}: no response");
        if (response[0] == (byte)(service + 0x40)) return;
        if (response[0] == 0x7F && response.Length >= 3)
            throw new DiagnosticException($"{what}: rejected by ECU ({Kwp2000.NegativeResponseText(response[2])})");
        throw new DiagnosticException($"{what}: unexpected response {Convert.ToHexString(response)}");
    }
}

public static class Kwp2000
{
    public const byte ReadDtcByStatus = 0x18;
    public const byte ClearDtc = 0x14;
    public const byte ReadEcuIdentification = 0x1A;
    public const byte TesterPresent = 0x3E;

    public static string NegativeResponseText(byte code) => code switch
    {
        0x10 => "general reject",
        0x11 => "service not supported",
        0x12 => "sub-function not supported / invalid format",
        0x21 => "busy, repeat request",
        0x22 => "conditions not correct (check ignition / engine state)",
        0x31 => "request out of range",
        0x33 => "security access denied",
        0x78 => "response pending",
        0x80 => "service not supported in active session",
        _ => $"code 0x{code:X2}",
    };

    /// <summary>
    /// ISO 14230 statusOfDTC: bits 5-6 hold the storage state (01 stored, 10 maturing, 11 present).
    /// Real E90 modules also report entries with state 00; they are in fault memory, so treat them as stored.
    /// The BMW meaning of every bit is not confirmed yet, so the raw byte is always shown too.
    /// </summary>
    public static FaultState StateFromStatus(byte status) => ((status >> 5) & 0x03) switch
    {
        0b11 => FaultState.Present,
        0b10 => FaultState.Intermittent,
        _ => FaultState.Stored,
    };
}
