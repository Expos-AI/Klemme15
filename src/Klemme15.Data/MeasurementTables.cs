using System.Globalization;
using System.Text.Json;
using Klemme15.Core;

namespace Klemme15.Data;

/// <summary>
/// Measurement tables (BMW's MESSWERTETAB) per module variant, extracted on the user's computer from the full
/// bmw-unified-db download into tables/&lt;variant&gt;.json next to the fault dictionaries. Not shipped with Klemme15.
/// </summary>
public sealed class MeasurementTables(string folder) : IMeasurementTables
{
    private readonly Dictionary<string, IReadOnlyList<MeasurementDef>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public string Folder { get; } = folder;

    private string PathOf(string variant) => Path.Combine(Folder, "tables", variant.ToLowerInvariant() + ".json");

    public bool Has(string variant) => File.Exists(PathOf(variant));

    public int Count => Directory.Exists(Path.Combine(Folder, "tables")) ? Directory.EnumerateFiles(Path.Combine(Folder, "tables"), "*.json").Count() : 0;

    public IReadOnlyList<MeasurementDef> MeasurementsOf(string variant)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(variant, out var cached)) return cached;
            var list = new List<MeasurementDef>();
            try
            {
                if (File.Exists(PathOf(variant)))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(PathOf(variant)));
                    if (doc.RootElement.TryGetProperty("measurements", out var ms))
                        foreach (var m in ms.EnumerateArray())
                        {
                            if (!OfficialLookup.TryParseCode(Str(m, "id") ?? "", out var id) || id is < 0 or > 0xFFFF) continue;
                            if (Str(m, "result") is not { Length: > 0 } result) continue;
                            list.Add(new MeasurementDef((ushort)id, result, Str(m, "comment"), Str(m, "unit"), Str(m, "type") ?? "-",
                                m.TryGetProperty("lh", out var lh) && lh.ValueKind == JsonValueKind.True,
                                Num(m, "mul", 1), Num(m, "div", 1), Num(m, "add", 0)));
                        }
                }
            }
            catch (Exception) { list.Clear(); }   // a damaged file must never break a session
            return _cache[variant] = list;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static double Num(JsonElement e, string name, double fallback) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : fallback;

    /// <summary>"0,75" (BMW writes decimal commas) or "-" → number.</summary>
    public static double? ParseBmwNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim() == "-") return null;
        return double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }
}
