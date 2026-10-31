using Klemme15.Core;

namespace Klemme15.Data;

/// <summary>
/// The last step for a BMW code that no source describes: a meaning from the code's number range and the module,
/// plus what to do next. On E-series modules, codes from 0xC000 to 0xEFFF are overwhelmingly communication faults
/// (a CAN / K-CAN / PT-CAN message missing or invalid): about 75-85 % of BMW's own texts in that range are, from
/// a statistical check across 232 module versions. Klemme15 states this as "most likely", never as fact.
/// </summary>
public static class FaultFallback
{
    public const string Origin = "klemme15-generic";

    public static (string Text, string Hint) For(Fault fault, EcuDefinition ecu, bool hasOfficialTexts)
    {
        var module = ecu.Name;
        var more = hasOfficialTexts
            ? "BMW's texts you have don't list this code for your module version either; it may be from a newer software level."
            : "For BMW's exact meaning, download the free fault database in the Garage (one click) or point Klemme15 at your ISTA/INPA files, then scan again.";

        if (fault.IsInfo || fault.IsShadow)
            return ($"{module} logged a background event (code {fault.Code}). It is not a fault and doesn't light a warning.",
                    "Only worth looking into if it matches a symptom. " + more);

        if (OfficialLookup.TryParseCode(fault.Code, out var n) && n is >= 0xC000 and <= 0xEFFF)
            return ($"{module}: most likely a communication fault. A message from another module on the data bus (CAN) was missing or invalid.",
                    "These are usually left behind by low battery voltage, jump-starting, a module that was unplugged or asleep, or a faulty module elsewhere. " +
                    "Charge the battery, clear the fault and scan again; if it returns, check which modules didn't answer the quick test. " + more);

        var area = ecu.IsEngine ? "engine management"
            : ecu.Category is { Length: > 0 } c && !c.Equals("Other", StringComparison.OrdinalIgnoreCase) ? c.ToLowerInvariant()
            : "this module";
        return ($"{module} reports a fault (code {fault.Code}) in {area}. Its exact meaning isn't in Klemme15's list yet.",
                "Check whether it's active now or stored, and clear it to see if it returns. " + more);
    }
}
