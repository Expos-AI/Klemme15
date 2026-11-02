using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Klemme15.Core;

/// <summary>The healthy range of a value at one point on the operating axis: the middle 80 % of healthy samples, and their median.</summary>
public sealed record BaselinePoint(double X, double P10, double Median, double P90, int Samples);

/// <summary>One healthy curve: a value (Y) across an operating axis (X), e.g. air mass across engine speed.</summary>
public sealed record BaselineCurve
{
    /// <summary>The value: a signal id ("maf", "load", "stft1+ltft1" for total trim).</summary>
    public required string Y { get; init; }

    /// <summary>The operating axis: "rpm" or "load".</summary>
    public required string X { get; init; }

    /// <summary>Only samples where this holds count (a ChainExpression over signal ids, e.g. "coolant > 70").</summary>
    public string? Filter { get; init; }

    public double BinWidth { get; init; } = 250;
    public required IReadOnlyList<BaselinePoint> Points { get; init; }
    public string Title => $"{BaselineSignals.Label(Y)} across {BaselineSignals.Label(X).ToLowerInvariant()}";
}

/// <summary>
/// A "known good" profile: healthy curves for one engine, built from logs of healthy cars. It says how many cars it's
/// built from and under what conditions, because one car is a hint, not an answer key.
/// </summary>
public sealed record BaselineProfile
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public IReadOnlyList<string> Engines { get; init; } = [];          // "N52"; empty = any
    public IReadOnlyList<string> Variants { get; init; } = [];         // "MSV70"
    public string Conditions { get; init; } = "";
    public int Cars { get; init; }

    /// <summary>A made-up example (the demo car), to show how overlays work. Never shown as a real baseline.</summary>
    public bool IsExample { get; init; }
    public IReadOnlyList<string> Contributors { get; init; } = [];
    public IReadOnlyList<string> Sources { get; init; } = [];
    public required IReadOnlyList<BaselineCurve> Curves { get; init; }

    public string Trust => IsExample ? "Example from the demo car: not a real engine"
        : Cars >= 3 ? $"Built from {Cars} healthy cars"
        : $"Built from {Cars} car{(Cars == 1 ? "" : "s")}: treat it as a hint";

    public bool Fits(string? engineCode) => Engines.Count == 0 || engineCode is null
        || Engines.Any(e => engineCode.StartsWith(e, StringComparison.OrdinalIgnoreCase));

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true, WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static BaselineProfile? Parse(string json)
    {
        try
        {
            var p = JsonSerializer.Deserialize<BaselineProfile>(json, Json);
            return p is { Curves.Count: > 0 } && p.Curves.All(c => c.Points.Count >= 2 && c.Points.All(pt => pt.P10 <= pt.Median && pt.Median <= pt.P90)) ? p : null;
        }
        catch (JsonException) { return null; }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);
}

/// <summary>Finds a signal in a datalog by its id, whatever the log called it ("Engine speed (rpm)", "RPM", "rpm").</summary>
public static class BaselineSignals
{
    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["rpm"] = ["engine speed", "rpm", "engine rpm"],
        ["maf"] = ["air mass", "mass air flow", "maf", "air flow"],
        ["load"] = ["engine load", "load", "calculated load"],
        ["coolant"] = ["coolant temperature", "coolant", "engine coolant temperature"],
        ["iat"] = ["intake air", "intake air temperature", "iat"],
        ["stft1"] = ["short-term trim bank 1", "short-term fuel trim bank 1", "stft1"],
        ["ltft1"] = ["long-term trim bank 1", "long-term fuel trim bank 1", "ltft1"],
        ["stft2"] = ["short-term trim bank 2", "short-term fuel trim bank 2", "stft2"],
        ["ltft2"] = ["long-term trim bank 2", "long-term fuel trim bank 2", "ltft2"],
        ["throttle"] = ["throttle", "throttle position"],
        ["voltage"] = ["battery voltage", "voltage", "control module voltage"],
    };

    public static string Label(string id) => id switch
    {
        "stft1+ltft1" => "Total trim bank 1", "stft2+ltft2" => "Total trim bank 2",
        _ => GhostHunter.SignalLabels.Name(id),
    };

    public static LogChannel? Find(DataLog log, string id) =>
        Aliases.TryGetValue(id, out var names)
            ? log.Channels.FirstOrDefault(c => names.Contains(c.Name.Trim().ToLowerInvariant()))
            : log.Channel(id);

    /// <summary>The value of a signal (or a sum like "stft1+ltft1") at one sample, or null.</summary>
    public static double? At(DataLog log, string id, int sample, Dictionary<string, LogChannel?> cache)
    {
        double total = 0;
        foreach (var part in id.Split('+'))
        {
            if (!cache.TryGetValue(part, out var ch)) cache[part] = ch = Find(log, part);
            if (ch?.Samples[sample] is not { } v) return null;
            total += v;
        }
        return total;
    }

    public static bool Has(DataLog log, string id) => id.Split('+').All(p => Find(log, p) is not null);
}

/// <summary>How a log compares with a healthy curve. MedianOffset is your median minus the healthy median, averaged over the curve, in the value's own unit.</summary>
public sealed record BaselineComparison(IReadOnlyList<BaselinePoint> Yours, double ShareInside, double MedianOffset, int Samples, string Summary);

/// <summary>
/// Puts a time-series log on the same footing as a baseline: every sample is placed by its operating point (rpm or load),
/// samples are grouped into the baseline's bins, and each bin's median is compared with the healthy range there.
/// Time drops out completely, so a drive and a baseline recorded on different days compare fairly.
/// </summary>
public static class BaselineAligner
{
    public static BaselineComparison? Compare(DataLog log, BaselineCurve curve)
    {
        if (!BaselineSignals.Has(log, curve.X) || !BaselineSignals.Has(log, curve.Y)) return null;
        var bins = Bin(log, curve.X, curve.Y, curve.Filter, curve.BinWidth, minSamples: 1);
        var cache = new Dictionary<string, LogChannel?>();
        int inside = 0, counted = 0, filteredOut = 0;
        var offsets = new List<double>();
        foreach (var b in bins)
        {
            var reference = Nearest(curve, b.X);
            if (reference is null) continue;
            offsets.Add(b.Median - reference.Median);
        }
        var filter = FilterFor(log, curve.Filter);
        for (var i = 0; i < log.Count; i++)
        {
            if (BaselineSignals.At(log, curve.X, i, cache) is not { } x || BaselineSignals.At(log, curve.Y, i, cache) is not { } y) continue;
            if (filter is not null && !filter.IsTrue(Vars(log, i, cache))) { filteredOut++; continue; }
            if (Nearest(curve, x) is not { } r) continue;
            counted++;
            if (y >= r.P10 && y <= r.P90) inside++;
        }
        if (counted == 0 && filteredOut > 0)
            return new(bins, 0, 0, 0, $"This baseline only counts readings where {curve.Filter}, and none of your log does (a cold engine runs richer on purpose). Record a drive once the engine is warm.");
        if (counted == 0) return new(bins, 0, 0, 0, $"None of your log falls in the range this baseline covers ({curve.Points[0].X:0}-{curve.Points[^1].X:0} {Unit(curve.X)}).");
        var share = (double)inside / counted;
        var offset = offsets.Count > 0 ? offsets.Average() : 0;
        return new(bins, share, offset, counted, Summarise(curve, bins, share, offset));
    }

    /// <summary>Groups samples into bins on the X axis and takes percentiles of Y in each (used to build baselines too).</summary>
    public static IReadOnlyList<BaselinePoint> Bin(DataLog log, string xId, string yId, string? filterText, double binWidth, int minSamples)
    {
        var cache = new Dictionary<string, LogChannel?>();
        var filter = FilterFor(log, filterText);
        var groups = new SortedDictionary<double, List<double>>();
        for (var i = 0; i < log.Count; i++)
        {
            if (BaselineSignals.At(log, xId, i, cache) is not { } x || BaselineSignals.At(log, yId, i, cache) is not { } y) continue;
            if (filter is not null && !filter.IsTrue(Vars(log, i, cache))) continue;
            var centre = Math.Round(x / binWidth) * binWidth;
            if (!groups.TryGetValue(centre, out var list)) groups[centre] = list = [];
            list.Add(y);
        }
        return groups.Where(g => g.Value.Count >= minSamples)
            .Select(g => { var s = g.Value.Order().ToList(); return new BaselinePoint(g.Key, Pct(s, 0.10), Pct(s, 0.50), Pct(s, 0.90), s.Count); })
            .ToList();
    }

    /// <summary>A healthy curve from one or more healthy logs (for contributors making a baseline).</summary>
    public static BaselineCurve Build(IEnumerable<DataLog> logs, string xId, string yId, string? filter, double binWidth, int minSamples = 20)
    {
        var all = logs.ToList();
        var points = all.SelectMany(l => RawPairs(l, xId, yId, filter, binWidth)).GroupBy(p => p.X).OrderBy(g => g.Key)
            .Where(g => g.Count() >= minSamples)
            .Select(g => { var s = g.Select(p => p.Y).Order().ToList(); return new BaselinePoint(g.Key, Pct(s, 0.10), Pct(s, 0.50), Pct(s, 0.90), s.Count); })
            .ToList();
        return new BaselineCurve { X = xId, Y = yId, Filter = filter, BinWidth = binWidth, Points = points };
    }

    private static IEnumerable<(double X, double Y)> RawPairs(DataLog log, string xId, string yId, string? filterText, double binWidth)
    {
        var cache = new Dictionary<string, LogChannel?>();
        var filter = FilterFor(log, filterText);
        for (var i = 0; i < log.Count; i++)
        {
            if (BaselineSignals.At(log, xId, i, cache) is not { } x || BaselineSignals.At(log, yId, i, cache) is not { } y) continue;
            if (filter is not null && !filter.IsTrue(Vars(log, i, cache))) continue;
            yield return (Math.Round(x / binWidth) * binWidth, y);
        }
    }

    private static readonly string[] FilterSignals = ["rpm", "load", "coolant", "iat", "maf", "throttle", "voltage"];

    /// <summary>The filter, or none when the log lacks a signal it needs (a log without coolant can't be held to "coolant > 70").</summary>
    private static ChainExpression? FilterFor(DataLog log, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var e = ChainExpression.Parse(text);
            e.CheckNames(FilterSignals.Where(id => BaselineSignals.Has(log, id)).ToHashSet());
            return e;
        }
        catch (ChainExpressionException) { return null; }
    }

    private static Dictionary<string, double> Vars(DataLog log, int i, Dictionary<string, LogChannel?> cache)
    {
        var vars = new Dictionary<string, double>();
        foreach (var id in FilterSignals)
            if (BaselineSignals.At(log, id, i, cache) is { } v) vars[id] = v;
        return vars;
    }

    /// <summary>The healthy point for an X value: the bin it falls in, or null outside the curve.</summary>
    public static BaselinePoint? Nearest(BaselineCurve curve, double x)
    {
        var p = curve.Points.MinBy(p => Math.Abs(p.X - x));
        return p is not null && Math.Abs(p.X - x) <= curve.BinWidth * 0.75 ? p : null;
    }

    private static double Pct(IReadOnlyList<double> sorted, double q)
    {
        if (sorted.Count == 1) return sorted[0];
        var pos = q * (sorted.Count - 1);
        var lo = (int)Math.Floor(pos);
        var hi = Math.Min(lo + 1, sorted.Count - 1);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
    }

    private static string Unit(string id) => GhostHunter.SignalLabels.Unit(id);

    private static string Summarise(BaselineCurve curve, IReadOnlyList<BaselinePoint> yours, double share, double offset)
    {
        var y = BaselineSignals.Label(curve.Y).ToLowerInvariant();
        var unit = Unit(curve.Y.Split('+')[0]);
        string V(double v) => v.ToString(Math.Abs(v) < 20 ? "0.0" : "0", CultureInfo.CurrentCulture) + (unit.Length > 0 ? (unit == "%" ? " %" : " " + unit) : "");
        var head = $"{share:P0} of your readings sit inside the healthy range.";
        // The stretch where you're furthest from the median, measured against the healthy spread there.
        var matched = yours.Select(b => (b, r: Nearest(curve, b.X))).Where(t => t.r is not null)
            .Select(t => (t.b, r: t.r!, dev: (t.b.Median - t.r!.Median) / Math.Max(1e-6, (t.r.P90 - t.r.P10) / 2))).ToList();
        if (matched.Count == 0) return head;
        var worst = matched.MaxBy(t => Math.Abs(t.dev));
        if (Math.Abs(worst.dev) < 1) return head + $" Your {y} follows the healthy curve.";
        var direction = worst.dev < 0 ? "below" : "above";
        var text = head + $" Your {y} reads {direction} the healthy range around {worst.b.X:0} {Unit(curve.X)}: {V(worst.b.Median)} where healthy engines read {V(worst.r.Median)}.";
        // Mention the whole-curve shift too when it's large compared with the typical healthy spread.
        var spread = matched.Average(t => (t.r.P90 - t.r.P10) / 2);
        if (Math.Abs(offset) >= spread) text += $" Across the whole range it's {V(Math.Abs(offset))} {(offset < 0 ? "lower" : "higher")} on average.";
        return text;
    }
}

/// <summary>Baselines the user made or downloaded, one JSON file each in a folder on this computer.</summary>
public sealed class BaselineStore(string folder)
{
    public string Folder { get; } = folder;

    public IReadOnlyList<BaselineProfile> Load()
    {
        if (!Directory.Exists(Folder)) return [];
        var list = new List<BaselineProfile>();
        foreach (var file in Directory.EnumerateFiles(Folder, "*.json").Order())
        {
            try
            {
                if (new FileInfo(file).Length > 2_000_000) continue;
                if (BaselineProfile.Parse(File.ReadAllText(file)) is { } p) list.Add(p);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return list;
    }

    /// <summary>Saves a profile under a file name made from its id (letters, digits and dashes only).</summary>
    public string Save(BaselineProfile profile)
    {
        Directory.CreateDirectory(Folder);
        var safe = new string(profile.Id.Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? char.ToLowerInvariant(c) : '-').ToArray()).Trim('-');
        var path = Path.Combine(Folder, (safe.Length == 0 ? "baseline" : safe) + ".json");
        File.WriteAllText(path, profile.ToJson());
        return path;
    }

    /// <summary>A baseline from one of your own logs: the curves this log has data for, across engine speed.</summary>
    public static BaselineProfile? FromLog(DataLog log, string id, string title, string? engine, string conditions)
    {
        var curves = new List<BaselineCurve>();
        foreach (var y in new[] { "stft1+ltft1", "stft2+ltft2", "maf", "load" })
        {
            if (!BaselineSignals.Has(log, "rpm") || !BaselineSignals.Has(log, y)) continue;
            var filter = BaselineSignals.Has(log, "coolant") ? "coolant > 70" : null;
            var curve = BaselineAligner.Build([log], "rpm", y, filter, 250, minSamples: 5);
            if (curve.Points.Count >= 2) curves.Add(curve);
        }
        return curves.Count == 0 ? null : new BaselineProfile
        {
            Id = id, Title = title, Engines = engine is null ? [] : [engine], Conditions = conditions, Cars = 1, Curves = curves,
        };
    }
}
