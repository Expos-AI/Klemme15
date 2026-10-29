using System.Globalization;

namespace Klemme15.Core;

/// <summary>One recorded channel: a name, a unit and a value (or null where it wasn't read) at each sample.</summary>
public sealed record LogChannel(string Name, string Unit, IReadOnlyList<double?> Samples)
{
    public double? Min => Samples.Where(v => v is not null).Select(v => v!.Value).DefaultIfEmpty().Min();
    public double? Max => Samples.Where(v => v is not null).Select(v => v!.Value).DefaultIfEmpty().Max();
    public bool HasData => Samples.Any(v => v is not null);
}

/// <summary>A point for a scatter plot: two channel values at the same instant, with that instant's index.</summary>
public readonly record struct ScatterPoint(double X, double Y, int Sample);

/// <summary>One cell of a binned grid: the average of a value over the samples that fell in the cell.</summary>
public readonly record struct Bin(double Average, int Count);

/// <summary>
/// A datalog: a time axis and the channels recorded against it, as written by Klemme15's live-data CSV or any CSV with a
/// time column first. Everything here is pure computation on the numbers, for scatter plots, binned maps and playback.
/// </summary>
public sealed class DataLog
{
    public IReadOnlyList<TimeSpan> Times { get; }
    public IReadOnlyList<LogChannel> Channels { get; }
    public int Count => Times.Count;

    public DataLog(IReadOnlyList<TimeSpan> times, IReadOnlyList<LogChannel> channels)
    {
        Times = times;
        Channels = channels;
    }

    public TimeSpan Duration => Times.Count == 0 ? TimeSpan.Zero : Times[^1] - Times[0];

    public LogChannel? Channel(string name) => Channels.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every sample where both channels have a value.</summary>
    public IReadOnlyList<ScatterPoint> Scatter(LogChannel x, LogChannel y)
    {
        var points = new List<ScatterPoint>();
        var n = Math.Min(x.Samples.Count, y.Samples.Count);
        for (var i = 0; i < n; i++)
            if (x.Samples[i] is { } xv && y.Samples[i] is { } yv) points.Add(new ScatterPoint(xv, yv, i));
        return points;
    }

    /// <summary>
    /// Averages a value channel over a grid of two others (e.g. fuel trim across RPM and load), so a tuner can see where
    /// a correction lives. Cells with no samples come back with Count 0. Bounds are taken from the data unless given.
    /// </summary>
    public Bin[,] Map(LogChannel xAxis, LogChannel yAxis, LogChannel value, int xBins = 16, int yBins = 12,
        (double Min, double Max)? xRange = null, (double Min, double Max)? yRange = null)
    {
        if (xBins < 1 || yBins < 1) throw new ArgumentOutOfRangeException(nameof(xBins));
        var (x0, x1) = xRange ?? (xAxis.Min ?? 0, xAxis.Max ?? 1);
        var (y0, y1) = yRange ?? (yAxis.Min ?? 0, yAxis.Max ?? 1);
        if (x1 <= x0) x1 = x0 + 1;
        if (y1 <= y0) y1 = y0 + 1;
        var sum = new double[xBins, yBins];
        var count = new int[xBins, yBins];
        var n = new[] { xAxis.Samples.Count, yAxis.Samples.Count, value.Samples.Count }.Min();
        for (var i = 0; i < n; i++)
        {
            if (xAxis.Samples[i] is not { } xv || yAxis.Samples[i] is not { } yv || value.Samples[i] is not { } v) continue;
            var xi = Math.Clamp((int)((xv - x0) / (x1 - x0) * xBins), 0, xBins - 1);
            var yi = Math.Clamp((int)((yv - y0) / (y1 - y0) * yBins), 0, yBins - 1);
            sum[xi, yi] += v;
            count[xi, yi]++;
        }
        var bins = new Bin[xBins, yBins];
        for (var xi = 0; xi < xBins; xi++)
            for (var yi = 0; yi < yBins; yi++)
                bins[xi, yi] = new Bin(count[xi, yi] > 0 ? sum[xi, yi] / count[xi, yi] : 0, count[xi, yi]);
        return bins;
    }

    /// <summary>The sample nearest a point in time (for scrubbing playback). 0 for an empty log.</summary>
    public int SampleAt(TimeSpan t)
    {
        if (Times.Count == 0) return 0;
        var best = 0;
        var bestGap = (Times[0] - t).Duration();
        for (var i = 1; i < Times.Count; i++)
        {
            var gap = (Times[i] - t).Duration();
            if (gap >= bestGap) continue;
            bestGap = gap;
            best = i;
        }
        return best;
    }

    /// <summary>
    /// Parses a CSV whose first column is a time (an "HH:mm:ss.fff" clock, or seconds). The header names the channels;
    /// a trailing "(unit)" in a name is split off. Blank cells become null. Lines that don't parse are skipped.
    /// </summary>
    public static DataLog Parse(string csv)
    {
        var lines = csv.Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0).ToList();
        if (lines.Count < 2) return new DataLog([], []);
        var header = SplitCsv(lines[0]);
        var channelCount = header.Count - 1;
        var times = new List<TimeSpan>();
        var columns = new List<double?>[channelCount];
        for (var c = 0; c < channelCount; c++) columns[c] = [];

        TimeSpan? first = null;
        for (var r = 1; r < lines.Count; r++)
        {
            var cells = SplitCsv(lines[r]);
            if (cells.Count == 0 || !TryTime(cells[0], out var t)) continue;
            first ??= t;
            times.Add(t - first.Value);
            for (var c = 0; c < channelCount; c++)
            {
                var cell = c + 1 < cells.Count ? cells[c + 1].Trim() : "";
                columns[c].Add(double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null);
            }
        }
        var channels = new List<LogChannel>();
        for (var c = 0; c < channelCount; c++)
        {
            var (name, unit) = SplitUnit(header[c + 1]);
            channels.Add(new LogChannel(name, unit, columns[c]));
        }
        return new DataLog(times, channels);
    }

    private static bool TryTime(string cell, out TimeSpan t)
    {
        cell = cell.Trim().Trim('"');
        if (TimeSpan.TryParseExact(cell, [@"hh\:mm\:ss\.fff", @"hh\:mm\:ss", @"mm\:ss\.fff", @"mm\:ss"], CultureInfo.InvariantCulture, out t))
            return true;
        if (double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) { t = TimeSpan.FromSeconds(seconds); return true; }
        t = default;
        return false;
    }

    private static (string Name, string Unit) SplitUnit(string header)
    {
        var name = header.Trim().Trim('"').Trim();
        var open = name.LastIndexOf('(');
        if (open > 0 && name.EndsWith(')'))
            return (name[..open].Trim(), name[(open + 1)..^1].Trim());
        return (name, "");
    }

    /// <summary>A minimal CSV splitter: commas separate, double quotes group and "" is an escaped quote.</summary>
    private static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var cell = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else cell.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { cells.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(ch);
        }
        cells.Add(cell.ToString());
        return cells;
    }
}
