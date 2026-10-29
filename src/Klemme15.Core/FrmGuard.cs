using System.Text.RegularExpressions;

namespace Klemme15.Core;

/// <summary>The footwell module (FRM) hardware generations of the E8x/E9x/E70/E84/E89 and MINI R5x.</summary>
public enum FrmGeneration
{
    /// <summary>Not a footwell module.</summary>
    None,
    /// <summary>First generation ("FRMFA", BMW numbers 61.35-6 9xx xxx, diagnostic variant FRM_87): no D-Flash problem.</summary>
    Frm1,
    /// <summary>Second generation ("FRM II", about 2007-2009): real EEPROM, not affected by the FRM3 corruption.</summary>
    Frm2,
    /// <summary>Third generation (FRM3/FRM3R, about 2009 on): EEPROM emulated in flash, which corrupts if power sags during a write.</summary>
    Frm3,
    /// <summary>A footwell module Klemme15 can't place: treated like an FRM3 to be safe.</summary>
    Unknown,
}

/// <summary>What Klemme15 knows about a footwell module, and the voltage it insists on before writing to it.</summary>
/// <param name="Fragile">True for FRM3 and for modules that can't be identified: these get the strict rules.</param>
/// <param name="WriteFloor">Minimum battery voltage for coding and service functions on this module.</param>
/// <param name="ClearFloor">Minimum battery voltage for clearing its fault memory (null: no limit).</param>
public sealed record FrmAssessment(FrmGeneration Generation, bool Fragile, string Label, string Advice, double WriteFloor, double? ClearFloor);

/// <summary>
/// The FRM3 "D-Flash" problem: the third-generation footwell module stores its settings in a log in flash memory, and a
/// voltage dip while it writes (coding, programming, clearing faults, a battery disconnect while it's awake) can leave it
/// dead: no lights, windows or mirrors, and no answer over the cable. Klemme15 identifies the hardware from the part
/// number the module reports, and refuses to write to a fragile one without a charger-level supply.
/// Sources: BMW SI B01 20 16 (via owners' and repair-shop write-ups), kessydocs.de FRM3 part list, BimmerCode FAQ,
/// tomvleeuwen/dflash_to_eeprom. Part number ranges between the confirmed numbers are inferred, which is why anything
/// unrecognised is treated as fragile.
/// </summary>
public static class FrmGuard
{
    /// <summary>Battery voltage a charger holds the car at (13.2-14.4 V); below this a fragile FRM is not written to.</summary>
    public const double FragileWriteFloor = 13.0;
    public const double NormalWriteFloor = 12.5;

    /// <summary>Chassis families whose cars have a footwell module (the E60 family and E83 use a light module instead).</summary>
    public static readonly IReadOnlySet<string> FamiliesWithFrm = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "E8x", "E9x", "E70", "E84", "E89", "R56" };

    public static bool IsFootwellModule(EcuDefinition ecu) =>
        ecu.Id.EndsWith(".FRM", StringComparison.OrdinalIgnoreCase) || ecu.Id.Equals("FRM", StringComparison.OrdinalIgnoreCase);

    /// <summary>Places a footwell module by its variant name and BMW part number.</summary>
    public static FrmGeneration Classify(string? partNumber, string? variant)
    {
        var v = variant?.Trim().ToLowerInvariant();
        if (v is not null && v.StartsWith("frm3", StringComparison.Ordinal)) return FrmGeneration.Frm3;

        var digits = partNumber is null ? "" : Regex.Replace(partNumber, @"\D", "");
        // BMW numbers are 7 digits; only the "6135" group prefix (or leading zeros) may come before them.
        if (digits.Length > 7)
        {
            var head = digits[..^7];
            digits = head.TrimStart('0') is "" or "6135" ? digits[^7..] : "";
        }
        if (digits.Length == 7 && int.TryParse(digits, out var n))
        {
            // 6 95x xxx - 6 98x xxx: FRMFA (2004-2007 numbers, long before the FRM3 appeared in 2009).
            if (n is >= 6_950_000 and < 7_000_000) return FrmGeneration.Frm1;
            if (n == 8_886_836) return FrmGeneration.Frm3;                          // FRM3R "MAX" service replacement
            if (n is >= 9_224_500 and < 9_500_000) return FrmGeneration.Frm3;       // 9 224 5xx onward: FRM3/FRM3R
            // FRM II: only numbers confirmed by listings. The rest of 9 12x-9 22x is inferred, so it stays "unknown" (fragile).
            if (n is 9_166_708 or 9_166_709 or 9_166_711 or 9_204_527) return FrmGeneration.Frm2;
        }
        if (v is "frm_87") return FrmGeneration.Frm1;
        // FRM_70 covers both FRM2 and FRM3, and MINI numbers aren't mapped: unknown, so treated as fragile.
        return FrmGeneration.Unknown;
    }

    public static FrmAssessment Assess(EcuIdentity? identity) => Assess(identity?.PartNumber, identity?.Variant);

    public static FrmAssessment Assess(string? partNumber, string? variant)
    {
        var g = Classify(partNumber, variant);
        return g switch
        {
            FrmGeneration.Frm1 => new(g, false, "Footwell module FRM1 (first generation)",
                "This footwell module is the early type. It isn't affected by the FRM3 corruption problem, but keep the battery healthy while coding anyway.",
                NormalWriteFloor, null),
            FrmGeneration.Frm2 => new(g, false, "Footwell module FRM2",
                "This footwell module is the second generation, which has a real EEPROM and isn't affected by the FRM3 corruption problem. Keep the battery healthy while coding anyway.",
                NormalWriteFloor, null),
            FrmGeneration.Frm3 => new(g, true, "Footwell module FRM3 (fragile type)",
                $"This is the FRM3 footwell module, the type that can corrupt itself if the voltage dips while it writes. Klemme15 only codes it or runs its service functions at {FragileWriteFloor:0.0} V or more (a charger), and only clears its faults at {NormalWriteFloor:0.0} V or more. Never disconnect the battery while the car is awake.",
                FragileWriteFloor, NormalWriteFloor),
            _ => new(FrmGeneration.Unknown, true, "Footwell module (type not confirmed)",
                $"Klemme15 can't tell whether this footwell module is the fragile FRM3 type, so it applies the FRM3 rules: coding and service functions only at {FragileWriteFloor:0.0} V or more (a charger), clearing faults at {NormalWriteFloor:0.0} V or more.",
                FragileWriteFloor, NormalWriteFloor),
        };
    }
}
