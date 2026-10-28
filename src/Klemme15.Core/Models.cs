namespace Klemme15.Core;

/// <summary>Diagnostic protocol an ECU speaks natively.</summary>
public enum BusProtocol
{
    /// <summary>BMW DS2 on the K-line, 9600 baud 8E1 (E36/E38/E39/E46/E53/E83/E85).</summary>
    Ds2,

    /// <summary>KWP2000 with BMW-FAST framing on D-CAN (E6x/E7x/E8x/E9x).</summary>
    BmwFast,

    /// <summary>Generic OBD-II only (ISO 9141-2 / ISO 14230 on K-line).</summary>
    Obd2,
}

/// <summary>One control unit as described by the open database.</summary>
public sealed record EcuDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required byte Address { get; init; }
    public required BusProtocol Protocol { get; init; }
    public string Category { get; init; } = "Other";

    /// <summary>EDIABAS group/variant files to try, in order (e.g. "D_0012", "D_MOTOR").</summary>
    public IReadOnlyList<string> Sgbd { get; init; } = [];

    /// <summary>Chassis codes this definition applies to (e.g. "E46", "E90").</summary>
    public IReadOnlyList<string> Chassis { get; init; } = [];

    /// <summary>
    /// EDIABAS variant names (SGBD file names, e.g. "msv70") this module can be on this chassis, most likely first.
    /// Used to pick the right official fault dictionary; the exact one is confirmed from the module's own ident when possible.
    /// </summary>
    public IReadOnlyList<string> Variants { get; init; } = [];

    /// <summary>True once someone has confirmed this entry on a real car.</summary>
    public bool Verified { get; init; }

    /// <summary>True for the engine ECU, which also answers generic OBD-II requests.</summary>
    public bool IsEngine { get; init; }

    /// <summary>Approximate location on the top-view car drawing (1000 x 460, front at left).</summary>
    public SchematicPoint? Position { get; init; }

    /// <summary>Fitted only with certain options; not answering is normal.</summary>
    public bool Optional { get; init; }

    /// <summary>Id of the module this one lives inside (e.g. navigation inside the head unit).</summary>
    public string? PartOf { get; init; }

    /// <summary>When it's fitted, other names, caveats.</summary>
    public string? Note { get; init; }

    public string AddressHex => $"0x{Address:X2}";
    public override string ToString() => $"{Id} ({AddressHex})";
}

/// <summary>Identification read from an ECU. Every field is optional because ECUs differ.</summary>
public sealed record EcuIdentity
{
    public string? PartNumber { get; init; }
    public string? HardwareVersion { get; init; }
    public string? SoftwareVersion { get; init; }
    public string? CodingIndex { get; init; }
    public string? DiagnosticIndex { get; init; }
    public string? Supplier { get; init; }
    public string? ProductionDate { get; init; }
    public string? Variant { get; init; }

    /// <summary>Everything the ECU returned, for display and bug reports.</summary>
    public IReadOnlyDictionary<string, string> Raw { get; init; } = new Dictionary<string, string>();

    public string Summary
    {
        get
        {
            var parsed = string.Join("  ", new[]
            {
                Variant is null ? null : $"Variant {Variant}",
                PartNumber is null ? null : $"Part {PartNumber}",
                HardwareVersion is null ? null : $"HW {HardwareVersion}",
                SoftwareVersion is null ? null : $"SW {SoftwareVersion}",
                CodingIndex is null ? null : $"CI {CodingIndex}",
            }.Where(s => s is not null));
            if (parsed.Length > 0) return parsed;
            // Not decoded yet: show the start of the raw reply so it can be studied.
            if (Raw.TryGetValue("hex", out var hex) && hex.Length > 0)
                return "ID " + string.Join(" ", hex.Chunk(2).Take(12).Select(c => new string(c))) + (hex.Length > 24 ? " ..." : "");
            if (Raw.TryGetValue("negative", out var neg)) return $"Answered, ident refused ({neg})";
            return "";
        }
    }
}

public enum FaultState
{
    /// <summary>Fault is active right now.</summary>
    Present,
    /// <summary>Fault occurred earlier and is stored in memory.</summary>
    Stored,
    /// <summary>Fault seen but not yet confirmed (maturing / sporadic).</summary>
    Intermittent,
    Unknown,
}

public enum FaultSource { Bmw, Obd2 }

/// <summary>One fault memory entry.</summary>
public sealed record Fault
{
    public required string EcuId { get; init; }

    /// <summary>Display code: "2A82" for BMW codes, "P0171" for OBD-II.</summary>
    public required string Code { get; init; }

    public required FaultSource Source { get; init; }
    public FaultState State { get; init; } = FaultState.Unknown;
    public string? Description { get; init; }

    /// <summary>Practical advice (where to look, common causes), if the database has any.</summary>
    public string? Hint { get; init; }

    /// <summary>How sure the description is: "official" (from the car's own SGBD), "high", "medium", or null for generic OBD-II text.</summary>
    public string? Confidence { get; init; }

    /// <summary>Where the description comes from (URLs for community entries).</summary>
    public IReadOnlyList<string> Sources { get; init; } = [];

    /// <summary>True for entries from the info or shadow memory rather than the fault memory.</summary>
    public bool IsInfo { get; init; }

    /// <summary>True for entries from the shadow memory (noticed, but never counted as a fault).</summary>
    public bool IsShadow { get; init; }

    /// <summary>Extra detail: frequency, symptom, mileage, raw status byte...</summary>
    public IReadOnlyDictionary<string, string> Details { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Where <see cref="Description"/> came from: "bmw-en" (BMW's English), "bmw-de" (BMW's German, untranslated),
    /// "bmw-de-glossary" (BMW's German put into English with Klemme15's glossary), "community", "obd", or null.
    /// </summary>
    public string? DescriptionOrigin { get; init; }

    /// <summary>BMW's own wording for this code (often German), from the user's BMW files or the optional fault database.</summary>
    public string? OfficialText { get; init; }

    /// <summary>Which module variant the official text belongs to (e.g. "MSV70").</summary>
    public string? ModuleVariant { get; init; }

    /// <summary>True when the variant was identified from the module itself (variant name or part number), not guessed.</summary>
    public bool VariantConfirmed { get; init; }

    /// <summary>Where the official text came from ("your BMW files", "bmw-unified-db").</summary>
    public string? OfficialSource { get; init; }

    /// <summary>Different meanings of the same code on other possible module variants ("MSV80: ...").</summary>
    public IReadOnlyList<string> Alternatives { get; init; } = [];

    public string DisplayDescription => Description ?? "No description in database yet (please contribute one)";
}

public enum EcuScanStatus { Ok, Faults, NoResponse, Error, Skipped }

public sealed record EcuScanResult
{
    public required EcuDefinition Ecu { get; init; }
    public required EcuScanStatus Status { get; init; }
    public EcuIdentity? Identity { get; init; }
    public IReadOnlyList<Fault> Faults { get; init; } = [];
    public string? Error { get; init; }

    /// <summary>Non-fatal remark, e.g. "fault memory needs EDIABAS mode for this module".</summary>
    public string? Note { get; init; }

    public TimeSpan Elapsed { get; init; }
}

public sealed record VehicleScanResult
{
    public required DateTimeOffset Started { get; init; }
    public required string Backend { get; init; }
    public string? Vin { get; init; }
    public string? Chassis { get; init; }
    public bool? IgnitionOn { get; init; }
    public required IReadOnlyList<EcuScanResult> Ecus { get; init; }
    public TimeSpan Elapsed { get; init; }
    public bool Cancelled { get; init; }

    /// <summary>Battery voltage measured before the scan, if the car reports it.</summary>
    public double? BatteryVoltage { get; init; }

    public int RespondingEcus => Ecus.Count(e => e.Status is EcuScanStatus.Ok or EcuScanStatus.Faults);
    public int TotalFaults => Ecus.Sum(e => e.Faults.Count(f => !f.IsInfo));
    public int InfoEntries => Ecus.Sum(e => e.Faults.Count(f => f.IsInfo));
}

/// <summary>A value that can be read live (RPM, coolant temperature, ...).</summary>
public sealed record LiveParameter
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Unit { get; init; } = "";

    /// <summary>Backend-specific locator, e.g. "obd:0C" or "ediabas:D_0012/STATUS_MOTORDREHZAHL/STAT_MOTORDREHZAHL_WERT".</summary>
    public required string Source { get; init; }

    public double? Min { get; init; }
    public double? Max { get; init; }
}

public sealed record LiveValue(LiveParameter Parameter, double? Value, string? Text, DateTimeOffset Timestamp)
{
    public string Display => Value is { } v ? $"{v:0.##} {Parameter.Unit}".Trim() : Text ?? "--";
}

/// <summary>A point on the 1000 x 460 top-view car drawing (front at left, driver side at bottom).</summary>
public readonly record struct SchematicPoint(double X, double Y);

/// <summary>Something on the car a fault can come from, e.g. "Cylinder 3" or "Thermostat".</summary>
public sealed record CarComponent(string Id, string Name, SchematicPoint Position);

/// <summary>A fault the demo car carries.</summary>
public sealed record DemoFault
{
    public required string ModuleId { get; init; }

    /// <summary>"P0171" for OBD-II, or a 4-digit hex BMW-format code.</summary>
    public required string Code { get; init; }

    public bool Present { get; init; }

    /// <summary>Description for made-up BMW-format demo codes.</summary>
    public string? Text { get; init; }

    /// <summary>Component id the fault points at on the drawing.</summary>
    public string? Component { get; init; }

    public bool IsObd => Code.Length == 5 && char.IsLetter(Code[0]);
}

/// <summary>One model within a family, e.g. the E90 335i.</summary>
public sealed record VehicleModel
{
    public required string Id { get; init; }
    public required string FamilyId { get; init; }
    public required string Name { get; init; }
    public string Body { get; init; } = "";
    public string Years { get; init; } = "";
    public string Engine { get; init; } = "";
    public string Drive { get; init; } = "";

    /// <summary>Modules the demo car of this model has fitted.</summary>
    public IReadOnlyList<string> ModuleIds { get; init; } = [];

    public IReadOnlyList<DemoFault> DemoFaults { get; init; } = [];

    /// <summary>Engine code from <see cref="Engine"/>, e.g. "N52", "N54", "M54".</summary>
    public string? EngineCode =>
        System.Text.RegularExpressions.Regex.Match(Engine, @"^[NMS]\d{2}[A-Z]{0,2}") is { Success: true } m ? m.Value : null;

    public int IdleRpm { get; init; } = 700;
    public bool Turbo { get; init; }
}

/// <summary>A chassis family that shares modules and protocols, e.g. E46 or E9x.</summary>
public sealed record VehicleFamily(string Id, string Name, string Years, string Cable, IReadOnlyList<EcuDefinition> Modules, IReadOnlyList<VehicleModel> Models);
