using System.Text;
using System.Text.RegularExpressions;

namespace Klemme15.Core.Coding;

/// <summary>One possible value of a coding setting ("aktiv", "nicht_aktiv", "wert_02") and the number it stands for.</summary>
public sealed record NcsValue(string Name, int Data);

/// <summary>
/// One coding setting (BMW calls it a function keyword) as described by NCS Expert's module file: where it lives and the
/// values it can take. <see cref="WordAddress"/> and <see cref="ByteAddress"/> are stored exactly as in the file; how they
/// map to a coding block depends on the module family, which is why every map is checked against the real module
/// (<see cref="NcsLayout"/>) before Klemme15 trusts it.
/// </summary>
public sealed record NcsSetting(string Name, int KeywordId, uint WordAddress, int ByteAddress, byte Mask, int MaskLength,
    IReadOnlyList<NcsValue> Values, string Group, int Occurrence)
{
    /// <summary>Single-byte settings are the only ones Klemme15 decodes and changes.</summary>
    public bool Supported => MaskLength == 1 && Mask != 0;

    /// <summary>
    /// Whether every value fits its mask without losing bits and maps to a different byte, under this layout. Only such
    /// settings are ever written: anything else could store a value that reads back as something else.
    /// </summary>
    public bool Lossless(NcsLayout layout)
    {
        if (!Supported) return false;
        var seen = new HashSet<byte>();
        foreach (var v in Values)
        {
            var raw = layout.ShiftIntoMask ? v.Data << System.Numerics.BitOperations.TrailingZeroCount(Mask) : v.Data;
            if ((raw & ~Mask) != 0 || !seen.Add(layout.Encode(this, v.Data))) return false;
        }
        return true;
    }

    public string Key => Occurrence == 0 ? Name : $"{Name}#{Occurrence}";
}

/// <summary>How a setting's address and value turn into a byte in a coding block. Several are tried; the module decides.</summary>
public sealed record NcsLayout(string Id, Func<NcsSetting, (ushort Block, int Offset)> Locate, bool ShiftIntoMask)
{
    /// <summary>
    /// The candidate layouts. NCS addresses look like 0x00BBBBOO (block BBBB, byte OO), as in NCS's own netto data dumps;
    /// whether the separate byte address is added on top, and whether values are shifted into their mask, is checked
    /// against the module rather than assumed.
    /// </summary>
    public static readonly IReadOnlyList<NcsLayout> Candidates =
    [
        new("block-from-word, offset=word+byte, shifted", s => ((ushort)(s.WordAddress >> 8), (int)(s.WordAddress & 0xFF) + s.ByteAddress), true),
        new("block-from-word, offset=word+byte, unshifted", s => ((ushort)(s.WordAddress >> 8), (int)(s.WordAddress & 0xFF) + s.ByteAddress), false),
        new("block=word, offset=byte, shifted", s => ((ushort)s.WordAddress, s.ByteAddress), true),
        new("block=word, offset=byte, unshifted", s => ((ushort)s.WordAddress, s.ByteAddress), false),
        new("block-from-word, offset=byte, shifted", s => ((ushort)(s.WordAddress >> 8), s.ByteAddress), true),
        new("block-from-word, offset=byte, unshifted", s => ((ushort)(s.WordAddress >> 8), s.ByteAddress), false),
    ];

    private static int Shift(byte mask) => mask == 0 ? 0 : System.Numerics.BitOperations.TrailingZeroCount(mask);

    /// <summary>The byte value a setting's value stands for, within its mask.</summary>
    public byte Encode(NcsSetting s, int data) => (byte)((ShiftIntoMask ? data << Shift(s.Mask) : data) & s.Mask);

    /// <summary>The name of the value stored in the block, or null when the byte matches none of the setting's values.</summary>
    public string? Decode(NcsSetting s, IReadOnlyDictionary<ushort, CodingBlock> blocks)
    {
        if (!s.Supported) return null;
        var (block, offset) = Locate(s);
        if (!blocks.TryGetValue(block, out var b) || offset < 0 || offset >= b.Data.Length) return null;
        var stored = (byte)(b.Data[offset] & s.Mask);
        return s.Values.FirstOrDefault(v => Encode(s, v.Data) == stored)?.Name;
    }

    /// <summary>The coding with one setting changed. Only the setting's own bits change.</summary>
    public IReadOnlyList<CodingBlock> Apply(NcsSetting s, string value, IReadOnlyList<CodingBlock> blocks)
    {
        var v = s.Values.FirstOrDefault(x => x.Name.Equals(value, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"{s.Name} has no value '{value}'.");
        var (block, offset) = Locate(s);
        var list = blocks.ToList();
        var i = list.FindIndex(b => b.Id == block);
        if (i < 0 || offset < 0 || offset >= list[i].Data.Length) throw new InvalidOperationException($"{s.Name} points outside the module's coding.");
        var data = (byte[])list[i].Data.Clone();
        data[offset] = (byte)(data[offset] & ~s.Mask | Encode(s, v.Data));
        list[i] = new CodingBlock(block, data);
        return list;
    }
}

/// <summary>
/// Reads NCS Expert's data files that the user already has (C:\NCSEXPER\DATEN\&lt;chassis&gt;): the keyword lists
/// (SWTFSW*.DAT, SWTPSW*.DAT) and a module's description file (*.Cnn). Klemme15 ships none of these files.
/// The record layout follows what the open-source BimmerDaten project (GPLv3) documented for listing settings; the
/// addresses are then verified against the user's own module before anything is written.
/// </summary>
public static class NcsFiles
{
    /// <summary>Keyword id → name, from SWTFSW*.DAT or SWTPSW*.DAT.</summary>
    public static IReadOnlyDictionary<int, string> ParseKeywords(byte[] data)
    {
        var result = new Dictionary<int, string>();
        var ambiguous = new HashSet<int>();
        var pos = 0;
        while (pos < data.Length - 6)
        {
            if (data[pos + 2] == 0x01 && data[pos + 3] == 0x00 && data[pos + 6] >= 0x30)
            {
                var end = Array.IndexOf(data, (byte)0, pos + 6);
                if (end > pos + 6 && end - (pos + 6) < 48)
                {
                    var word = Encoding.Latin1.GetString(data, pos + 6, end - (pos + 6));
                    if (word.All(c => char.IsAsciiLetterOrDigit(c) || "_/.-&%[]".Contains(c)))
                    {
                        var id = data[pos + 4] | data[pos + 5] << 8;
                        // A keyword id seen twice with different words is ambiguous: drop it rather than guess.
                        if (result.TryGetValue(id, out var existing) && existing != word) ambiguous.Add(id);
                        else result[id] = word;
                        pos = end + 1;
                        continue;
                    }
                }
            }
            pos++;
        }
        foreach (var id in ambiguous) result.Remove(id);
        return result;
    }

    /// <summary>The settings in a module description file (*.Cnn).</summary>
    public static IReadOnlyList<NcsSetting> ParseModule(byte[] data, IReadOnlyDictionary<int, string> functions, IReadOnlyDictionary<int, string> values)
    {
        var settings = new List<NcsSetting>();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var group = "";
        var pos = 0;
        while (pos < data.Length - 20)
        {
            var type = data[pos + 2] | data[pos + 3] << 8;
            if (type == 0x0006)
            {
                var start = pos + 15;
                var end = Array.IndexOf(data, (byte)0, start);
                if (end > start + 2 && end - start < 64)
                {
                    var name = Encoding.Latin1.GetString(data, start, end - start);
                    if (name.All(c => c is >= ' ' and <= '~')) group = name;
                }
            }
            else if (type == 0x0012 && TryParseSetting(data, pos, functions, values, group, out var setting, out var next))
            {
                var n = seen.TryGetValue(setting.Name, out var c) ? c : 0;
                seen[setting.Name] = n + 1;
                settings.Add(setting with { Occurrence = n });
                pos = next;
                continue;
            }
            pos++;
        }
        return settings;
    }

    private static bool TryParseSetting(byte[] d, int pos, IReadOnlyDictionary<int, string> functions, IReadOnlyDictionary<int, string> values,
        string group, out NcsSetting setting, out int next)
    {
        setting = null!;
        next = pos + 1;
        try
        {
            var p = pos + 4;
            int blockCount = d[p++];
            p += blockCount * 4;
            var word = (uint)(d[p] | d[p + 1] << 8 | d[p + 2] << 16 | d[p + 3] << 24); p += 4;
            var byteAddr = d[p] | d[p + 1] << 8; p += 2;
            var id = d[p] | d[p + 1] << 8; p += 2;
            int indexCount = d[p++]; p += indexCount;
            int maskLength = d[p++];
            if (maskLength == 0) return false;  // no mask: not a layout Klemme15 understands
            p++;                                // reserved
            var mask = d[p++];
            if (maskLength > 1) p += maskLength - 1;
            int einCount = d[p++]; p += einCount;
            int indCount = d[p++]; p += indCount;
            var list = new List<NcsValue>();
            while (p < d.Length - 6 && d[p + 2] == 0x10 && d[p + 3] == 0x00)
            {
                p += 4;
                var vid = d[p] | d[p + 1] << 8; p += 2;
                int present = d[p++];
                var datum = 0;
                if (present > 0) { p++; datum = d[p++]; if (present > 1) p += present - 1; }
                list.Add(new NcsValue(values.TryGetValue(vid, out var vn) ? vn : $"PSW_{vid:X4}", datum));
            }
            if (list.Count == 0) return false;
            setting = new NcsSetting(functions.TryGetValue(id, out var fn) ? fn : $"FSW_{id:X4}", id, word, byteAddr, mask, maskLength, list, group, 0);
            next = p;
            return true;
        }
        catch (IndexOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// NCS Expert's "FSW_PSW.TRC" (what NCS read from the module): a keyword line followed by an indented value line.
    /// Repeated keywords get #1, #2... in the order they appear.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseTrace(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string? current = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            if (line[0] is '\t' or ' ')
            {
                if (current is not null && line.Trim() is { Length: > 0 } v)
                {
                    var n = seen.TryGetValue(current, out var c) ? c : 0;
                    seen[current] = n + 1;
                    result[n == 0 ? current : $"{current}#{n}"] = v;
                    current = null;
                }
            }
            else current = line.Trim();
        }
        return result;
    }

    /// <summary>NCS Expert's chassis folder for a Klemme15 car family (the E8x and E9x share NCS's "E89" data).</summary>
    public static IReadOnlyList<string> ChassisFolders(string family) => family.ToUpperInvariant() switch
    {
        "E8X" or "E9X" or "E84" or "E89" => ["E89", "E90", "E87"],
        "E70" => ["E70"],
        "E60" => ["E60"],
        "E46" => ["E46"],
        _ => [family],
    };

    /// <summary>The coding-index number in a module file's extension (".C05" → 5), or null.</summary>
    public static int? ExtensionIndex(string fileName)
    {
        var m = Regex.Match(fileName, @"\.C(\d\d)$", RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }
}

/// <summary>
/// What a module map was verified with: NCS Expert's reading of the module (its trace) and Klemme15's own reading of the
/// same module at that moment. The writer re-runs the verification from this itself, rather than trusting a caller's word.
/// </summary>
public sealed record NcsEvidence(NcsModule Module, IReadOnlyDictionary<string, string> Trace, IReadOnlyList<CodingBlock> Blocks)
{
    public IReadOnlyList<NcsLayout> Layouts => NcsDecoder.VerifiedLayouts(Module, Blocks, Trace);
}

/// <summary>A module description from the user's NCS data, with its decoded settings.</summary>
public sealed record NcsModule(string FileName, IReadOnlyList<NcsSetting> Settings)
{
    public IEnumerable<NcsSetting> Supported => Settings.Where(s => s.Supported);
}

/// <summary>The outcome of checking a module map against what NCS Expert read from the same module.</summary>
public sealed record NcsVerification(NcsLayout? Layout, int Compared, int Matched, IReadOnlyList<string> Mismatches)
{
    /// <summary>Every compared setting matched, with enough of them (and enough non-zero ones) to mean something.</summary>
    public bool Verified => Layout is not null && Compared >= NcsDecoder.MinCompared && Matched == Compared && Informative >= NcsDecoder.MinInformative;

    /// <summary>Matches whose stored bits aren't all zero: zero bytes match "off" values by chance, these don't.</summary>
    public int Informative { get; init; }
    public string Summary => Layout is null ? "No layout matched." : $"{Matched} of {Compared} settings match ({Layout.Id}).";
}

/// <summary>Decodes a module's coding with a module map, and checks the map against an NCS Expert trace.</summary>
public static class NcsDecoder
{
    /// <summary>Settings a trace must cover, and how many of them must be non-zero, before a map counts as verified.</summary>
    public const int MinCompared = 20, MinInformative = 8;

    private static (int Compared, int Matched, int Informative, List<string> Wrong) Score(NcsModule module, NcsLayout layout,
        IReadOnlyDictionary<ushort, CodingBlock> map, IReadOnlyDictionary<string, string> trace)
    {
        int compared = 0, matched = 0, informative = 0;
        var wrong = new List<string>();
        foreach (var s in module.Supported)
        {
            if (!trace.TryGetValue(s.Key, out var expected)) continue;
            compared++;
            var actual = layout.Decode(s, map);
            if (actual is not null && actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                matched++;
                var v = s.Values.First(x => x.Name.Equals(actual, StringComparison.OrdinalIgnoreCase));
                if (layout.Encode(s, v.Data) != 0) informative++;
            }
            else if (wrong.Count < 10) wrong.Add($"{s.Key}: NCS {expected}, Klemme15 {actual ?? "?"}");
        }
        return (compared, matched, informative, wrong);
    }

    /// <summary>Current value of every supported setting (null where the byte matches no known value).</summary>
    public static IReadOnlyList<(NcsSetting Setting, string? Value)> Decode(NcsModule module, NcsLayout layout, IReadOnlyList<CodingBlock> blocks)
    {
        var map = blocks.ToDictionary(b => b.Id);
        return module.Supported.Select(s => (s, layout.Decode(s, map))).ToList();
    }

    /// <summary>How well a layout explains the module's coding without any trace: the share of multi-value settings
    /// (which can't match by chance as easily as on/off ones) that decode to a known value.</summary>
    public static double Plausibility(NcsModule module, NcsLayout layout, IReadOnlyList<CodingBlock> blocks)
    {
        var map = blocks.ToDictionary(b => b.Id);
        var hard = module.Supported.Where(s => s.Values.Count < (1 << System.Numerics.BitOperations.PopCount(s.Mask))).ToList();
        if (hard.Count == 0) return 0;
        return (double)hard.Count(s => layout.Decode(s, map) is not null) / hard.Count;
    }

    /// <summary>Tries every layout against the trace; the best one wins, and only a full match counts as verified.</summary>
    public static NcsVerification Verify(NcsModule module, IReadOnlyList<CodingBlock> blocks, IReadOnlyDictionary<string, string> trace)
    {
        NcsVerification? best = null;
        var map = blocks.ToDictionary(b => b.Id);
        foreach (var layout in NcsLayout.Candidates)
        {
            var (compared, matched, informative, wrong) = Score(module, layout, map, trace);
            var result = new NcsVerification(layout, compared, matched, wrong) { Informative = informative };
            if (best is null || matched > best.Matched) best = result;
        }
        return best ?? new NcsVerification(null, 0, 0, []);
    }

    /// <summary>
    /// The layouts that decode the trace perfectly. Klemme15 only writes when all of them agree on the bytes to write,
    /// so an ambiguity can never pick the wrong bits.
    /// </summary>
    public static IReadOnlyList<NcsLayout> VerifiedLayouts(NcsModule module, IReadOnlyList<CodingBlock> blocks, IReadOnlyDictionary<string, string> trace)
    {
        var map = blocks.ToDictionary(b => b.Id);
        var list = new List<NcsLayout>();
        foreach (var layout in NcsLayout.Candidates)
        {
            var (compared, matched, informative, _) = Score(module, layout, map, trace);
            if (compared >= MinCompared && matched == compared && informative >= MinInformative) list.Add(layout);
        }
        return list;
    }
}
