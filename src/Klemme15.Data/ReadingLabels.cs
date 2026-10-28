using System.Text.RegularExpressions;

namespace Klemme15.Data;

/// <summary>
/// Turns BMW's job and result names (STATUS_L_SONDE, STAT_LAMBDA_SONDE_WERT) and their German comments into
/// short English labels, and sorts readings into groups a person troubleshooting a car looks for.
/// </summary>
public static class ReadingLabels
{
    /// <summary>Groups in display order; first match on the job name (and comment) wins.</summary>
    private static readonly (string Group, Regex Match)[] Groups =
    [
        ("Oxygen sensors and mixture", R(@"L_?SONDE|LAMBDA|LSH|LSU|GEMISCH|\bADD\b|STATUS_ADD|STATUS_INT|ADAPTION_GEMISCH|INTEGRATOR|TRIM")),
        ("Misfires and smooth running", R(@"LAUFRUHE|\bAUSSETZER|VERBRENNUNGSAUSSETZ|\bLRP|MISFIRE|SEGMENTZEIT|\bLUR\b")),
        ("VANOS and camshafts", R(@"VANOS|NOCKEN|\bENWS|\bANWS|^NW|_NW|EXZENTER|\bVVT|VALVETRONIC|\bEXWE")),
        ("Ignition and knock", R(@"KLOPF|^KB\d|ZUEND|^ZW_|^ZS\d|IGNITION")),
        ("Fuel system", R(@"^EV\d|INJEKT|KRAFTSTOFF|^EKP|RAIL|^HDP|MENGEN|TANK|^DMTL|^TEV|EINSPRITZ")),
        ("Intake, exhaust, throttle and pedal", R(@"ABGAS|^AGK|DROSSEL|^DK|_DK|^PWG|^FWV|^LMM|^HFM|LUFTMASSE|SAUG|^DISA|ANSAUG|AN_LUFT|LADEDRUCK|^SDF|BOOST|WASTEGATE|^UDF|UMGEBUNGSDRUCK|^SLP|^SLV|SEKUNDAER")),
        ("Switches and states", R(@"SCHALTER|TASTER|DIGITAL|FUNKTION|BETRIEBSZUST|^STATE|^SPT$|^TTEMP|^ODS$|OELDRUCKSCH")),
        ("Temperatures", R(@"TEMP|THERMOSTAT|^KFT|KUEHL|^TKA|^TOEL")),
        ("Battery, alternator and power", R(@"^UBAT|BATTERIE|SPANNUNG|^IBS|GENERATOR|DFMONITOR|STROM|^BSD|ENERGIE|KL15|KL30|KLEMME|^SR$|STARTRELAIS")),
        ("Wheels, brakes and stability", R(@"^RAD|RADGESCHW|RADDREHZ|BREMS|^BLS|^BLTS|DRUCK_HZ|GIER|QUER|LENKW|^LWS|^DSC|^ABS")),
        ("Gearbox and clutch", R(@"GETRIEBE|^GANG|^KUP|KUPPLUNG|WAHL|^EGS|WANDLER")),
        ("Lights and warning lamps", R(@"LICHT|LAMPE|LEUCHTE|BLINK|SCHEINW|XENON|^LED|^MIL$|^EML$|^FGRL")),
        ("Climate, fans and comfort", R(@"KLIMA|GEBLAESE|HEIZ|KOMPRESSOR|^KOREL|LUEFTER|^EBL|SITZ|FENSTER|SPIEGEL|TUER|^ZV_|VERRIEGEL")),
        ("Counters and identification", R(@"ZAEHLER|STUNDEN|KM_|KILOMETER|IDENT|NUMMER|VERSION|CODIER")),
    ];

    public const string OtherGroup = "Other readings";

    public static IReadOnlyList<string> GroupOrder { get; } = [.. Groups.Select(g => g.Group), OtherGroup];

    public static string GroupOf(string job, string? comment)
    {
        // The job name is the most reliable signal; the description (without BMW's boilerplate) breaks ties.
        var name = job.StartsWith("STATUS_", StringComparison.OrdinalIgnoreCase) ? job[7..] : job;
        foreach (var (group, match) in Groups)
            if (match.IsMatch(name)) return group;
        if (CleanComment(comment) is { } text)
        {
            var upper = GermanGlossary.Normalize(text).ToUpperInvariant();
            foreach (var (group, match) in Groups)
                if (match.IsMatch(upper)) return group;
        }
        return OtherGroup;
    }

    /// <summary>English title for a reading job, from BMW's comment when it translates well, else from the job name.</summary>
    public static string JobTitle(string job, string? comment)
    {
        var cleaned = CleanComment(comment);
        if (cleaned is not null && GermanGlossary.Translate(cleaned) is { } en) return en;
        var fromName = FromName(job, "STATUS_");
        // Prefer readable German over a name that is mostly abbreviations.
        return cleaned is not null && fromName.Count(char.IsUpper) > fromName.Length / 2 ? cleaned : fromName;
    }

    /// <summary>English label for one result value.</summary>
    public static string ResultLabel(string result, string? comment)
    {
        var cleaned = CleanComment(comment);
        if (cleaned is not null && cleaned.Length <= 80 && GermanGlossary.Translate(cleaned) is { } en) return en;
        return FromName(Regex.Replace(result, "_(WERT|EINH|TEXT|INFO)(\\d*)$", " $2", RegexOptions.IgnoreCase).Trim(), "STAT_");
    }

    /// <summary>Text values ("nicht betaetigt", "Klappe offen") in English when the glossary knows them.</summary>
    public static string ValueText(string text) =>
        text.Any(char.IsLetter) && GermanGlossary.Translate(text) is { } en ? en : text;

    /// <summary>Removes protocol details BMW puts in comments ("0x30C101", "$22 40 08", "KWP 2000* ...", "NO_CON ...").</summary>
    public static string? CleanComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) return null;
        var t = comment.Trim();
        t = Regex.Replace(t, @"\b(KWP\s?2000\*?|KWP2000:?|UDS:?)\b.*$", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\bNO_CON\b.*$", "");
        t = Regex.Replace(t, @"\b(ReadDataBy\w+|DynamicallyDefinedLocalIdentifier|DDLI)\b", "");
        t = Regex.Replace(t, @"\$\s?[0-9A-Fa-f]{2}\b(\s[0-9A-Fa-f]{2}\b)*", "");
        t = Regex.Replace(t, @"\b0x[0-9A-Fa-f]+\b", "");
        t = Regex.Replace(t, @"^\s*(Auslesen|Lesen)\s+(des|der|von|eines|einer)?\s*", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\s+(auslesen|lesen)\s*$", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\s{2,}", " ").Trim(' ', ':', '-', ',', '.');
        return t.Length < 3 ? null : char.ToUpperInvariant(t[0]) + t[1..];
    }

    /// <summary>"STATUS_L_SONDE_2_H" → "L sonde 2 h" with known words translated ("Lambda sensor ...").</summary>
    private static string FromName(string name, string prefix)
    {
        var n = name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? name[prefix.Length..] : name;
        var words = n.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(w =>
        {
            var lower = w.ToLowerInvariant();
            if (w.Length >= 4 && GermanGlossary.Translate(lower) is { } en) return en.ToLowerInvariant();
            return w.Length <= 3 ? w : lower;
        });
        var s = string.Join(' ', words).Trim();
        return s.Length == 0 ? name : char.ToUpperInvariant(s[0]) + s[1..];
    }

    private static Regex R(string pattern) => new(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
}
