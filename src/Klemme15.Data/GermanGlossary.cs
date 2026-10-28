using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Klemme15.Data;

/// <summary>
/// Puts BMW's German fault texts into rough English with Klemme15's own glossary (db/glossary-de-en.txt),
/// splitting compound words into known parts. Returns null when too many words are unknown, so the user
/// sees BMW's original German (with a translate link) rather than a half-translated guess.
/// </summary>
public static class GermanGlossary
{
    private static readonly Lazy<Dictionary<string, string>> Words = new(Load);
    private static readonly string[] Links = ["", "s", "n", "en", "e", "es"];
    private static readonly Regex Tokens = new(@"[A-Za-zÄÖÜäöüß]+|[^A-Za-zÄÖÜäöüß]+", RegexOptions.Compiled);

    /// <summary>Share of ordinary words that must be known for a translation to be shown.</summary>
    public const double MinimumCoverage = 0.75;

    public static int Count => Words.Value.Count;

    public static string? Translate(string german)
    {
        var dict = Words.Value;
        // BMW ids like FEHLER_EXTERNAL_WATCHDOG or CAN_ID_19E_ERROR_Ausfall_Botschaft: underscores are word breaks.
        var text = WebUtility.HtmlDecode(german).Replace('_', ' ');
        var sb = new StringBuilder();
        int words = 0, known = 0;
        foreach (Match m in Tokens.Matches(text))
        {
            var t = m.Value;
            if (!char.IsLetter(t[0])) { sb.Append(t); continue; }
            var key = Normalize(t);
            var isAbbreviation = t.Length == 1 || t.All(c => !char.IsLower(c));
            if (isAbbreviation)
            {
                // Abbreviations (DME, VANOS) stay as they are; shouted words in BMW ids (FEHLER, WATCHDOG) are translated.
                if (t.Length >= 4 && dict.TryGetValue(key, out var caps))
                {
                    words++; known++;
                    sb.Append(caps);
                }
                else sb.Append(t);
                continue;
            }
            words++;
            if (dict.TryGetValue(key, out var en)) { sb.Append(en); known++; continue; }
            if (Split(key, dict) is { } parts) { sb.Append(string.Join(' ', parts.Select(p => dict[p]))); known++; continue; }
            sb.Append(t);
        }
        if (words == 0 || (double)known / words < MinimumCoverage) return null;
        var s = sb.ToString().Trim();
        return s.Length == 0 ? null : char.ToUpperInvariant(s[0]) + s[1..];
    }

    /// <summary>
    /// Fixes BMW's half-translated English ("Drehzahlfuehler right rear electrically defective"): only longer words the
    /// glossary knows as German are replaced, so the English around them is left alone. Never returns null.
    /// </summary>
    public static string Polish(string mixed)
    {
        var dict = Words.Value;
        var sb = new StringBuilder();
        foreach (Match m in Tokens.Matches(WebUtility.HtmlDecode(mixed)))
        {
            var t = m.Value;
            if (!char.IsLetter(t[0]) || t.Length < 6 || t.All(c => !char.IsLower(c))) { sb.Append(t); continue; }
            var key = Normalize(t);
            if (dict.TryGetValue(key, out var en) && !en.Equals(key, StringComparison.OrdinalIgnoreCase)) sb.Append(en);
            else if (!dict.ContainsKey(key) && Split(key, dict) is { } parts) sb.Append(string.Join(' ', parts.Select(p => dict[p])));
            else sb.Append(t);
        }
        var s = sb.ToString().Trim();
        return s.Length == 0 ? mixed : char.ToUpperInvariant(s[0]) + s[1..];
    }

    /// <summary>Lower case, umlauts as ae/oe/ue, ß as ss: the form BMW itself often uses.</summary>
    public static string Normalize(string word) =>
        word.ToLowerInvariant().Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");

    /// <summary>Splits a compound into the fewest known parts (each at least 3 letters), allowing German linking letters.</summary>
    private static List<string>? Split(string word, Dictionary<string, string> dict)
    {
        var memo = new Dictionary<int, List<string>?>();
        List<string>? Best(int i)
        {
            if (i == word.Length) return [];
            if (memo.TryGetValue(i, out var cached)) return cached;
            List<string>? best = null;
            for (var j = word.Length; j >= i + 3; j--)
            {
                var part = word[i..j];
                if (!dict.ContainsKey(part)) continue;
                foreach (var link in Links)
                {
                    if (link.Length > 0 && (j + link.Length > word.Length || string.CompareOrdinal(word, j, link, 0, link.Length) != 0)) continue;
                    var rest = Best(j + link.Length);
                    if (rest is not null && (best is null || rest.Count + 1 < best.Count)) best = [part, .. rest];
                }
            }
            memo[i] = best;
            return best;
        }
        var result = Best(0);
        return result is { Count: > 1 } ? result : null;
    }

    private static Dictionary<string, string> Load()
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Klemme15.Data.glossary-de-en.txt");
        if (stream is null) return dict;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            dict[Normalize(line[..eq].Trim())] = line[(eq + 1)..].Trim();
        }
        return dict;
    }
}
