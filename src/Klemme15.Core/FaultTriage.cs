namespace Klemme15.Core;

public enum FaultPriority
{
    /// <summary>Active now: something is wrong at this moment.</summary>
    FixNow,
    /// <summary>Happened before or comes and goes: clear it and see whether it returns.</summary>
    CheckIt,
    /// <summary>Information for workshops, or a typical leftover of battery work: no action without a symptom.</summary>
    Background,
}

/// <summary>Klemme15's answer to "does this need fixing?" for one fault.</summary>
public sealed record FaultVerdict(FaultPriority Priority, string Label, string Advice);

/// <summary>
/// Sorts faults into what needs attention and what doesn't, so a list of ten codes doesn't send anyone
/// shopping for parts. The rules are deliberately simple and explained in the advice text.
/// </summary>
public static class FaultTriage
{
    // Words in BMW's English/German texts that mark faults typically left behind by a flat battery, battery work
    // or a module that was asleep, rather than a broken part.
    private static readonly string[] SupplyWords =
        ["unterspannung", "undervoltage", "under voltage", "low voltage", "supply voltage", "versorgungsspannung", "bordnetzspannung",
         "batteriespannung", "battery voltage", "klemme 30", "kl.30", "kl30", "terminal 30", "power supply", "energiesparmodus",
         "energy saving", "transportmodus", "transport mode"];
    private static readonly string[] BusWords =
        ["botschaft", "message missing", "no message", "timeout", "time-out", "zeitüberschreitung", "kommunikation", "communication",
         "bus off", "k-can", "pt-can", "f-can", "lo-can", "most-bus", "most bus", "byteflight", "k-bus", "i-bus"];

    private static bool LampOn(string value)
    {
        var v = value.Trim().ToLowerInvariant();
        return v.Contains("request") || v is "on" or "yes" or "ja" or "ein" || v.Contains("lampe ein") || v.Contains("lamp on");
    }

    public static FaultVerdict Assess(Fault f)
    {
        var text = $"{f.Description} {f.OfficialText}".ToLowerInvariant();
        var count = f.Details.TryGetValue("Occurrences", out var c) && int.TryParse(c, out var n) ? n : 0;
        var lamp = f.Details.Any(kv => kv.Key.Contains("warning lamp", StringComparison.OrdinalIgnoreCase) && LampOn(kv.Value));

        if (f.IsShadow)
            return new(FaultPriority.Background, "Background",
                "Shadow memory: the module noticed something but didn't count it as a fault, and it never lights a warning. Ignore it unless you have a symptom that matches.");
        if (f.IsInfo)
            return new(FaultPriority.Background, "Background",
                "Info memory: logged for workshops, never lights a warning. Ignore it unless you have a symptom that matches.");

        var supply = SupplyWords.Any(text.Contains);
        var bus = BusWords.Any(text.Contains);

        // Codes our research marks as normally harmless on their own (e.g. the junction box's latching relay after a radio swap):
        // the tip says when they do matter, so they don't go to the top of the list.
        if (f.UsuallyHarmless && f.State is FaultState.Present or FaultState.Intermittent)
            return new(FaultPriority.CheckIt, "Check it",
                "Active, but this code is usually harmless on its own. Read the tip below: if you have none of the symptoms it describes, clear it and see whether it returns.");
        if (f.UsuallyHarmless && count < 10)
            return new(FaultPriority.Background, "Probably harmless",
                "Not active now, and this code is usually harmless on its own. Read the tip below; clear it and only investigate if it returns.");

        switch (f.State)
        {
            case FaultState.Present when supply:
                return new(FaultPriority.FixNow, "Fix now",
                    "Active now and about the power supply: check the battery and charging first (Readings > battery voltage, or a meter), then the module's fuses and grounds.");
            case FaultState.Present:
                return new(FaultPriority.FixNow, "Fix now",
                    lamp ? "Active now and it turned on a warning light. Start with the troubleshooting steps below."
                         : "Active now: something is wrong at this moment. Start with the troubleshooting steps below.");
            case FaultState.Intermittent:
                return new(FaultPriority.CheckIt, "Check it",
                    "Comes and goes. Clear it and drive: if it comes back, look for loose connectors or chafed wiring on that circuit.");
        }

        if (supply || bus)
            return count >= 10
                ? new(FaultPriority.CheckIt, "Check it",
                    $"Not active now, but logged {count} times. A one-off is usually from battery work; this many times points at a weak battery, a bad ground or a loose connector.")
                : new(FaultPriority.Background, "Probably harmless",
                    bus ? "Not active now. Communication faults are usually logged when a module was asleep, being woken up, or the battery was disconnected. Clear it; only investigate if it returns."
                        : "Not active now. Low-voltage faults are usually left behind by a flat battery, jump-starting or battery work. Clear it; only investigate if it returns.");

        return count >= 5
            ? new(FaultPriority.CheckIt, "Check it", $"Not active now, but it has happened {count} times. Clear it and drive; if it returns, fix it before it becomes permanent.")
            : new(FaultPriority.CheckIt, "Check it", "Happened before but isn't active now. Clear it and drive a few days: if it returns, fix it; if not, no action needed.");
    }
}
