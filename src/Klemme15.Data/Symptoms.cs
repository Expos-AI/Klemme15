using System.Reflection;
using System.Text.Json;

namespace Klemme15.Data;

/// <summary>One thing to do for a symptom: a test, chain, guide or check by id, or a page of the app.</summary>
/// <param name="Kind">"test", "chain", "guide", "check" or "page".</param>
public sealed record SymptomStep(string Kind, string Id, string Why);

/// <summary>What an owner notices ("won't start"), and what to do about it, in order.</summary>
public sealed record Symptom(string Id, string Title, string Summary, IReadOnlyList<SymptomStep> Steps);

/// <summary>Symptom-first help (db/symptoms.json): start from what the car does, not from a code.</summary>
public static class SymptomLibrary
{
    /// <summary>Pages a step can point at.</summary>
    public static readonly IReadOnlyList<string> Pages = ["quick-test", "drain-finder", "ghost-hunter"];

    public static IReadOnlyList<Symptom> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Klemme15.Data.symptoms.json");
        if (stream is null) return [];
        using var doc = JsonDocument.Parse(stream);
        var list = new List<Symptom>();
        foreach (var s in doc.RootElement.GetProperty("symptoms").EnumerateArray())
            list.Add(new(s.GetProperty("id").GetString()!, s.GetProperty("title").GetString()!, s.GetProperty("summary").GetString()!,
                s.GetProperty("steps").EnumerateArray()
                    .Select(x => new SymptomStep(x.GetProperty("kind").GetString()!, x.GetProperty("id").GetString()!, x.GetProperty("why").GetString()!)).ToList()));
        return list;
    }
}
