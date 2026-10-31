using System.Reflection;
using System.Text.Json;
using Klemme15.Core;

namespace Klemme15.Data;

/// <summary>
/// Klemme15's own list of Check Control IDs (db/check-control.json, plus check-control.json in the user's override
/// folder, whose entries win), with BMW's wording from the user's own BMW files shown alongside when available.
/// </summary>
public sealed class CheckControlCatalog : ICheckControlCatalog
{
    private readonly Dictionary<int, CheckControlInfo> _entries = [];
    private readonly List<ICheckControlTextSource> _official = [];

    public IReadOnlyDictionary<int, CheckControlInfo> Entries => _entries;

    public static CheckControlCatalog Load(string? overrideFolder = null)
    {
        var catalog = new CheckControlCatalog();
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Klemme15.Data.check-control.json"))
            if (stream is not null) { using var reader = new StreamReader(stream); catalog.Apply(reader.ReadToEnd()); }
        if (overrideFolder is not null)
        {
            var path = Path.Combine(overrideFolder, "check-control.json");
            if (File.Exists(path)) { try { catalog.Apply(File.ReadAllText(path)); } catch (Exception) { /* a broken user file never breaks the scan */ } }
        }
        return catalog;
    }

    /// <summary>Adds or replaces entries from one JSON document. Malformed entries are skipped one by one.</summary>
    public void Apply(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("messages", out var list) || list.ValueKind != JsonValueKind.Array) return;
        foreach (var e in list.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            if (!e.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number || !idEl.TryGetInt32(out var id) || id <= 0 || id > 0xFFFE) continue;
            var title = Str(e, "title");
            if (string.IsNullOrWhiteSpace(title)) continue;
            var severity = Enum.TryParse<CcSeverity>(Str(e, "severity"), ignoreCase: true, out var s) && Enum.IsDefined(s) ? s : CcSeverity.Warning;
            var sources = e.TryGetProperty("sources", out var src) && src.ValueKind == JsonValueKind.Array
                ? src.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
                : [];
            _entries[id] = new CheckControlInfo(title.Trim(), Str(e, "description"), severity, Str(e, "hint"), Str(e, "confidence") ?? "medium", sources, Str(e, "module"));
        }
    }

    public void SetOfficialSources(IEnumerable<ICheckControlTextSource> sources)
    {
        _official.Clear();
        _official.AddRange(sources);
    }

    public CheckControlInfo? LookupCheckControl(int id) => _entries.GetValueOrDefault(id);

    public (string Text, string Source)? OfficialCheckControlText(int id)
    {
        foreach (var source in _official)
            if (source.CheckControlText(id) is { } text) return (text, source.Name);
        return null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}

/// <summary>Somewhere BMW's own Check Control texts can be read from on the user's machine.</summary>
public interface ICheckControlTextSource
{
    string Name { get; }
    string? CheckControlText(int id);
}
