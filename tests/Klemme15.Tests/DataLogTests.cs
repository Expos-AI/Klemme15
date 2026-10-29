using Klemme15.Core;
using Xunit;

namespace Klemme15.Tests;

public class DataLogTests
{
    private const string Csv =
        "time,\"Engine speed (rpm)\",\"Load (%)\",\"Short-term fuel trim bank 1 (%)\"\n" +
        "10:00:00.000,800,18,2.0\n" +
        "10:00:00.500,2500,55,-1.0\n" +
        "10:00:01.000,2500,55,\n" +      // a blank cell becomes null
        "10:00:01.500,4000,80,-4.0\n";

    [Fact]
    public void Parses_header_units_times_and_blanks()
    {
        var log = DataLog.Parse(Csv);
        Assert.Equal(4, log.Count);
        Assert.Equal(["Engine speed", "Load", "Short-term fuel trim bank 1"], log.Channels.Select(c => c.Name).ToArray());
        var rpm = log.Channel("Engine speed")!;
        Assert.Equal("rpm", rpm.Unit);
        Assert.Equal(800, rpm.Min);
        Assert.Equal(4000, rpm.Max);
        Assert.Null(log.Channel("Short-term fuel trim bank 1")!.Samples[2]);
        Assert.Equal(TimeSpan.Zero, log.Times[0]);
        Assert.Equal(TimeSpan.FromSeconds(1.5), log.Times[3]);
        Assert.Equal(TimeSpan.FromSeconds(1.5), log.Duration);
    }

    [Fact]
    public void Scatter_skips_samples_where_either_channel_is_missing()
    {
        var log = DataLog.Parse(Csv);
        var points = log.Scatter(log.Channel("Engine speed")!, log.Channel("Short-term fuel trim bank 1")!);
        Assert.Equal(3, points.Count); // the null trim row is left out
        Assert.Equal(800, points[0].X);
        Assert.Equal(2.0, points[0].Y);
        Assert.Equal(0, points[0].Sample);
    }

    [Fact]
    public void Map_averages_a_value_over_the_two_axes()
    {
        var log = DataLog.Parse(Csv);
        // Two samples at 2500 rpm / 55 % load: one has trim -1, the other is null, so the average is just -1.
        var map = log.Map(log.Channel("Engine speed")!, log.Channel("Load")!, log.Channel("Short-term fuel trim bank 1")!,
            xBins: 4, yBins: 4, xRange: (0, 4000), yRange: (0, 100));
        var hot = 0;
        foreach (var bin in map) if (bin.Count > 0) hot++;
        Assert.True(hot >= 2);
        // The 4000 rpm / 80 % sample lands in the top bins with trim -4.
        var xi = Math.Clamp((int)(4000.0 / 4000 * 4), 0, 3);
        var yi = Math.Clamp((int)(80.0 / 100 * 4), 0, 3);
        Assert.Equal(-4.0, map[xi, yi].Average);
    }

    [Fact]
    public void SampleAt_finds_the_nearest_instant_for_scrubbing()
    {
        var log = DataLog.Parse(Csv);
        Assert.Equal(0, log.SampleAt(TimeSpan.Zero));
        Assert.Equal(1, log.SampleAt(TimeSpan.FromSeconds(0.4)));
        Assert.Equal(3, log.SampleAt(TimeSpan.FromSeconds(9)));
    }

    [Fact]
    public void Round_trips_the_apps_own_csv_with_seconds_times_too()
    {
        var log = DataLog.Parse("t,\"Boost (kPa)\"\n0,100\n1.5,180\n3,95\n");
        Assert.Equal(3, log.Count);
        Assert.Equal(TimeSpan.FromSeconds(3), log.Duration);
        Assert.Equal(180, log.Channel("Boost")!.Max);
    }

    [Fact]
    public void An_empty_or_header_only_csv_is_safe()
    {
        Assert.Equal(0, DataLog.Parse("").Count);
        Assert.Equal(0, DataLog.Parse("time,rpm\n").Count);
        Assert.Equal(0, DataLog.Parse("time,rpm\n").Channel("rpm")?.Samples.Count ?? 0);
    }
}
