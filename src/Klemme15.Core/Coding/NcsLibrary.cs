using System.Globalization;

namespace Klemme15.Core.Coding;

/// <summary>A module file that may describe the car's module, with how well it fits.</summary>
public sealed record NcsCandidate(string FileName, double Score, string Why);

/// <summary>
/// The user's own NCS Expert data folder (usually C:\NCSEXPER\DATEN). Klemme15 only reads it; nothing from it is copied
/// into Klemme15 or uploaded anywhere.
/// </summary>
public sealed class NcsLibrary
{
    private readonly string _folder;
    private readonly IReadOnlyDictionary<int, string> _functions;
    private readonly IReadOnlyDictionary<int, string> _values;
    private readonly Dictionary<string, NcsModule> _cache = new(StringComparer.OrdinalIgnoreCase);

    private NcsLibrary(string folder, IReadOnlyDictionary<int, string> functions, IReadOnlyDictionary<int, string> values)
    {
        _folder = folder;
        _functions = functions;
        _values = values;
    }

    public string Folder => _folder;

    /// <summary>Opens the chassis folder for a car family inside a DATEN folder (or the chassis folder itself). Null if not found.</summary>
    public static NcsLibrary? Open(string datenOrChassisFolder, string family)
    {
        if (!Directory.Exists(datenOrChassisFolder)) return null;
        var folders = new List<string>();
        foreach (var name in NcsFiles.ChassisFolders(family))
            folders.AddRange(Directory.EnumerateDirectories(datenOrChassisFolder).Where(d => Path.GetFileName(d).Equals(name, StringComparison.OrdinalIgnoreCase)));
        folders.Add(datenOrChassisFolder);
        foreach (var folder in folders)
        {
            var fsw = Find(folder, "SWTFSW") ?? Find(datenOrChassisFolder, "SWTFSW");
            var psw = Find(folder, "SWTPSW") ?? Find(datenOrChassisFolder, "SWTPSW");
            if (fsw is null || psw is null || !ModuleFilesIn(folder).Any()) continue;
            return new NcsLibrary(folder, NcsFiles.ParseKeywords(File.ReadAllBytes(fsw)), NcsFiles.ParseKeywords(File.ReadAllBytes(psw)));
        }
        return null;
    }

    private static string? Find(string folder, string prefix) =>
        Directory.EnumerateFiles(folder).Where(f => Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                                                    && f.EndsWith(".DAT", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();

    private static IEnumerable<string> ModuleFilesIn(string folder) =>
        Directory.EnumerateFiles(folder).Where(f => NcsFiles.ExtensionIndex(Path.GetFileName(f)) is not null);

    public IReadOnlyList<string> ModuleFiles => ModuleFilesIn(_folder).Select(Path.GetFileName).OrderBy(f => f).ToList()!;

    public NcsModule Load(string fileName)
    {
        if (_cache.TryGetValue(fileName, out var m)) return m;
        var path = Path.Combine(_folder, Path.GetFileName(fileName));
        m = new NcsModule(Path.GetFileName(path), NcsFiles.ParseModule(File.ReadAllBytes(path), _functions, _values));
        _cache[fileName] = m;
        return m;
    }

    /// <summary>File name prefixes NCS uses for each Klemme15 module.</summary>
    public static IReadOnlyList<string> PrefixesFor(string shortName) => shortName.ToUpperInvariant() switch
    {
        "FRM" => ["FRM", "NFRM"],
        "KOMBI" => ["KMB", "KOMB"],
        "JBE" => ["JBBF", "JBE"],
        "IHKA" => ["IHK"],
        "FZD" => ["FZD"],
        "LM" => ["LM", "AHL"],
        "KBM" => ["KBM"],
        "PDC" => ["PDC"],
        "SZL" => ["SZL"],
        "SHD" => ["SHD"],
        _ => [shortName.ToUpperInvariant()],
    };

    /// <summary>
    /// Module files that could describe this module, best first: same coding index, a matching name, then either the
    /// overlap with NCS Expert's trace (when there is one) or how plausibly the file decodes the module's coding.
    /// </summary>
    public IReadOnlyList<NcsCandidate> Candidates(string shortName, string? codingIndex, IReadOnlyList<CodingBlock> blocks,
        IReadOnlyDictionary<string, string>? trace = null)
    {
        var indexes = new HashSet<int>();
        if (codingIndex is { Length: > 0 } ci)
        {
            var clean = ci.Replace("0x", "", StringComparison.OrdinalIgnoreCase);
            if (int.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)) indexes.Add(hex);
            if (int.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec)) indexes.Add(dec);
        }
        var prefixes = PrefixesFor(shortName);
        var files = ModuleFiles.Where(f => prefixes.Any(p => f.StartsWith(p, StringComparison.OrdinalIgnoreCase))).ToList();
        if (indexes.Count > 0 && files.Any(f => indexes.Contains(NcsFiles.ExtensionIndex(f) ?? -1)))
            files = files.Where(f => indexes.Contains(NcsFiles.ExtensionIndex(f) ?? -1)).ToList();

        var list = new List<NcsCandidate>();
        foreach (var f in files)
        {
            NcsModule module;
            try { module = Load(f); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            if (!module.Supported.Any()) continue;
            if (trace is { Count: > 0 })
            {
                var names = module.Supported.Select(s => s.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var overlap = (double)trace.Keys.Count(names.Contains) / trace.Count;
                list.Add(new(f, overlap, $"{overlap:P0} of NCS Expert's settings are in this file"));
            }
            else
            {
                var best = NcsLayout.Candidates.Max(l => NcsDecoder.Plausibility(module, l, blocks));
                list.Add(new(f, best, $"{best:P0} of its multi-value settings decode to a known value"));
            }
        }
        return list.OrderByDescending(c => c.Score).ToList();
    }
}
