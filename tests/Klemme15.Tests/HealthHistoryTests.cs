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

public class HealthHistoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static HealthSnapshot S(int day, double? volts = null, double? rpm = 0, double? ltft = null, string[]? faults = null, int[]? cc = null) => new()
    {
        Time = T0.AddDays(day),
        CarKey = "abc123",
        Voltage = volts,
        Rpm = rpm,
        Ltft1 = ltft,
        Faults = faults ?? [],
        DashMessages = cc ?? [],
    };

    [Fact]
    public void KeyIsStableOneWayAndHidesTheVin()
    {
        var a = HealthSnapshot.KeyFor("WBAVB13586PT00000");
        Assert.Equal(a, HealthSnapshot.KeyFor(" wbavb13586pt00000 "));
        Assert.Equal(12, a.Length);
        Assert.False(a.Contains("wba", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(a, HealthSnapshot.KeyFor("WBAVB13586PT00001"));
    }

    [Fact]
    public void FallingRestingVoltageIsNoticed()
    {
        var t = HealthTrends.Analyze([S(0, 12.65), S(10, 12.6), S(20, 12.45), S(30, 12.35)]);
        Assert.Contains(t, x => x.Title.Contains("falling") && x.Level == TrendLevel.Watch);
        Assert.DoesNotContain(t, x => x.Title.Contains("low at rest"));
    }

    [Fact]
    public void LowRestingAndBadChargingAreFlagged()
    {
        Assert.Contains(HealthTrends.Analyze([S(0, 12.0)]), x => x.Title == "Battery low at rest" && x.Level == TrendLevel.Act);
        Assert.Contains(HealthTrends.Analyze([S(0, 13.1, rpm: 750)]), x => x.Title == "Charging voltage low");
        Assert.Contains(HealthTrends.Analyze([S(0, 15.4, rpm: 750)]), x => x.Title == "Charging voltage high");
        Assert.Empty(HealthTrends.Analyze([S(0, 14.2, rpm: 750)]));
    }

    [Fact]
    public void EngineRunningIsNotMistakenForRestingBattery()
    {
        // 14.1 V with the engine running must not be read as a resting value, so no "falling" from 14 to 12.5.
        var t = HealthTrends.Analyze([S(0, 14.1, rpm: 800), S(5, 14.0, rpm: 800), S(10, 12.6), S(15, 12.6), S(20, 12.58)]);
        Assert.DoesNotContain(t, x => x.Title.Contains("falling"));
    }

    [Fact]
    public void CreepingFuelTrimsAreNoticedOnlyOverTime()
    {
        var slow = HealthTrends.Analyze([S(0, 14, 750, 1), S(10, 14, 750, 2), S(20, 14, 750, 5), S(30, 14, 750, 7)]);
        Assert.Contains(slow, x => x.Title == "Fuel trims creeping leaner");
        var sameDay = HealthTrends.Analyze([S(0, 14, 750, 1), S(0, 14, 750, 2), S(0, 14, 750, 7), S(0, 14, 750, 8)]);
        Assert.DoesNotContain(sameDay, x => x.Title.StartsWith("Fuel trims"));
        Assert.Contains(HealthTrends.Analyze([S(0, 14, 750, 12)]), x => x.Title == "Engine running lean");
        Assert.Contains(HealthTrends.Analyze([S(0, 14, 750, -11)]), x => x.Title == "Engine running rich");
    }

    [Fact]
    public void ReturningFaultsAndNewOnes()
    {
        var f = "E9x.FZD:A670";
        var t = HealthTrends.Analyze([S(0, faults: [f]), S(3, faults: []), S(6, faults: [f]), S(9, faults: [f, "E9x.JBE:A731"])]);
        Assert.Contains(t, x => x.Title == "FZD A670 keeps coming back");
        Assert.Contains(t, x => x.Title == "New since last scan" && x.Detail == "JBE A731");
        Assert.True(t[0].Level >= t[^1].Level);
    }

    [Fact]
    public void StoreRoundTripsAndForgets()
    {
        var dir = Path.Combine(Path.GetTempPath(), "k15-hist-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new HealthHistoryStore(dir);
            store.Add(S(5, 12.5));
            store.Add(S(1, 12.6) with { ModelName = "330i" });
            File.AppendAllText(Path.Combine(dir, "abc123.jsonl"), "not json\n");
            var loaded = store.Load("abc123");
            Assert.Equal(2, loaded.Count);
            Assert.True(loaded[0].Time < loaded[1].Time);
            Assert.Single(store.Cars());
            store.Forget("abc123");
            Assert.Empty(store.Cars());
            Assert.Empty(store.Load("../../etc/passwd"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}

public class HealthReportTests
{
    [Fact]
    public void ReportsAndForumPostCarryTheTrends()
    {
        var trends = new List<HealthTrend> { new(TrendLevel.Watch, "Fuel trims creeping leaner", "From +1 % to +7 % over 30 days.") };
        var scan = new VehicleScanResult
        {
            Started = DateTimeOffset.Now, Backend = "test", Ecus = [], Vin = "WBAVB13586PT00000",
            Health = new HealthSummary(5, DateTimeOffset.Now.AddDays(-30), trends),
        };
        Assert.Contains("Health over time", ReportWriter.ToMarkdown(scan));
        Assert.Contains("Fuel trims creeping leaner", ReportWriter.ToHtml(scan));
        var post = ReportWriter.ToForumPost(scan);
        Assert.Contains("Changes over time", post);
        Assert.Contains("5 scans", post);
        Assert.Contains("watch", post);
        var none = scan with { Health = new HealthSummary(1, DateTimeOffset.Now, []) };
        Assert.False(ReportWriter.ToMarkdown(none).Contains("Health over time"));
    }
}
