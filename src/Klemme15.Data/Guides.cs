using System.Reflection;
using System.Text.Json;
using Klemme15.Core;

namespace Klemme15.Data;

/// <summary>One step of a troubleshooting guide.</summary>
/// <param name="Text">What to do or check, in plain words.</param>
/// <param name="Module">Short module name whose readings help ("DME", "DSC"), or null.</param>
/// <param name="Readings">Search text for the Readings page ("trim", "VANOS"), used with <see cref="Module"/>.</param>
/// <param name="Live">True when the generic Live data page shows what this step needs.</param>
/// <param name="Engines">Only for these engines (e.g. "N54"); empty for all.</param>
public sealed record GuideStep(string Text, string? Module, string? Readings, bool Live, IReadOnlyList<string> Engines);

/// <summary>A troubleshooting guide for a family of faults (lean mixture, misfire, VANOS...).</summary>
public sealed record Guide(
    string Id,
    string Title,
    string Summary,
    IReadOnlyList<string> Safety,
    IReadOnlyList<GuideStep> Steps,
    IReadOnlyList<string> ObdCodes,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Modules)
{
    /// <summary>Steps that apply to this engine (all steps when the engine is unknown).</summary>
    public IReadOnlyList<GuideStep> StepsFor(string? engine) =>
        Steps.Where(s => s.Engines.Count == 0 || engine is null
                         || s.Engines.Any(e => engine.StartsWith(e, StringComparison.OrdinalIgnoreCase))).ToList();
}

/// <summary>
/// Klemme15's own troubleshooting guides (db/guides.json, written by contributors). A guide is offered for a fault
/// when its OBD-II code is listed, or when the fault's meaning mentions one of the guide's keywords.
/// </summary>
public sealed class GuideLibrary
{
    private readonly List<Guide> _guides;

    private GuideLibrary(List<Guide> guides) => _guides = guides;

    public IReadOnlyList<Guide> All => _guides;

    public static GuideLibrary Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Klemme15.Data.guides.json");
        if (stream is null) return new GuideLibrary([]);
        using var doc = JsonDocument.Parse(stream);
        static IReadOnlyList<string> List(JsonElement e, string name) =>
            e.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
        var guides = doc.RootElement.GetProperty("guides").EnumerateArray().Select(g => new Guide(
            g.GetProperty("id").GetString()!,
            g.GetProperty("title").GetString()!,
            g.GetProperty("summary").GetString()!,
            List(g, "safety"),
            g.GetProperty("steps").EnumerateArray().Select(s => new GuideStep(
                s.GetProperty("text").GetString()!,
                s.TryGetProperty("module", out var m) ? m.GetString() : null,
                s.TryGetProperty("readings", out var r) ? r.GetString() : null,
                s.TryGetProperty("live", out var l) && l.ValueKind == JsonValueKind.True,
                List(s, "engines"))).ToList(),
            List(g, "obd").Select(c => c.ToUpperInvariant()).ToList(),
            List(g, "keywords").Select(k => k.ToLowerInvariant()).ToList(),
            List(g, "modules"))).ToList();
        return new GuideLibrary(guides);
    }

    private static readonly Dictionary<string, System.Text.RegularExpressions.Regex> Patterns = new();

    /// <summary>"lean" matches the whole word only; "*raddrehzahl*" also inside longer (compound) words.</summary>
    private static System.Text.RegularExpressions.Regex KeywordRegex(string keyword)
    {
        lock (Patterns)
        {
            if (Patterns.TryGetValue(keyword, out var r)) return r;
            var core = System.Text.RegularExpressions.Regex.Escape(keyword.Trim('*'));
            var pattern = (keyword.StartsWith('*') ? "" : @"(?<![\p{L}\d])") + core + (keyword.EndsWith('*') ? "" : @"(?![\p{L}\d])");
            return Patterns[keyword] = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }
    }

    /// <summary>The best guide for a fault, or null. An exact OBD-II code wins over a keyword match.</summary>
    public Guide? For(Fault fault, EcuDefinition ecu)
    {
        var module = ecu.Id.Split('.').Last();
        bool ModuleOk(Guide g) => g.Modules.Count == 0 || g.Modules.Contains(module, StringComparer.OrdinalIgnoreCase);

        if (fault.Source == FaultSource.Obd2)
            if (_guides.FirstOrDefault(g => g.ObdCodes.Contains(fault.Code.ToUpperInvariant())) is { } byCode) return byCode;

        var text = string.Join(" | ", new[] { fault.Description, fault.OfficialText }.Where(t => t is not null)).ToLowerInvariant();
        if (text.Length == 0) return null;
        return _guides
            .Where(ModuleOk)
            .Select(g => (g, hits: g.Keywords.Count(k => KeywordRegex(k).IsMatch(text))))
            .Where(x => x.hits > 0)
            .OrderByDescending(x => x.hits)
            .Select(x => x.g)
            .FirstOrDefault();
    }
}
