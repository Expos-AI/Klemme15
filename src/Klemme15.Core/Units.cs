namespace Klemme15.Core;

/// <summary>Turns the unit spellings in BMW's data ("degC", "Grad KW", "mg/Hub", "1/min", "0/1") into one readable form.</summary>
public static class Units
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["degC"] = "°C", ["deg C"] = "°C", ["Grad C"] = "°C", ["°C"] = "°C", ["Grad Celsius"] = "°C",
        ["Grad KW"] = "° crank", ["°KW"] = "° crank", ["deg CrS"] = "° crank", ["degCrS"] = "° crank", ["°CRK"] = "° crank",
        ["Grad"] = "°", ["deg"] = "°", ["°TPS"] = "°",
        ["1/min"] = "rpm", ["U/min"] = "rpm",
        ["us"] = "µs", ["µs"] = "µs",
        ["mg/hub"] = "mg/stroke", ["mg/stk"] = "mg/stroke", ["mg/cyc"] = "mg/stroke", ["mg/inj"] = "mg/injection",
        ["Kg/h"] = "kg/h", ["Volt"] = "V", ["mv"] = "mV", ["ohm"] = "Ω", ["ul"] = "µl", ["0/oo"] = "‰", ["%."] = "%",
        ["%DK"] = "%", ["%PED"] = "%",
        // State values (on/off, counters, codes) need no unit.
        ["0/1"] = "", ["0_1"] = "", ["0-n"] = "", ["1/1"] = "", ["hex"] = "", ["-"] = "",
    };

    public static string? Normalize(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return null;
        var u = unit.Replace(" manuell", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (Map.TryGetValue(u, out var mapped)) u = mapped;
        return u.Length == 0 ? null : u;
    }
}
