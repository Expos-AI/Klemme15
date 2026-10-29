using System.Text.Json;
using System.Text.Json.Serialization;

namespace Klemme15.Core.Coding;

/// <summary>One coding data block as the module stores it ($22/$2E identifier 0x3000-0x3EFF, usually 16 bytes).</summary>
public sealed record CodingBlock(ushort Id, byte[] Data)
{
    public string IdHex => Id.ToString("X4");
}

/// <summary>
/// A copy of a module's complete coding, saved before any change so it can always be put back. It records which module
/// it came from (part number and coding index) so it is never restored to a different one. Nothing in it identifies the
/// car: the vehicle order and VIN blocks (0x3F00 and up) are never read.
/// </summary>
public sealed record CodingBackup
{
    public required string ModuleId { get; init; }
    public required string ModuleName { get; init; }
    public string? PartNumber { get; init; }
    public string? CodingIndex { get; init; }
    public string? Variant { get; init; }
    public required DateTimeOffset Taken { get; init; }
    public string? Note { get; init; }
    public required IReadOnlyList<CodingBlock> Blocks { get; init; }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new BlockConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static CodingBackup FromJson(string json) =>
        JsonSerializer.Deserialize<CodingBackup>(json, Json) ?? throw new InvalidDataException("Not a Klemme15 coding backup.");

    /// <summary>A file name like "E9x.FRM 2026-10-28 1432.k15coding.json".</summary>
    public string FileName => $"{ModuleId} {Taken.LocalDateTime:yyyy-MM-dd HHmmss}.k15coding.json";

    private sealed class BlockConverter : JsonConverter<CodingBlock>
    {
        public override CodingBlock Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var id = Convert.ToUInt16(doc.RootElement.GetProperty("id").GetString(), 16);
            var data = Convert.FromHexString(doc.RootElement.GetProperty("data").GetString()!.Replace(" ", ""));
            return new CodingBlock(id, data);
        }

        public override void Write(Utf8JsonWriter writer, CodingBlock value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.IdHex);
            writer.WriteString("data", Convert.ToHexString(value.Data));
            writer.WriteEndObject();
        }
    }
}

/// <summary>One byte that differs between two codings, with the bits that changed.</summary>
public sealed record CodingDifference(ushort Block, int Offset, byte Before, byte After)
{
    public byte ChangedBits => (byte)(Before ^ After);
    public override string ToString() => $"{Block:X4}+{Offset:X2}: {Before:X2} -> {After:X2} (bits {ChangedBits:X2})";
}

/// <summary>
/// Reading and writing coding blocks with the standard BMW-FAST coding requests: $22 ReadDataByCommonIdentifier and
/// $2E WriteDataByCommonIdentifier on identifiers 0x3000-0x3EFF (the range BMW's own coding jobs use). The vehicle
/// order (0x3F00-0x3F7F), the change index (0x3FFF) and the VIN are outside that range and never touched.
/// </summary>
public static class CodingProtocol
{
    public const ushort FirstBlock = 0x3000, LastBlock = 0x3EFF;

    /// <summary>Modules Klemme15 may code: comfort and lighting only. Never the key/immobiliser module, engine, gearbox,
    /// brakes or airbag modules.</summary>
    public static readonly IReadOnlySet<string> CodableModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "FRM", "KOMBI", "JBE", "IHKA", "FZD", "LM", "KBM", "PDC", "SHD",
    };

    public static readonly IReadOnlySet<string> NeverCoded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "CAS", "EWS", "ELV", "DME", "DDE", "EGS", "DSC", "ACSM", "MRS", "ABG", "SINE", "EPS", "AFS", "EMF", "VGSG",
        "SZL", // carries the steering angle sensor DSC relies on
    };

    public static string ShortName(EcuDefinition ecu) => ecu.Id.Contains('.') ? ecu.Id[(ecu.Id.LastIndexOf('.') + 1)..] : ecu.Id;

    public static bool IsCodable(EcuDefinition ecu) =>
        !ecu.IsEngine && CodableModules.Contains(ShortName(ecu)) && !NeverCoded.Contains(ShortName(ecu));

    public static bool IsCodingBlock(ushort id) => id is >= FirstBlock and <= LastBlock;

    /// <summary>Reads one block. Null when the module says the block doesn't exist (request out of range / not supported).</summary>
    public static async Task<byte[]?> ReadBlockAsync(IRawRequests raw, EcuDefinition ecu, ushort id, CancellationToken ct = default)
    {
        if (!IsCodingBlock(id)) throw new ArgumentOutOfRangeException(nameof(id), "Only coding blocks 3000-3EFF are read.");
        var reply = await raw.RequestAsync(ecu, [0x22, (byte)(id >> 8), (byte)id], ct);
        if (reply is null) throw new DiagnosticException($"{ecu.Name} didn't answer the coding read.");
        if (reply.Length >= 3 && reply[0] == 0x7F)
            return reply[2] is 0x31 or 0x12 or 0x11 ? null
                : throw new DiagnosticException($"{ecu.Name} refused to read its coding (code 0x{reply[2]:X2}).");
        if (reply.Length < 3 || reply[0] != 0x62 || reply[1] != (byte)(id >> 8) || reply[2] != (byte)id)
            throw new DiagnosticException($"{ecu.Name} sent an unexpected reply to the coding read.");
        return reply[3..];
    }

    /// <summary>Reads the given blocks; missing ones are left out.</summary>
    public static async Task<IReadOnlyList<CodingBlock>> ReadBlocksAsync(IRawRequests raw, EcuDefinition ecu, IEnumerable<ushort> ids,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var list = new List<CodingBlock>();
        var all = ids.Distinct().OrderBy(x => x).ToList();
        for (var i = 0; i < all.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Reading coding block {all[i]:X4} ({i + 1} of {all.Count})");
            if (await ReadBlockAsync(raw, ecu, all[i], ct) is { } data) list.Add(new CodingBlock(all[i], data));
        }
        return list;
    }

    /// <summary>
    /// Finds every coding block a module has without any BMW data: each 0x30xx-0x3Exx page is probed from xx00 upwards
    /// and abandoned after <paramref name="gap"/> missing blocks in a row (coding blocks are numbered consecutively).
    /// </summary>
    public static async Task<IReadOnlyList<CodingBlock>> DiscoverAsync(IRawRequests raw, EcuDefinition ecu,
        IProgress<string>? progress = null, CancellationToken ct = default, int gap = 16)
    {
        var list = new List<CodingBlock>();
        for (var page = 0x30; page <= 0x3E; page++)
        {
            var misses = 0;
            for (var low = 0; low <= 0xFF && misses < gap; low++)
            {
                ct.ThrowIfCancellationRequested();
                var id = (ushort)(page << 8 | low);
                progress?.Report($"Looking for coding blocks: {id:X4} ({list.Count} found)");
                var data = await ReadBlockAsync(raw, ecu, id, ct);
                if (data is null) { misses++; continue; }
                misses = 0;
                list.Add(new CodingBlock(id, data));
            }
        }
        return list;
    }

    /// <summary>Writes one block and reads it back. Throws if the module refuses or the read-back differs.</summary>
    public static async Task WriteBlockAsync(IRawRequests raw, EcuDefinition ecu, CodingBlock block, CancellationToken ct = default)
    {
        if (!IsCodingBlock(block.Id)) throw new ArgumentOutOfRangeException(nameof(block), "Only coding blocks 3000-3EFF are written.");
        if (!IsCodable(ecu)) throw new InvalidOperationException($"Klemme15 doesn't code {ecu.Name}.");
        var request = new byte[3 + block.Data.Length];
        request[0] = 0x2E;
        request[1] = (byte)(block.Id >> 8);
        request[2] = (byte)block.Id;
        block.Data.CopyTo(request, 3);
        var reply = await raw.RequestAsync(ecu, request, ct) ?? throw new DiagnosticException($"{ecu.Name} didn't answer the coding write.");
        if (reply.Length >= 3 && reply[0] == 0x7F)
            throw new DiagnosticException($"{ecu.Name} refused the coding write ({reply[2] switch { 0x22 => "conditions not correct", 0x33 => "security access needed", 0x31 => "block out of range", 0x72 => "memory write failed", _ => $"code 0x{reply[2]:X2}" }}).");
        if (reply.Length < 3 || reply[0] != 0x6E || reply[1] != (byte)(block.Id >> 8) || reply[2] != (byte)block.Id)
            throw new DiagnosticException($"{ecu.Name} sent an unexpected reply to the coding write.");
        var back = await ReadBlockAsync(raw, ecu, block.Id, ct);
        if (back is null || !back.AsSpan().SequenceEqual(block.Data))
            throw new DiagnosticException($"{ecu.Name} accepted block {block.IdHex} but reads back something different.");
    }

    /// <summary>The bytes that differ between two codings of the same module (blocks missing from either side are skipped).</summary>
    public static IReadOnlyList<CodingDifference> Compare(IReadOnlyList<CodingBlock> before, IReadOnlyList<CodingBlock> after)
    {
        var map = after.ToDictionary(b => b.Id);
        var diffs = new List<CodingDifference>();
        foreach (var b in before.OrderBy(b => b.Id))
        {
            if (!map.TryGetValue(b.Id, out var a)) continue;
            for (var i = 0; i < Math.Min(a.Data.Length, b.Data.Length); i++)
                if (a.Data[i] != b.Data[i]) diffs.Add(new CodingDifference(b.Id, i, b.Data[i], a.Data[i]));
        }
        return diffs;
    }

    /// <summary>
    /// Whether a backup belongs to this module: same module, part number and coding index. Unknown on either side counts
    /// as a mismatch, so a backup is never written to a module Klemme15 can't match it to.
    /// </summary>
    public static string? Mismatch(CodingBackup backup, EcuDefinition ecu, EcuIdentity? identity)
    {
        if (!backup.ModuleId.Equals(ecu.Id, StringComparison.OrdinalIgnoreCase))
            return $"This backup is from {backup.ModuleName}, not {ecu.Name}.";
        if (backup.PartNumber is null || identity?.PartNumber is null)
            return "Klemme15 can't confirm this backup belongs to this module (part number unknown). Run the quick test so the module is identified.";
        if (!backup.PartNumber.Equals(identity.PartNumber, StringComparison.OrdinalIgnoreCase))
            return $"This backup is from a module with part number {backup.PartNumber}; the car's module is {identity.PartNumber}.";
        if (backup.CodingIndex is null || identity.CodingIndex is null)
            return "Klemme15 can't confirm the coding index of this backup or module, so it won't restore it.";
        if (!backup.CodingIndex.Equals(identity.CodingIndex, StringComparison.OrdinalIgnoreCase))
            return $"This backup has coding index {backup.CodingIndex}; the module now reports {identity.CodingIndex}. Its coding layout may differ.";
        return null;
    }

    /// <summary>Writes every block, carrying on past failures; returns the blocks that failed.</summary>
    public static async Task<IReadOnlyList<string>> WriteAllAsync(IRawRequests raw, EcuDefinition ecu, IEnumerable<CodingBlock> blocks)
    {
        var failed = new List<string>();
        foreach (var block in blocks)
        {
            try { await WriteBlockAsync(raw, ecu, block, CancellationToken.None); }
            catch (Exception) { failed.Add(block.IdHex); }
        }
        return failed;
    }

    /// <summary>
    /// Puts a backup back: only blocks that differ are written, each one read back. Every block in the backup must exist on
    /// the module with the same length, or nothing is written. If a write fails part-way, the blocks already written are
    /// put back to what the module had before. Returns how many blocks were written.
    /// </summary>
    public static async Task<int> RestoreAsync(IRawRequests raw, EcuDefinition ecu, CodingBackup backup, EcuIdentity? identity,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (Mismatch(backup, ecu, identity) is { } why) throw new InvalidOperationException(why);
        if (backup.Blocks.Any(b => !IsCodingBlock(b.Id))) throw new InvalidOperationException("The backup contains blocks outside the coding range.");
        var current = await ReadBlocksAsync(raw, ecu, backup.Blocks.Select(b => b.Id), progress, ct);
        var now = current.ToDictionary(b => b.Id);
        foreach (var block in backup.Blocks)
            if (!now.TryGetValue(block.Id, out var have) || have.Data.Length != block.Data.Length)
                throw new InvalidOperationException($"Block {block.IdHex} in the backup doesn't match the module's layout. Nothing was written.");
        var changed = backup.Blocks.Where(b => !now[b.Id].Data.AsSpan().SequenceEqual(b.Data)).ToList();
        var done = new List<CodingBlock>();
        foreach (var block in changed)
        {
            progress?.Report($"Restoring block {block.IdHex}");
            try
            {
                await WriteBlockAsync(raw, ecu, block, CancellationToken.None); // a restore is never left half done by a cancel
                done.Add(block);
            }
            catch (Exception e)
            {
                var failed = await WriteAllAsync(raw, ecu, done.Select(b => now[b.Id]));
                throw new DiagnosticException($"Restoring block {block.IdHex} failed ({e.Message}). "
                    + (failed.Count == 0 ? "The module was put back to how it was before the restore." : $"Putting back block(s) {string.Join(", ", failed)} also failed."));
            }
        }
        return done.Count;
    }
}
