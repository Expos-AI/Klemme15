namespace Klemme15.Core;

/// <summary>Generic OBD-II helpers shared by every backend.</summary>
public static class ObdCodes
{
    /// <summary>Decode the two raw DTC bytes from a mode 03/07 response, e.g. (0x01, 0x71) -> "P0171".</summary>
    public static string Decode(byte hi, byte lo) =>
        $"{"PCBU"[hi >> 6]}{(hi >> 4) & 3}{hi & 0xF:X}{lo:X2}";

    /// <summary>Best-effort description for any code, used when the database has no entry.</summary>
    public static string Fallback(string code)
    {
        if (code.Length != 5) return "Unknown code format";
        var system = code[0] switch
        {
            'P' => "Powertrain",
            'C' => "Chassis",
            'B' => "Body",
            'U' => "Network",
            _ => "Unknown system",
        };
        if (code[1] is '1' or '3')
            return $"{system}: manufacturer-specific code (look up BMW meaning)";
        if (code[0] != 'P') return $"{system}: generic code";
        var area = code[2] switch
        {
            '0' => "fuel/air metering and auxiliary emission controls",
            '1' or '2' => "fuel and air metering",
            '3' => "ignition system or misfire",
            '4' => "auxiliary emission controls",
            '5' => "vehicle speed, idle control and auxiliary inputs",
            '6' => "computer and output circuits",
            '7' or '8' or '9' => "transmission",
            'A' or 'B' or 'C' => "hybrid propulsion",
            _ => "unspecified area",
        };
        return $"Generic powertrain code in {area}";
    }
}
