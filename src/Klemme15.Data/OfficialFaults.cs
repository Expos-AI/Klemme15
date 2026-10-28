using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Klemme15.Core;

namespace Klemme15.Data;

/// <summary>The official meaning found for one code, and how sure we are which module variant it belongs to.</summary>
public sealed record OfficialMatch(string Variant, OfficialEntry Entry, string SourceName, bool VariantConfirmed, IReadOnlyList<string> Alternatives);

/// <summary>
/// Picks the right per-variant fault dictionary for a module and looks a code up in it.
/// The same hex code can mean different things on different variants (e.g. 5E1A on DSC_87 vs DXC_90),
/// so the variant is taken from the module's own answer whenever possible.
/// </summary>
public static class OfficialLookup
{
    public static OfficialMatch? Find(IReadOnlyList<IOfficialFaultSource> sources, EcuDefinition ecu, EcuIdentity? identity, string? engine, string code)
    {
        if (sources.Count == 0 || !TryParseCode(code, out var number)) return null;

        bool Has(string v) => sources.Any(s => s.HasVariant(v));
        var confirmed = false;
        List<string> candidates;

        // 1. The module named its variant (EDIABAS mode).
        if (identity?.Variant is { Length: > 0 } named && Has(named.ToLowerInvariant()))
        {
            candidates = [named.ToLowerInvariant()];
            confirmed = true;
        }
        else
        {
            candidates = ecu.Variants.Select(v => v.ToLowerInvariant()).Where(Has).ToList();

            // 2. The module's BMW part number belongs to exactly one known variant.
            if (PartDigits(identity?.PartNumber) is { } part)
            {
                var byPart = candidates.Where(v => sources.Any(s => s.PartNumbersOf(v).Contains(part))).ToList();
                if (byPart.Count == 1) { candidates = byPart; confirmed = true; }
            }

            // 3. Engine computers: keep the ones made for this engine.
            if (!confirmed && engine is { Length: > 0 })
            {
                var forEngine = candidates.Where(v =>
                {
                    var engines = sources.SelectMany(s => s.EnginesOf(v)).ToList();
                    return engines.Count == 0 || engines.Any(e => engine.StartsWith(e, StringComparison.OrdinalIgnoreCase) || e.StartsWith(engine, StringComparison.OrdinalIgnoreCase));
                }).ToList();
                // Exact engine code first (MSV70 is "N52"; MSV80 is "N52KP"), then the rest in database order.
                if (forEngine.Count > 0)
                    candidates = forEngine.OrderBy(v => sources.SelectMany(s => s.EnginesOf(v)).Any(e => e.Equals(engine, StringComparison.OrdinalIgnoreCase)) ? 0 : 1).ToList();
            }
        }

        var hits = new List<(string Variant, OfficialEntry Entry, string Source)>();
        foreach (var v in candidates)
        {
            foreach (var s in sources)
            {
                if (s.HasVariant(v) && s.Lookup(v, number) is { } e && !IsPlaceholder(e.Text))
                {
                    hits.Add((v, e, s.Name));
                    break;
                }
            }
        }
        if (hits.Count == 0) return null;

        var first = hits[0];
        var key = Normalize(Clean(first.Entry.Text, code));
        var alternatives = hits.Skip(1)
            .Where(h => Normalize(Clean(h.Entry.Text, code)) != key)
            .GroupBy(h => Normalize(Clean(h.Entry.Text, code)))
            .Take(3)
            .Select(g => $"{g.First().Variant.ToUpperInvariant()}: {Clean(g.First().Entry.English ?? g.First().Entry.Text, code)}")
            .ToList();

        return new OfficialMatch(first.Variant.ToUpperInvariant(), first.Entry, first.Source, confirmed || hits.Count == 1 && candidates.Count == 1, alternatives);
    }

    /// <summary>"2A82", "0x2A82" or "01" → number.</summary>
    public static bool TryParseCode(string code, out int number)
    {
        var c = code.Trim();
        if (c.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) c = c[2..];
        return int.TryParse(c, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>Last 7 digits of a BMW part number ("7 551 012" → "7551012").</summary>
    public static string? PartDigits(string? partNumber)
    {
        if (partNumber is null) return null;
        var digits = new string(partNumber.Where(char.IsAsciiDigit).ToArray());
        return digits.Length >= 7 ? digits[^7..] : null;
    }

    /// <summary>Removes the code BMW repeats at the start of many texts ("2A82 Einlass-VANOS", "5E1A - CAN ...").</summary>
    public static string Clean(string text, string code)
    {
        var t = System.Net.WebUtility.HtmlDecode(text).Trim();
        if (TryParseCode(code, out var n))
        {
            var m = Regex.Match(t, @"^(0x)?([0-9A-Fa-f]{2,6})\s*(-|:)?\s+");
            if (m.Success && int.TryParse(m.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var lead) && lead == n)
                t = t[m.Length..];
        }
        t = t.TrimEnd('.', ' ');
        return t.Length == 0 ? text.Trim() : t;
    }

    private static string Normalize(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9äöüß]+", "");

    private static bool IsPlaceholder(string text)
    {
        var t = text.Trim();
        return t.Length == 0 || t.Trim('-', ' ').Length == 0 || t.Contains("nicht bedatet", StringComparison.OrdinalIgnoreCase) || t.Equals("unbekannter Fehlerort", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The JSON mirror of bmw-unified-db (github.com/gediz/bmw-unified-db), downloaded by the user into their
/// own data folder: one variants/&lt;sgbd&gt;.json per module variant. Not shipped with Klemme15.
/// </summary>
public sealed class UnifiedDbSource(string folder) : IOfficialFaultSource
{
    private sealed record Variant(Dictionary<int, OfficialEntry> Codes, HashSet<string> Engines, HashSet<string> Parts);
    private readonly Dictionary<string, Variant?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public string Name => "bmw-unified-db (BMW data, downloaded by you)";
    public string Folder { get; } = folder;

    private string PathOf(string variant) => System.IO.Path.Combine(Folder, "variants", variant.ToLowerInvariant() + ".json");

    public bool HasVariant(string variant) => File.Exists(PathOf(variant));

    public OfficialEntry? Lookup(string variant, int code) => Load(variant)?.Codes.GetValueOrDefault(code);
    public IReadOnlyCollection<string> EnginesOf(string variant) => (IReadOnlyCollection<string>?)Load(variant)?.Engines ?? [];
    public IReadOnlyCollection<string> PartNumbersOf(string variant) => (IReadOnlyCollection<string>?)Load(variant)?.Parts ?? [];

    /// <summary>Number of module variants available.</summary>
    public int VariantCount => Directory.Exists(System.IO.Path.Combine(Folder, "variants"))
        ? Directory.EnumerateFiles(System.IO.Path.Combine(Folder, "variants"), "*.json").Count() : 0;

    private Variant? Load(string variant)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(variant, out var cached)) return cached;
            Variant? v = null;
            try
            {
                var path = PathOf(variant);
                if (File.Exists(path))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
                    var root = doc.RootElement;
                    var codes = new Dictionary<int, OfficialEntry>();
                    if (root.TryGetProperty("dtcs", out var dtcs))
                    {
                        foreach (var d in dtcs.EnumerateArray())
                        {
                            if (!OfficialLookup.TryParseCode(Str(d, "code") ?? "", out var n)) continue;
                            var de = Str(d, "location_text");
                            if (de is null) continue;
                            var en = Str(d, "location_text_en");
                            // FORTTEXTE (fault memory) wins over IORTTEXTE (info memory) when both define a code.
                            if (!codes.ContainsKey(n) || Str(d, "source_table") == "FORTTEXTE")
                                codes[n] = new OfficialEntry(de, string.IsNullOrWhiteSpace(en) ? null : en);
                        }
                    }
                    var engines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    // Engine codes as BMW writes them (N52, N52KP, M54); the family only when no variant is given.
                    if (root.TryGetProperty("engines", out var eng))
                        foreach (var e in eng.EnumerateArray())
                            if (Str(e, "relation") == "engine_ecu" && (Str(e, "engine_variant") ?? Str(e, "engine")) is { } code)
                                engines.Add(code);
                    var parts = new HashSet<string>();
                    if (root.TryGetProperty("hardware_numbers", out var hw))
                        foreach (var h in hw.EnumerateArray())
                            if (OfficialLookup.PartDigits(Str(h, "hwnr")) is { } p) parts.Add(p);
                    v = new Variant(codes, engines, parts);
                }
            }
            catch (Exception) { v = null; }   // a damaged file must never break a scan
            _cache[variant] = v;
            return v;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}

/// <summary>
/// The user's own EDIABAS "Ecu" folder (installed with ISTA, INPA or EDIABAS). Fault texts are read from
/// the FORTTEXTE table of each variant's .prg file on the user's machine; nothing is copied into Klemme15.
/// </summary>
public sealed class SgbdFolderSource : IOfficialFaultSource
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<int, OfficialEntry>?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public SgbdFolderSource(string folder)
    {
        Folder = folder;
        if (Directory.Exists(folder))
            foreach (var f in Directory.EnumerateFiles(folder, "*.*").Where(f => f.EndsWith(".prg", StringComparison.OrdinalIgnoreCase)))
                _files[System.IO.Path.GetFileNameWithoutExtension(f)] = f;
    }

    public string Folder { get; }
    public string Name => "your BMW files";
    public int VariantCount => _files.Count;

    public bool HasVariant(string variant) => _files.ContainsKey(variant);
    public IReadOnlyCollection<string> EnginesOf(string variant) => [];
    public IReadOnlyCollection<string> PartNumbersOf(string variant) => [];

    public OfficialEntry? Lookup(string variant, int code)
    {
        lock (_lock)
        {
            if (!_cache.TryGetValue(variant, out var table))
            {
                table = null;
                if (_files.TryGetValue(variant, out var path))
                {
                    try
                    {
                        var f = SgbdFile.Open(path);
                        table = new Dictionary<int, OfficialEntry>();
                        foreach (var name in new[] { "IORTTEXTE", "FORTTEXTE" })   // fault memory last, so it wins
                            foreach (var row in f.ReadTable(name) ?? [])
                                if (row.TryGetValue("ORT", out var ort) && row.TryGetValue("ORTTEXT", out var text) && OfficialLookup.TryParseCode(ort, out var n))
                                    table[n] = new OfficialEntry(text, null);
                    }
                    catch (Exception) { table = null; }
                }
                _cache[variant] = table;
            }
            return table?.GetValueOrDefault(code);
        }
    }
}
