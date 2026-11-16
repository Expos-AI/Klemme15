// Klemme15: open-source BMW diagnostics
// Copyright (C) 2026 Kindric Castro
//
// This program is free software: you can redistribute it and/or modify it under the terms of the GNU General
// Public License as published by the Free Software Foundation, either version 3 of the License, or (at your
// option) any later version. This program is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
// General Public License for more details. You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// SPDX-License-Identifier: GPL-3.0-or-later

using Klemme15.Core;
using Xunit;

namespace Klemme15.Tests;

public class DriftTests
{
    [Fact]
    public void ReadingsInsideTheHealthyRangeNeverAddUp()
    {
        var d = new DriftDetector();
        var random = new Random(7);
        for (var i = 0; i < 20_000; i++) d.Add(random.NextDouble() * 9.8 - 4.9, -5, 0, 5);
        Assert.Equal(0, d.Score);
        Assert.False(d.High || d.Low);
    }

    [Fact]
    public void OneWildSampleDoesNotCountAsDrift()
    {
        var d = new DriftDetector();
        for (var i = 0; i < 50; i++) d.Add(0, -5, 0, 5);
        d.Add(500, -5, 0, 5);
        Assert.False(d.High);
        for (var i = 0; i < 5; i++) d.Add(0, -5, 0, 5);
        Assert.Equal(0, d.Score);   // and it drains away again
    }

    [Fact]
    public void StayingOutsideTheRangeIsSustainedDriftInTheRightDirection()
    {
        var high = new DriftDetector();
        for (var i = 0; i < 10; i++) high.Add(12, -5, 0, 5);
        Assert.True(high.High);
        Assert.False(high.Low);
        Assert.True(high.Score >= 1);

        var low = new DriftDetector();
        for (var i = 0; i < 10; i++) low.Add(-12, -5, 0, 5);
        Assert.True(low.Low);
        Assert.False(low.High);
    }

    private static DataLog Log(Func<int, double> trim)
    {
        var n = 300;
        return new DataLog(Enumerable.Range(0, n).Select(i => TimeSpan.FromSeconds(i * 0.5)).ToList(),
        [
            new LogChannel("Engine speed", "rpm", Enumerable.Range(0, n).Select(i => (double?)(700 + i % 5 * 20)).ToList()),
            new LogChannel("Short-term trim bank 1", "%", Enumerable.Range(0, n).Select(i => (double?)trim(i)).ToList()),
            new LogChannel("Long-term trim bank 1", "%", Enumerable.Range(0, n).Select(_ => (double?)0).ToList()),
        ]);
    }

    private static readonly BaselineCurve Curve = new()
    {
        X = "rpm", Y = "stft1+ltft1",
        Points = [new BaselinePoint(500, -5, 0, 5, 50), new BaselinePoint(750, -5, 0, 5, 50), new BaselinePoint(1000, -5, 0, 5, 50)],
    };

    [Fact]
    public void ALogThatStaysLeanIsReportedWithWhenItStarted()
    {
        var healthy = BaselineDrift.Run(Log(i => i % 3 - 1), Curve);
        Assert.NotNull(healthy);
        Assert.Null(healthy!.SustainedFrom);
        Assert.Contains("No sustained drift", healthy.Summary);

        // Healthy for the first minute, then 14 % lean for the rest.
        var lean = BaselineDrift.Run(Log(i => i < 120 ? 0 : 14), Curve);
        Assert.NotNull(lean);
        Assert.True(lean!.Above);
        Assert.NotNull(lean.SustainedFrom);
        Assert.InRange(lean.SustainedFrom!.Value.TotalSeconds, 60, 65);
        Assert.Contains("above the healthy range", lean.Summary);
    }
}
