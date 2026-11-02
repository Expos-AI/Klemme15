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

using System.Globalization;
using System.Text;
using Klemme15.Core;
using Klemme15.Data;
using System.Runtime.CompilerServices;
using Xunit;

namespace Klemme15.Tests;

public class BaselineTests
{
    /// <summary>A warm engine at idle (760 rpm) and at 2,500 rpm, with total trim (short + long) as given.</summary>
    private static DataLog Log(double idleTrim, double cruiseTrim, double coolant = 90, string rpmHeader = "Engine speed (rpm)")
    {
        var sb = new StringBuilder($"time,\"{rpmHeader}\",\"Coolant temperature (°C)\",\"Short-term trim bank 1 (%)\",\"Long-term trim bank 1 (%)\",\"Air mass (g/s)\"\n");
        var rng = new Random(4);
        for (var i = 0; i < 200; i++)
        {
            var cruise = i >= 100;
            var rpm = cruise ? 2500 + rng.Next(-60, 60) : 760 + rng.Next(-15, 15);
            var trim = (cruise ? cruiseTrim : idleTrim) + rng.NextDouble() * 2 - 1;
            var maf = cruise ? 11.5 : 3.6;
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{i * 0.2:0.0},{rpm},{coolant},{trim / 2:0.00},{trim / 2:0.00},{maf}"));
        }
        return DataLog.Parse(sb.ToString());
    }

    private static BaselineCurve TrimCurve() => BaselineAligner.Build([Log(0, 0)], "rpm", "stft1+ltft1", "coolant > 70", 250, minSamples: 5);

    [Fact]
    public void Build_makes_a_band_per_operating_point()
    {
        var curve = TrimCurve();
        Assert.Equal(2, curve.Points.Count);
        Assert.Equal(750, curve.Points[0].X);
        Assert.Equal(2500, curve.Points[1].X);
        Assert.All(curve.Points, p => Assert.True(p.P10 <= p.Median && p.Median <= p.P90));
        Assert.InRange(curve.Points[0].Median, -0.5, 0.5);
    }

    [Fact]
    public void A_healthy_log_sits_inside_and_a_lean_idle_is_called_out()
    {
        var curve = TrimCurve();
        var healthy = BaselineAligner.Compare(Log(0, 0), curve)!;
        Assert.True(healthy.ShareInside > 0.7, $"healthy share {healthy.ShareInside}");
        Assert.Contains("follows the healthy curve", healthy.Summary);

        // Vacuum-leak shape: lean (positive trim) at idle, fine at 2,500 rpm.
        var leak = BaselineAligner.Compare(Log(14, 1), curve)!;
        Assert.True(leak.ShareInside < 0.6, $"leak share {leak.ShareInside}");
        Assert.Contains("above the healthy range around 750 rpm", leak.Summary);
    }

    [Fact]
    public void Alignment_ignores_time_and_column_names()
    {
        var curve = TrimCurve();
        var renamed = BaselineAligner.Compare(Log(0, 0, rpmHeader: "RPM"), curve);
        Assert.NotNull(renamed);
        Assert.True(renamed!.Samples > 150);
    }

    [Fact]
    public void A_cold_engine_is_left_out_by_the_filter()
    {
        var cold = BaselineAligner.Compare(Log(14, 14, coolant: 40), TrimCurve())!;
        Assert.Equal(0, cold.Samples);
        Assert.Contains("only counts readings where coolant > 70", cold.Summary);
    }

    [Fact]
    public void Profiles_round_trip_and_bad_ones_are_refused()
    {
        var profile = BaselineStore.FromLog(Log(0, 0), "mine-n52", "My N52", "N52", "test")!;
        Assert.True(profile.Curves.Count >= 2);
        var back = BaselineProfile.Parse(profile.ToJson())!;
        Assert.Equal("mine-n52", back.Id);
        Assert.Equal(profile.Curves.Count, back.Curves.Count);
        Assert.True(back.Fits("N52B30"));
        Assert.False(back.Fits("N54B30"));

        Assert.Null(BaselineProfile.Parse("{ not json"));
        var inverted = profile.ToJson().Replace("\"P10\"", "\"Tmp\"").Replace("\"P90\"", "\"P10\"").Replace("\"Tmp\"", "\"P90\"");
        Assert.Null(BaselineProfile.Parse(inverted));
    }

    [Fact]
    public void Store_saves_with_a_safe_file_name()
    {
        var folder = Path.Combine(Path.GetTempPath(), "k15-baselines-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BaselineStore(folder);
            var profile = BaselineStore.FromLog(Log(0, 0), "../../evil name", "x", null, "")!;
            var path = store.Save(profile);
            Assert.Equal(folder, Path.GetDirectoryName(path));
            Assert.Single(store.Load());
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void The_shipped_baselines_validate_and_are_labelled()
    {
        var db = VehicleDatabase.Load();
        Assert.NotEmpty(db.Baselines);
        Assert.Empty(db.LoadWarnings.Where(w => w.StartsWith("baselines", StringComparison.Ordinal)));
        // Until real healthy cars contribute, everything shipped is a labelled example.
        Assert.All(db.Baselines, p => Assert.True(p.IsExample || p.IsModel || p.Cars >= 1));
        Assert.All(db.Baselines.Where(p => p.IsModel), p => Assert.StartsWith("Physics model", p.Trust));
        Assert.All(db.Baselines.Where(p => p.IsModel), p => Assert.Contains("not measured", p.Title));
        Assert.All(db.Baselines.Where(p => p.IsExample), p => Assert.StartsWith("Example", p.Trust));
    }

    private static string Root([CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    [Fact]
    public void The_N52_model_reads_logs_from_other_tools_and_splits_by_load()
    {
        // The reference log uses another tool's names and kg/h: "STFT_Bank1(%)", "MAF(kg/h)", "CoolantTemp(C)".
        var log = DataLog.Parse(File.ReadAllText(Path.Combine(Root(), "tools", "baseline-model", "n52_model_log.csv")));
        var model = VehicleDatabase.Load().Baselines.Single(p => p.Id == "n52-model");
        Assert.True(model.Fits("N52B30"));
        Assert.False(model.Fits("N54B30"));
        foreach (var curve in model.Curves)
        {
            var c = BaselineAligner.Compare(log, curve);
            Assert.NotNull(c);
            Assert.True(c!.Samples > 0, curve.Title);
            Assert.True(c.ShareInside > 0.95, $"{curve.Title}: {c.ShareInside:P0}");
        }
        // kg/h was turned into g/s: idle air is about 4 g/s, not 15.
        var idle = BaselineAligner.Compare(log, model.Curves.Single(c => c.Name == "Air mass at idle and no load"))!;
        Assert.InRange(idle.Yours[0].Median, 3.5, 5.5);
        // Full throttle only counts the pull, not the no-load hold at the same rpm.
        var wot = BaselineAligner.Compare(log, model.Curves.Single(c => c.Name == "Air mass at full throttle"))!;
        Assert.True(wot.Yours.All(b => b.Median > 40), "no-load readings leaked into the full-throttle curve");
    }

    [Fact]
    public void A_curve_that_needs_load_is_skipped_for_a_log_without_it()
    {
        var model = VehicleDatabase.Load().Baselines.Single(p => p.Id == "n52-model");
        var noLoad = Log(0, 0);   // rpm, coolant, trims, air mass; no load
        Assert.All(model.Curves, c => Assert.False(BaselineAligner.Applies(noLoad, c)));
        Assert.Null(BaselineAligner.Compare(noLoad, model.Curves[0]));
    }
}
