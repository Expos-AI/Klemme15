using System.Buffers.Binary;
using System.Globalization;
using Klemme15.Core;
using Klemme15.Protocols.Channels;

namespace Klemme15.Protocols;

/// <summary>
/// Module readings without BMW's files: reads values from BMW's measurement table the way BMW's own
/// STATUS_MESSWERTBLOCK_LESEN does. Klemme15 defines a temporary record in the module listing the value ids
/// (KWP2000 $2C F0: clear, then define by common identifier), reads it with $21 F0, and scales each value with the
/// table's type, factor and unit. Confirmed on a real MSV70: a plain $22 request for these ids is refused (7F 22 12).
/// Modules that refuse $2C get a $22 request instead. The tables come from the database the user downloaded.
/// Read-only: the temporary record lives in the module's RAM and nothing about the car is changed.
/// </summary>
public sealed class NativeReadings(IRawRequests raw, IMeasurementTables tables, Func<EcuDefinition, string?> variantOf, TrafficLog? log = null) : IModuleReadings, IMeasurementReader, IBatchReadings
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

    public const byte DynamicallyDefineLocalIdentifier = 0x2C, ReadDataByLocalIdentifier = 0x21, RecordId = 0xF0;

    /// <summary>Most values per record: keeps the request (2 + 6 per value) and the reply inside one BMW-FAST frame.</summary>
    public const int MaxPerRecord = 20;

    public async Task<IReadOnlyList<ReadingValue>> ReadAsync(EcuDefinition ecu, string job, CancellationToken ct = default)
    {
        if (variantOf(ecu) is not { } v) throw new DiagnosticException("The module version isn't known yet; run the quick test first.");
        var def = tables.MeasurementsOf(v).FirstOrDefault(m => m.Result.Equals(job, StringComparison.OrdinalIgnoreCase))
                  ?? throw new DiagnosticException($"{job} is not in {v.ToUpperInvariant()}'s measurement table.");
        var data = (await ReadRawAsync(ecu, [def], ct))[0];
        var number = Decode(def, data);
        log?.Note($"Reading {def.Result} (0x{def.Id:X4}): raw {Convert.ToHexString(data)} -> {number?.ToString(CultureInfo.InvariantCulture) ?? "?"} {def.Unit}");
        var text = number?.ToString("0.###", CultureInfo.InvariantCulture) ?? Convert.ToHexString(data);
        return [new ReadingValue(def.Result, text, number, Clean(def.Unit), def.Comment)];
    }

    public string PinSource(EcuDefinition ecu, string job, string result) => $"native:{ecu.Id}/{job}";

    /// <summary>Reads the named values this module's table has, many per request; values it lacks or refuses are left out.</summary>
    public async Task<IReadOnlyDictionary<string, double>> ReadMeasurementsAsync(EcuDefinition ecu, IReadOnlyList<string> resultNames, CancellationToken ct = default)
    {
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (variantOf(ecu) is not { } v || !tables.Has(v)) return values;
        var defs = resultNames
            .Select(n => tables.MeasurementsOf(v).FirstOrDefault(m => m.Result.Equals(n, StringComparison.OrdinalIgnoreCase)))
            .OfType<MeasurementDef>().Where(d => SizeOf(d) > 0).ToList();
        foreach (var chunk in defs.Chunk(MaxPerRecord))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var raw = await ReadRawAsync(ecu, chunk, ct);
                for (var i = 0; i < chunk.Length; i++)
                    if (Decode(chunk[i], raw[i]) is { } d) values[chunk[i].Result] = d;
            }
            catch (DiagnosticException)
            {
                // One value the module doesn't know spoils the whole record: read this chunk one by one.
                foreach (var def in chunk)
                    try
                    {
                        if (Decode(def, (await ReadRawAsync(ecu, [def], ct))[0]) is { } d) values[def.Result] = d;
                    }
                    catch (DiagnosticException) { /* this value isn't available on this car */ }
            }
        }
        return values;
    }

    /// <summary>Reads up to <see cref="MaxPerRecord"/> values per record; a chunk the module refuses is read value by value.</summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<ReadingValue>>> ReadManyAsync(EcuDefinition ecu, IReadOnlyList<string> jobs, CancellationToken ct = default)
    {
        var result = new Dictionary<string, IReadOnlyList<ReadingValue>>(StringComparer.OrdinalIgnoreCase);
        if (variantOf(ecu) is not { } v || !tables.Has(v)) return result;
        var table = tables.MeasurementsOf(v);
        var defs = jobs.Select(j => table.FirstOrDefault(m => m.Result.Equals(j, StringComparison.OrdinalIgnoreCase)))
            .OfType<MeasurementDef>().ToList();
        ReadingValue Value(MeasurementDef def, byte[] data)
        {
            var number = Decode(def, data);
            var text = number?.ToString("0.###", CultureInfo.InvariantCulture) ?? Convert.ToHexString(data);
            return new ReadingValue(def.Result, text, number, Clean(def.Unit), def.Comment);
        }
        foreach (var chunk in defs.Where(d => SizeOf(d) > 0).Chunk(MaxPerRecord))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var raw = await ReadRawAsync(ecu, chunk, ct);
                for (var i = 0; i < chunk.Length; i++) result[chunk[i].Result] = [Value(chunk[i], raw[i])];
                log?.Note($"Read {chunk.Length} values in one record");
            }
            catch (DiagnosticException)
            {
                foreach (var def in chunk)
                    try { result[def.Result] = [Value(def, (await ReadRawAsync(ecu, [def], ct))[0])]; }
                    catch (DiagnosticException) { }
            }
        }
        // Values whose size the table doesn't state are read on their own.
        foreach (var def in defs.Where(d => SizeOf(d) == 0))
            try { result[def.Result] = await ReadAsync(ecu, def.Result, ct); }
            catch (DiagnosticException) { }
        return result;
    }

    /// <summary>Bytes a value takes in the record, from BMW's data type (0 when the table doesn't say).</summary>
    public static int SizeOf(MeasurementDef def)
    {
        var type = def.DataType.Trim().ToLowerInvariant().Replace("unsigend", "unsigned");
        if (type.StartsWith("0x", StringComparison.Ordinal)) return 1;
        return type switch
        {
            "unsigned char" or "signed char" => 1,
            "unsigned integer" or "signed integer" or "unsigned int" or "signed int" or "unsigned word" or "signed word" => 2,
            "unsigned long" or "signed long" or "unsigned dword" or "signed dword" or "motorola float" or "intel float" or "float" => 4,
            _ => 0,
        };
    }

    /// <summary>The $2C F0 request that defines a record holding these values, in order.</summary>
    public static byte[] DefineRequest(IReadOnlyList<MeasurementDef> defs)
    {
        var request = new List<byte> { DynamicallyDefineLocalIdentifier, RecordId };
        var position = 1;
        foreach (var d in defs)
        {
            var size = SizeOf(d) is > 0 and var s ? s : 2;
            // definitionMode 02 (by common identifier), position in the record, size, the value's id, from its first byte.
            request.AddRange([0x02, (byte)position, (byte)size, (byte)(d.Id >> 8), (byte)d.Id, 0x01]);
            position += size;
        }
        return [.. request];
    }

    /// <summary>The raw bytes of each value, read through a temporary record ($2C F0, $21 F0), or with $22 if the module refuses $2C.</summary>
    private async Task<byte[][]> ReadRawAsync(EcuDefinition ecu, IReadOnlyList<MeasurementDef> defs, CancellationToken ct)
    {
        var clear = await raw.RequestAsync(ecu, [DynamicallyDefineLocalIdentifier, RecordId, 0x04], ct)
                    ?? throw new DiagnosticException("No reply from the module.");
        if (clear[0] == 0x7F)
        {
            if (defs.Count == 1) return [await ReadByCommonIdentifierAsync(ecu, defs[0], ct)];
            throw new DiagnosticException($"The module declined a measurement record ({Kwp2000.NegativeResponseText(clear.Length > 2 ? clear[2] : (byte)0)}).");
        }
        var define = await raw.RequestAsync(ecu, DefineRequest(defs), ct) ?? throw new DiagnosticException("No reply from the module.");
        if (define[0] != DynamicallyDefineLocalIdentifier + 0x40)
            throw new DiagnosticException(define[0] == 0x7F && define.Length > 2
                ? $"The module doesn't offer {(defs.Count == 1 ? "this value" : "one of these values")} ({Kwp2000.NegativeResponseText(define[2])})."
                : $"Unexpected reply {Convert.ToHexString(define)}.");
        var reply = await raw.RequestAsync(ecu, [ReadDataByLocalIdentifier, RecordId], ct) ?? throw new DiagnosticException("No reply from the module.");
        if (reply.Length < 2 || reply[0] != ReadDataByLocalIdentifier + 0x40 || reply[1] != RecordId)
            throw new DiagnosticException(reply[0] == 0x7F && reply.Length > 2
                ? $"The module declined the reading ({Kwp2000.NegativeResponseText(reply[2])})."
                : $"Unexpected reply {Convert.ToHexString(reply)}.");

        var data = reply.AsSpan(2);
        var result = new byte[defs.Count][];
        var offset = 0;
        for (var i = 0; i < defs.Count; i++)
        {
            var size = SizeOf(defs[i]) is > 0 and var s ? s : 2;
            if (offset + size > data.Length) throw new DiagnosticException($"The reply was shorter than expected ({Convert.ToHexString(reply)}).");
            result[i] = data.Slice(offset, size).ToArray();
            offset += size;
        }
        return result;
    }

    private async Task<byte[]> ReadByCommonIdentifierAsync(EcuDefinition ecu, MeasurementDef def, CancellationToken ct)
    {
        byte[] request = [ReadDataByCommonIdentifier, (byte)(def.Id >> 8), (byte)def.Id];
        var reply = await raw.RequestAsync(ecu, request, ct) ?? throw new DiagnosticException("No reply from the module.");
        if (reply.Length >= 3 && reply[0] == 0x7F)
            throw new DiagnosticException($"The module declined this reading ({Kwp2000.NegativeResponseText(reply[2])}). Please send the traffic log so it can be supported.");
        if (reply.Length < 4 || reply[0] != ReadDataByCommonIdentifier + 0x40 || reply[1] != request[1] || reply[2] != request[2])
            throw new DiagnosticException($"Unexpected reply {Convert.ToHexString(reply)}.");
        return reply[3..];
    }

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

    private static string? Clean(string? unit) => Units.Normalize(unit);
}
