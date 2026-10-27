namespace OpenHood.Core;

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

    /// <summary>True once someone has confirmed this entry on a real car.</summary>
    public bool Verified { get; init; }

    /// <summary>True for the engine ECU, which also answers generic OBD-II requests.</summary>
    public bool IsEngine { get; init; }

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

    public string Summary =>
        string.Join("  ", new[]
        {
            Variant is null ? null : $"Variant {Variant}",
            PartNumber is null ? null : $"Part {PartNumber}",
            HardwareVersion is null ? null : $"HW {HardwareVersion}",
            SoftwareVersion is null ? null : $"SW {SoftwareVersion}",
            CodingIndex is null ? null : $"CI {CodingIndex}",
        }.Where(s => s is not null));
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

    /// <summary>Extra detail: frequency, symptom, mileage, raw status byte...</summary>
    public IReadOnlyDictionary<string, string> Details { get; init; } = new Dictionary<string, string>();

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

    public int RespondingEcus => Ecus.Count(e => e.Status is EcuScanStatus.Ok or EcuScanStatus.Faults);
    public int TotalFaults => Ecus.Sum(e => e.Faults.Count);
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
