using System.Buffers.Binary;
using System.Globalization;
using Klemme15.Core;
using Klemme15.Protocols.Channels;

namespace Klemme15.Protocols;

/// <summary>
/// Module readings without BMW's files: asks an engine computer for each value in BMW's measurement table
/// (ReadDataByCommonIdentifier, $22 + the value's id) and scales the reply with the table's type, factor and unit.
/// The tables come from the database the user downloaded. Read-only: nothing is changed on the car.
/// </summary>
public sealed class NativeReadings(IRawRequests raw, IMeasurementTables tables, Func<EcuDefinition, string?> variantOf, TrafficLog? log = null) : IModuleReadings
{
    public const byte ReadDataByCommonIdentifier = 0x22;

    public string? VariantOf(EcuDefinition ecu) => variantOf(ecu)?.ToUpperInvariant();

    /// <summary>True when there is a measurement table for this module's variant.</summary>
    public bool Supports(EcuDefinition ecu) => variantOf(ecu) is { } v && tables.Has(v);

    public Task<IReadOnlyList<ModuleReading>> ListReadingsAsync(EcuDefinition ecu, CancellationToken ct = default)
    {
        if (variantOf(ecu) is not { } v || !tables.Has(v)) return Task.FromResult<IReadOnlyList<ModuleReading>>([]);
        // One reading per value. The job name is the result name, so pins and searches stay readable.
        var list = tables.MeasurementsOf(v)
            .GroupBy(m => m.Result, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .Select(m => new ModuleReading(m.Result, m.Comment))
            .ToList();
        return Task.FromResult<IReadOnlyList<ModuleReading>>(list);
    }

    public async Task<IReadOnlyList<ReadingValue>> ReadAsync(EcuDefinition ecu, string job, CancellationToken ct = default)
    {
        if (variantOf(ecu) is not { } v) throw new DiagnosticException("The module version isn't known yet; run the quick test first.");
        var def = tables.MeasurementsOf(v).FirstOrDefault(m => m.Result.Equals(job, StringComparison.OrdinalIgnoreCase))
                  ?? throw new DiagnosticException($"{job} is not in {v.ToUpperInvariant()}'s measurement table.");

        byte[] request = [ReadDataByCommonIdentifier, (byte)(def.Id >> 8), (byte)def.Id];
        var reply = await raw.RequestAsync(ecu, request, ct);
        if (reply is null) throw new DiagnosticException("No reply from the module.");
        if (reply.Length >= 3 && reply[0] == 0x7F)
            throw new DiagnosticException($"The module declined this reading ({Kwp2000.NegativeResponseText(reply[2])}). Please send the traffic log so it can be supported.");
        if (reply.Length < 4 || reply[0] != ReadDataByCommonIdentifier + 0x40 || reply[1] != request[1] || reply[2] != request[2])
            throw new DiagnosticException($"Unexpected reply {Convert.ToHexString(reply)}.");

        var data = reply.AsSpan(3);
        var number = Decode(def, data);
        log?.Note($"Reading {def.Result} (0x{def.Id:X4}): raw {Convert.ToHexString(data)} -> {number?.ToString(CultureInfo.InvariantCulture) ?? "?"} {def.Unit}");
        var text = number?.ToString("0.###", CultureInfo.InvariantCulture) ?? Convert.ToHexString(data);
        return [new ReadingValue(def.Result, text, number, Clean(def.Unit), def.Comment)];
    }

    public string PinSource(EcuDefinition ecu, string job, string result) => $"native:{ecu.Id}/{job}";

    /// <summary>Raw bytes to a scaled value: raw × mul ÷ div + add, with the raw size and sign from BMW's data type.</summary>
    public static double? Decode(MeasurementDef def, ReadOnlySpan<byte> data)
    {
        var type = def.DataType.Trim().ToLowerInvariant().Replace("unsigend", "unsigned");   // one table has this typo
        double raw;
        if (type is "motorola float" or "intel float" or "float")
        {
            if (data.Length < 4) return null;
            raw = type == "intel float" || def.LowHigh ? BinaryPrimitives.ReadSingleLittleEndian(data) : BinaryPrimitives.ReadSingleBigEndian(data);
        }
        else if (type.StartsWith("0x", StringComparison.Ordinal))
        {
            // A bit mask on a single byte (e.g. "0xFF", "0x04"): the masked value.
            if (data.Length < 1 || !int.TryParse(type[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var mask)) return null;
            raw = data[0] & mask;
        }
        else
        {
            var size = type switch
            {
                "unsigned char" or "signed char" => 1,
                "unsigned integer" or "signed integer" or "unsigned int" or "signed int" or "unsigned word" or "signed word" => 2,
                "unsigned long" or "signed long" or "unsigned dword" or "signed dword" => 4,
                _ => data.Length is 1 or 2 or 4 ? data.Length : 0,   // "-": size from the reply itself
            };
            if (size == 0 || data.Length < size) return null;
            var bytes = data[..size];
            var signed = type.StartsWith("signed", StringComparison.Ordinal);
            raw = size switch
            {
                1 => signed ? (sbyte)bytes[0] : bytes[0],
                2 => def.LowHigh
                    ? (signed ? BinaryPrimitives.ReadInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16LittleEndian(bytes))
                    : (signed ? BinaryPrimitives.ReadInt16BigEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes)),
                _ => def.LowHigh
                    ? (signed ? BinaryPrimitives.ReadInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes))
                    : (signed ? BinaryPrimitives.ReadInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes)),
            };
        }
        var div = def.Div == 0 ? 1 : def.Div;
        return Math.Round(raw * def.Mul / div + def.Add, 4);
    }

    private static string? Clean(string? unit) =>
        unit is null || unit is "-" or "" ? null : unit.Replace("Grad C", "°C", StringComparison.OrdinalIgnoreCase);
}
