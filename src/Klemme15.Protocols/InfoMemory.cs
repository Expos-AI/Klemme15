using Klemme15.Core;

namespace Klemme15.Protocols;

/// <summary>
/// The info / shadow memory of a D-CAN module, read natively: KWP2000 <c>$22 2000</c> (BMW calls it dtcShadowMemory,
/// the job IS_LESEN). Modules log events here that are worth knowing but never light a fault lamp: a remote with a
/// weak battery, a crank that was aborted, a voltage dip. Read for the modules where it helps most (CAS and the
/// engine ECU), and now every D-CAN module that has one.
///
/// The reply is <c>62 20 00</c>, a count byte, then 3 bytes per entry (code high, code low, status), as seen from a
/// real E90's CAS3. Replies without the count byte are accepted too; anything whose length doesn't fit is ignored.
/// The MSV70 engine computer declines the request (7F 22 12), which is fine.
/// </summary>
public static class InfoMemory
{
    public static readonly byte[] Request = [0x22, 0x20, 0x00];

    /// <summary>
    /// KWP2000 StartRoutineByLocalIdentifier <c>$31</c> with routine <c>$06</c>, ClearDTCShadowMemory (BMW's IS_LOESCHEN).
    /// In every KWP2000 module description in the reference set, routine $06 means only this. It is sent only to a
    /// module that has just confirmed it has an info memory. Never change these bytes: other $31 routines erase
    /// flash or adaptations.
    /// </summary>
    public static readonly byte[] ClearRequest = [0x31, 0x06];

    /// <summary>Every D-CAN (KWP2000) module is asked; one that has no info memory simply declines.</summary>
    public static bool Offered(EcuDefinition ecu) => ecu.Protocol == BusProtocol.BmwFast;

    /// <summary>True when the reply is a positive answer to <see cref="Request"/>, i.e. the module has an info memory.</summary>
    public static bool Supported(byte[]? reply) => reply is { Length: >= 3 } && reply[0] == 0x62 && reply[1] == 0x20 && reply[2] == 0x00;

    /// <summary>True for the positive answer to <see cref="ClearRequest"/>.</summary>
    public static bool Cleared(byte[]? reply) => reply is { Length: >= 2 } && reply[0] == 0x71 && reply[1] == 0x06;

    public static IReadOnlyList<Fault> Parse(EcuDefinition ecu, byte[]? reply)
    {
        if (reply is null || reply.Length < 3 || reply[0] != 0x62 || reply[1] != 0x20 || reply[2] != 0x00) return [];
        var payload = reply.AsSpan(3);
        int start, limit = int.MaxValue;
        switch (payload.Length % 3)
        {
            case 0: start = 0; break;
            case 1: start = 1; limit = payload[0]; break;
            default: return [];
        }
        var list = new List<Fault>();
        for (var i = start; i + 2 < payload.Length && list.Count < limit; i += 3)
        {
            var code = payload[i] << 8 | payload[i + 1];
            if (code is 0 or 0xFFFF) continue;
            var status = payload[i + 2];
            list.Add(new Fault
            {
                EcuId = ecu.Id,
                Code = code.ToString("X4"),
                Source = FaultSource.Bmw,
                State = FaultState.Stored,
                IsInfo = true,
                IsShadow = true,
                Details = new Dictionary<string, string> { ["status"] = $"0x{status:X2}", ["memory"] = Fault.InfoMemoryLabel },
            });
        }
        return list.DistinctBy(f => f.Code).ToList();
    }
}
