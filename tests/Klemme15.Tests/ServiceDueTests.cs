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

public class ServiceDueTests
{
    private static Dictionary<string, string> Item(string kind, string key, int percent, long remaining, string unit, int resets, int month = 0, int year = 0) => new()
    {
        [$"ID_FN_{kind}_MESS_TEXT"] = key,
        [$"AVAI_{kind}_WERT"] = percent.ToString(),
        [$"RMMI_{kind}_WERT"] = remaining.ToString(),
        [$"ST_UN_{kind}_TEXT"] = unit,
        [$"COU_RSTG_{kind}_MESS_WERT"] = resets.ToString(),
        ["ZIEL_MM_WERT"] = month.ToString(),
        ["ZIEL_YY_WERT"] = year.ToString(),
    };

    private static readonly IReadOnlyList<IReadOnlyDictionary<string, string>> Sets =
    [
        new Dictionary<string, string> { ["JOB_STATUS"] = "OKAY", ["ANZ_CBS"] = "4" },
        Item("CBS", "Oel", 62, 14000, "km", 3),
        Item("CBS", "Brfl", 0, 0, "", 2, 3, 26),
        Item("CBS", "Br_v", 12, 4000, "km", 1),
        Item("CBS", "Oel", 99, 1, "km", 9),
        Item("CBS", "Xyz", 80, 0, "", 0),
    ];

    [Fact]
    public void TheJobOnlyReads()
    {
        Assert.True(JobPolicy.IsRead(ServiceDue.Job));
        Assert.Null(JobPolicy.Refusal("komb87", ServiceDue.Job));
        Assert.NotNull(JobPolicy.Refusal("komb87", "STEUERN_KM_STAND"));
    }

    [Fact]
    public void ItemsAreNamedAndTheMostUrgentComeFirst()
    {
        var items = ServiceDue.Parse(Sets);
        Assert.Equal(4, items.Count);
        Assert.Equal("Brake fluid", items[0].Name);
        Assert.Equal(ServiceDueLevel.Due, items[0].Level);
        Assert.Equal("March 2026", items[0].DueDate);
        Assert.Equal("Front brake pads", items[1].Name);
        Assert.Equal(ServiceDueLevel.Soon, items[1].Level);
        var oil = Assert.Single(items, i => i.Key == "Oel");
        Assert.Equal(62, oil.Percent);
        Assert.Equal(3, oil.Resets);
        Assert.Contains("62 % of the interval left", oil.Summary());
        Assert.Contains("reset 3 times", oil.Summary());
        Assert.Equal("Xyz", items[^1].Name);
        Assert.Equal("1 item is due now: Brake fluid.", ServiceDue.Headline(items));
    }

    [Fact]
    public void TheOlderLayoutIsReadToo()
    {
        var items = ServiceDue.Parse([Item("BOS", "Filt", 40, 9000, "km", 1)]);
        var filter = Assert.Single(items);
        Assert.Equal("Cabin microfilter", filter.Name);
        Assert.Equal(ServiceDueLevel.Ok, filter.Level);
        Assert.Equal("Nothing is due.", ServiceDue.Headline(items));
    }

    [Fact]
    public void MissingOrImpossibleFiguresAreNotGuessed()
    {
        var items = ServiceDue.Parse([new Dictionary<string, string> { ["ID_FN_CBS_MESS_TEXT"] = "Kfl", ["AVAI_CBS_WERT"] = "255", ["ZIEL_MM_WERT"] = "15" }]);
        var coolant = Assert.Single(items);
        Assert.Null(coolant.Percent);
        Assert.Equal(ServiceDueLevel.Unknown, coolant.Level);
        Assert.Equal("", coolant.DueDate);
        Assert.Equal("The cluster didn't report a figure for this item.", coolant.Summary());
        Assert.Equal("The cluster didn't report any service items.", ServiceDue.Headline([]));
    }

    private sealed class FakeJobs : IJobRunner
    {
        public List<(string Sgbd, string Job)> Calls { get; } = [];
        public Task<IReadOnlyList<string>> ListJobsAsync(string sgbd, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> RunJobAsync(string sgbd, string job, string arguments, CancellationToken ct = default)
        {
            Calls.Add((sgbd, job));
            return Task.FromResult(Sets);
        }
    }

    [Fact]
    public async Task ReadingRunsOnlyTheReadJob()
    {
        var jobs = new FakeJobs();
        var items = await ServiceDue.ReadAsync(jobs, "komb87");
        Assert.Equal(4, items.Count);
        var call = Assert.Single(jobs.Calls);
        Assert.Equal(("komb87", "CBS_DATEN_LESEN"), call);
    }
}
