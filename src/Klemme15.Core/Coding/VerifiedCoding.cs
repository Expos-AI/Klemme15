using System.Text.Json;
using System.Text.Json.Serialization;

namespace Klemme15.Core.Coding;

/// <summary>One bit-operation a toggle performs: at a block and offset, within a mask, the byte for "on" and for "off".</summary>
public sealed record CodingOp(ushort Block, int Offset, byte Mask, byte OnByte, byte OffByte)
{
    [JsonIgnore] public bool Valid => (OnByte & ~Mask) == 0 && (OffByte & ~Mask) == 0 && OnByte != OffByte && Mask != 0
        && Offset is >= 0 and < 256 && CodingProtocol.IsCodingBlock(Block);
}

/// <summary>
/// A community-verified coding map for one exact module: which bit-operations each comfort toggle performs. It is produced
/// only from a successful NCS-Expert verification on a real car, so an owner with the same module (matched by part number
/// and coding index) can use the toggles without NCS data. It is our own numbers, contributed under GPL — never BMW's files.
/// </summary>
public sealed record VerifiedCoding
{
    public required string Chassis { get; init; }
    public required string Module { get; init; }          // short name, e.g. "FRM"
    public required string PartNumber { get; init; }
    public required string CodingIndex { get; init; }
    public string? ModuleFile { get; init; }              // the NCS file it was verified against, for provenance
    public string? Contributor { get; init; }
    public string? Source { get; init; }
    public DateTimeOffset? Verified { get; init; }
    /// <summary>Toggle id → the operations that toggle performs on this module.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<CodingOp>> Toggles { get; init; }

    /// <summary>Whether this entry is for the module now on the car (same part number and coding index).</summary>
    public bool Matches(EcuDefinition ecu, EcuIdentity? identity) =>
        Module.Equals(CodingProtocol.ShortName(ecu), StringComparison.OrdinalIgnoreCase)
        && identity?.PartNumber is { } p && p.Equals(PartNumber, StringComparison.OrdinalIgnoreCase)
        && identity.CodingIndex is { } c && c.Equals(CodingIndex, StringComparison.OrdinalIgnoreCase);

    /// <summary>Every op in the entry points at a real coding block and stores distinct, in-mask bytes.</summary>
    public bool WellFormed => !string.IsNullOrWhiteSpace(PartNumber) && !string.IsNullOrWhiteSpace(CodingIndex)
        && Toggles.Count > 0 && Toggles.Values.All(ops => ops.Count > 0 && ops.All(o => o.Valid));
}

/// <summary>
/// Turning a proven NCS verification into a shareable entry, and using shared entries to code a module without NCS data.
/// Both paths go through the same safety as the NCS path: a backup first, only the toggle's own bits change, every write is
/// read back, and the original is restored automatically if anything is off.
/// </summary>
public static class VerifiedCodingExport
{
    /// <summary>
    /// Builds a contribution from a verified module: for each comfort toggle this module supports, the exact bit-operations
    /// under the verified layout. Returns null if nothing is verified (so a half-checked module can't be contributed).
    /// </summary>
    public static VerifiedCoding? FromEvidence(EcuDefinition ecu, EcuIdentity? identity, string chassis, NcsEvidence evidence, string? contributor = null)
    {
        var layouts = evidence.Layouts;
        if (layouts.Count == 0 || identity?.PartNumber is not { } part || identity.CodingIndex is not { } index) return null;
        var module = evidence.Module;
        var layout = layouts[0];
        var toggles = new Dictionary<string, IReadOnlyList<CodingOp>>();
        foreach (var toggle in CodingToggles.All.Where(t => t.Module.Equals(CodingProtocol.ShortName(ecu), StringComparison.OrdinalIgnoreCase)))
        {
            var ops = new List<CodingOp>();
            var ok = true;
            foreach (var s in toggle.Match(module))
            {
                // Only trust a setting all verified layouts agree on, that NCS actually read, and that stores losslessly.
                if (!evidence.Trace.ContainsKey(s.Key) || layouts.Any(l => !s.Lossless(l))) { ok = false; break; }
                var (block, offset) = layout.Locate(s);
                var on = (byte)(layout.Encode(s, Value(s, toggle.OnValue)) & s.Mask);
                var off = (byte)(layout.Encode(s, Value(s, toggle.OffValue)) & s.Mask);
                // Every verified layout must agree on the location AND the exact bytes, or Klemme15 won't bake it into a shared map.
                if (layouts.Any(l => l.Locate(s) != (block, offset)
                    || (byte)(l.Encode(s, Value(s, toggle.OnValue)) & s.Mask) != on
                    || (byte)(l.Encode(s, Value(s, toggle.OffValue)) & s.Mask) != off)) { ok = false; break; }
                ops.Add(new CodingOp(block, offset, s.Mask, on, off));
            }
            if (ok && ops.Count > 0) toggles[toggle.Id] = ops;
        }
        if (toggles.Count == 0) return null;
        return new VerifiedCoding
        {
            Chassis = chassis, Module = CodingProtocol.ShortName(ecu), PartNumber = part, CodingIndex = index,
            ModuleFile = module.FileName, Contributor = contributor, Verified = DateTimeOffset.Now, Toggles = toggles,
        };
    }

    private static int Value(NcsSetting s, string name) =>
        s.Values.First(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Data;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static string ToJson(VerifiedCoding entry) => JsonSerializer.Serialize(entry, Json);
}

/// <summary>What a verified toggle state reads as on the module right now.</summary>
public enum VerifiedState { Unavailable, Off, On, Mixed, Unknown }

/// <summary>Codes a toggle from a shared verified entry (no NCS data), with the full backup/read-back/restore safety.</summary>
public static class VerifiedWriter
{
    public static VerifiedState StateOf(VerifiedCoding entry, string toggleId, IReadOnlyList<CodingBlock> blocks)
    {
        if (!entry.Toggles.TryGetValue(toggleId, out var ops)) return VerifiedState.Unavailable;
        var map = blocks.ToDictionary(b => b.Id);
        var states = new List<bool?>();
        foreach (var op in ops)
        {
            if (!map.TryGetValue(op.Block, out var b) || op.Offset >= b.Data.Length) { states.Add(null); continue; }
            var bits = (byte)(b.Data[op.Offset] & op.Mask);
            states.Add(bits == op.OnByte ? true : bits == op.OffByte ? false : (bool?)null);
        }
        if (states.Any(s => s is null)) return VerifiedState.Unknown;
        if (states.All(s => s == true)) return VerifiedState.On;
        if (states.All(s => s == false)) return VerifiedState.Off;
        return VerifiedState.Mixed;
    }

    public static Task<CodingResult> ApplyAsync(IRawRequests raw, EcuDefinition ecu, EcuIdentity? identity, VerifiedCoding entry,
        string toggleId, bool on, CodingBackup backup, IProgress<string>? progress = null, CancellationToken ct = default) =>
        ApplyAsync(raw, ecu, identity, entry, [toggleId], on, backup, progress, ct);

    /// <summary>Several options in one write (a preset), with one read-back; all succeed or the original goes back.</summary>
    public static async Task<CodingResult> ApplyAsync(IRawRequests raw, EcuDefinition ecu, EcuIdentity? identity, VerifiedCoding entry,
        IReadOnlyList<string> toggleIds, bool on, CodingBackup backup, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!CodingProtocol.IsCodable(ecu)) return new(false, $"Klemme15 doesn't code {ecu.Name}.", [], false);
        if (!entry.WellFormed) return new(false, "This verified-coding entry is malformed.", [], false);
        if (!entry.Matches(ecu, identity))
            return new(false, "This verified entry is for a different module (part number or coding index don't match this car's).", [], false);
        if (toggleIds.Count == 0) return new(false, "Nothing to change.", [], false);
        var ops = new List<CodingOp>();
        foreach (var id in toggleIds)
        {
            if (!entry.Toggles.TryGetValue(id, out var o)) return new(false, "That option isn't in the verified entry for this module.", [], false);
            ops.AddRange(o);
        }
        // Two options must never want different bits in the same place.
        foreach (var g in ops.GroupBy(o => (o.Block, o.Offset)))
        {
            var list = g.ToList();
            for (var i = 0; i < list.Count; i++)
                for (var j = i + 1; j < list.Count; j++)
                {
                    var overlap = (byte)(list[i].Mask & list[j].Mask);
                    var a = on ? list[i].OnByte : list[i].OffByte;
                    var b = on ? list[j].OnByte : list[j].OffByte;
                    if (overlap != 0 && (a & overlap) != (b & overlap))
                        return new(false, "These options disagree about the same bits, so Klemme15 won't write them together. Nothing was written.", [], false);
                }
        }
        if (ops.Any(o => backup.Blocks.All(b => b.Id != o.Block)))
            return new(false, "The backup doesn't cover the blocks this change writes. Take a fresh backup first.", [], false);

        progress?.Report("Reading the current coding...");
        var blocks = await CodingProtocol.ReadBlocksAsync(raw, ecu, ops.Select(o => o.Block).Distinct(), null, ct);
        var current = blocks.ToDictionary(b => b.Id);

        var targets = new Dictionary<ushort, byte[]>();
        foreach (var op in ops)
        {
            if (!current.TryGetValue(op.Block, out var b) || op.Offset >= b.Data.Length)
                return new(false, $"The module's coding doesn't have block {op.Block:X4} byte {op.Offset}: the entry doesn't fit this module.", [], false);
            var data = targets.TryGetValue(op.Block, out var d) ? d : (byte[])b.Data.Clone();
            data[op.Offset] = (byte)(data[op.Offset] & ~op.Mask | (on ? op.OnByte : op.OffByte));
            targets[op.Block] = data;
        }

        var changed = targets.Where(kv => !current[kv.Key].Data.AsSpan().SequenceEqual(kv.Value)).ToList();
        if (changed.Count == 0) return new(true, "Already set that way: nothing to write.", [], false);

        // Only the ops' own mask bits anywhere, as a final guard.
        foreach (var (id, data) in changed)
        {
            var before = current[id].Data;
            var allowed = new byte[data.Length];
            foreach (var op in ops.Where(o => o.Block == id)) allowed[op.Offset] |= op.Mask;
            for (var i = 0; i < data.Length; i++)
                if (((before[i] ^ data[i]) & ~allowed[i]) != 0)
                    return new(false, $"Safety stop: writing block {id:X4} would change bits outside the option. Nothing was written.", [], false);
        }

        var written = new List<CodingBlock>();
        try
        {
            foreach (var (id, data) in changed)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"Writing block {id:X4}...");
                written.Add(current[id]);
                await CodingProtocol.WriteBlockAsync(raw, ecu, new CodingBlock(id, data), CancellationToken.None);
            }
            progress?.Report("Checking...");
            var after = (await CodingProtocol.ReadBlocksAsync(raw, ecu, ops.Select(o => o.Block).Distinct(), null, CancellationToken.None)).ToDictionary(b => b.Id);
            foreach (var op in ops)
                if (!after.TryGetValue(op.Block, out var b) || (byte)(b.Data[op.Offset] & op.Mask) != (on ? op.OnByte : op.OffByte))
                    throw new DiagnosticException($"After writing, block {op.Block:X4} didn't read back as expected.");
            var diffs = changed.SelectMany(c => CodingProtocol.Compare([current[c.Key]], [new CodingBlock(c.Key, c.Value)])).ToList();
            return new(true, "Coded and checked: read back as expected. Switch the ignition off, wait 10 seconds and switch it on again.", diffs, false);
        }
        catch (Exception e)
        {
            if (written.Count == 0)
            {
                if (e is DiagnosticException or TimeoutException or OperationCanceledException) return new(false, $"Nothing was written: {e.Message}", [], false);
                throw;
            }
            progress?.Report("Putting the original coding back...");
            var failed = await CodingProtocol.WriteAllAsync(raw, ecu, written);
            return failed.Count == 0
                ? new(false, $"{e.Message} The original coding was put back.", [], true)
                : new(false, $"{e.Message} Putting back block(s) {string.Join(", ", failed)} also failed. Restore your backup.", [], false);
        }
    }
}

/// <summary>The shipped list of community-verified coding maps (db/coding-verified.json), plus any the user has imported.</summary>
public sealed class VerifiedCodingLibrary
{
    private readonly List<VerifiedCoding> _entries;
    public IReadOnlyList<VerifiedCoding> Entries => _entries;

    private VerifiedCodingLibrary(List<VerifiedCoding> entries) => _entries = entries;

    /// <summary>A library from entries already parsed (e.g. the shipped file plus the user's).</summary>
    public static VerifiedCodingLibrary FromEntries(IEnumerable<VerifiedCoding> entries) => new(entries.Where(e => e.WellFormed).ToList());

    public static VerifiedCodingLibrary Load(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new VerifiedCodingLibrary([]);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var array = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement
                : doc.RootElement.TryGetProperty("entries", out var e) ? e : default;
            var list = new List<VerifiedCoding>();
            if (array.ValueKind == JsonValueKind.Array)
                foreach (var item in array.EnumerateArray())
                    try { if (JsonSerializer.Deserialize<VerifiedCoding>(item.GetRawText()) is { } v && v.WellFormed) list.Add(v); }
                    catch (JsonException) { /* skip this entry, keep the rest */ }
            return new VerifiedCodingLibrary(list);
        }
        catch (JsonException) { return new VerifiedCodingLibrary([]); }
    }

    /// <summary>The verified entry for the module now on the car, if there is one.</summary>
    public VerifiedCoding? For(EcuDefinition ecu, EcuIdentity? identity) => _entries.FirstOrDefault(e => e.Matches(ecu, identity));
}
