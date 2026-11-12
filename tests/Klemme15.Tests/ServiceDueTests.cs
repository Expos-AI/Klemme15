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
    private static readonly Dictionary<string, int> Numbers = ServiceDue.KeysByNumber.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>One item as a module reports it. <paramref name="km"/> is what's left; the module stores it in steps of 10 km.</summary>
    private static Dictionary<string, string> Item(string kind, string key, int percent, long? km, int resets, int month = 0, int year = 0)
    {
        var set = new Dictionary<string, string>
        {
            [$"ID_FN_{kind}_MESS_TEXT"] = key,
            [$"AVAI_{kind}_WERT"] = percent.ToString(),
            [$"RMMI_{kind}_WERT"] = km is { } k ? (k / 10).ToString() : "-32768",
            [$"ST_UN_{kind}_WERT"] = "1",
            [$"ST_UN_{kind}_TEXT"] = "Weg",
            [$"COU_RSTG_{kind}_MESS_WERT"] = resets.ToString(),
            ["ZIEL_MM_WERT"] = month.ToString(),
            ["ZIEL_YY_WERT"] = year.ToString(),
        };
        if (Numbers.TryGetValue(key, out var number)) set[$"ID_FN_{kind}_MESS_WERT"] = number.ToString();
        return set;
    }

    private static readonly IReadOnlyList<IReadOnlyDictionary<string, string>> Sets =
    [
        new Dictionary<string, string> { ["JOB_STATUS"] = "OKAY", ["ANZ_CBS"] = "4" },
        Item("CBS", "Oel", 62, 14000, 3),
        Item("CBS", "Brfl", 0, null, 2, 3, 26),
        Item("CBS", "Br_v", 12, 4000, 1),
        Item("CBS", "Oel", 99, 10, 9),
        Item("CBS", "Xyz", 80, null, 0),
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
        Assert.Equal(14000, oil.Remaining);
        Assert.Contains("to go", oil.Summary());
        Assert.Contains("reset 3 times", oil.Summary());
        Assert.Equal("Xyz", items[^1].Name);
        Assert.Equal("1 item is due now: Brake fluid.", ServiceDue.Headline(items));
    }

    [Fact]
    public void TheOlderLayoutIsReadToo()
    {
        var items = ServiceDue.Parse([Item("BOS", "Filt", 40, 9000, 1)]);
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

    /// <summary>
    /// What a 2006 E90 325i's cluster (KOMB87) answered on 11 November 2026, right after its brake fluid, spark plugs
    /// and vehicle check were reset. It names items by number and long German text, stores the distance in steps of
    /// 10 km, uses -32768 for "no distance", and carries Germany's two statutory inspections.
    /// </summary>
    [Fact]
    public void ARealClustersAnswerIsReadCorrectly()
    {
        static Dictionary<string, string> Real(int id, string text, int rmmi, int resets, int percent, int month, int year) => new()
        {
            ["ID_FN_CBS_MESS_WERT"] = $"{id}", ["ID_FN_CBS_MESS_HEX"] = $"0x{id:X2}", ["ID_FN_CBS_MESS_TEXT"] = text, ["RMMI_CBS_WERT"] = $"{rmmi}",
            ["ST_UN_CBS_WERT"] = "1", ["ST_UN_CBS_TEXT"] = "Weg", ["COU_RSTG_CBS_MESS_WERT"] = $"{resets}", ["AVAI_CBS_WERT"] = $"{percent}",
            ["ZIEL_MM_WERT"] = $"{month}", ["ZIEL_YY_WERT"] = $"{year}", ["MANIP_CBS_TEXT"] = "Keine Manipulation",
        };
        var items = ServiceDue.Parse(
        [
            new Dictionary<string, string> { ["JOB_STATUS"] = "OKAY", ["ANZ_CBS"] = "5", ["AVAI_CBS_WERT_BRFL"] = "100", ["AVAI_CBS_WERT_SIC"] = "100", ["AVAI_CBS_WERT_ZKRZ"] = "100" },
            Real(3, "Bremsfluessigkeit", -32768, 9, 100, 11, 28),
            Real(17, "Sichtpruefung/Fahrzeug-Check", 4800, 7, 100, 11, 30),
            Real(32, "§Fahrzeuguntersuchung", -32768, 1, 69, 11, 30),
            Real(33, "§Abgasuntersuchung", -32768, 2, 87, 9, 40),
            Real(16, "Zuendkerzen", 16100, 4, 100, 15, 63),
        ]);
        Assert.Equal(["Brake fluid", "Spark plugs", "Vehicle check"], items.Select(i => i.Name).Order().ToArray());

        var fluid = items.Single(i => i.Key == "Brfl");
        Assert.Null(fluid.Remaining);
        Assert.Equal("November 2028", fluid.DueDate);
        Assert.Equal(9, fluid.Resets);
        Assert.DoesNotContain("overdue", fluid.Summary());

        var check = items.Single(i => i.Key == "Sic");
        Assert.Equal(48_000, check.Remaining);
        Assert.Equal("November 2030", check.DueDate);

        var plugs = items.Single(i => i.Key == "ZKrz");
        Assert.Equal(161_000, plugs.Remaining);
        Assert.Equal("", plugs.DueDate);
        Assert.Equal("Nothing is due.", ServiceDue.Headline(items));
    }

    [Fact]
    public async Task TheWholeCarsListComesFromTheModulesThatKeepIt()
    {
        var kombi = new EcuDefinition { Id = "E9x.KOMBI", Name = "Instrument cluster (KOMBI)", Address = 0x60, Protocol = BusProtocol.BmwFast };
        var dme = new EcuDefinition { Id = "E9x.DME", Name = "Engine (DME)", Address = 0x12, Protocol = BusProtocol.BmwFast, IsEngine = true };
        var dsc = new EcuDefinition { Id = "E9x.DSC", Name = "Stability control (DSC)", Address = 0x29, Protocol = BusProtocol.BmwFast };
        var frm = new EcuDefinition { Id = "E9x.FRM", Name = "Footwell module (FRM)", Address = 0x72, Protocol = BusProtocol.BmwFast };
        Assert.True(ServiceDue.KeepsServiceItems(kombi) && ServiceDue.KeepsServiceItems(dme) && ServiceDue.KeepsServiceItems(dsc));
        Assert.False(ServiceDue.KeepsServiceItems(frm));

        var jobs = new PerModule();
        var items = await ServiceDue.ReadAllAsync(jobs, [(kombi, "komb87"), (dme, "msv70"), (dsc, "dsc_87")]);
        // The stability control didn't answer here: its items are simply missing, the rest still shows.
        Assert.Equal(["Engine oil", "Brake fluid"], items.Select(i => i.Name).ToArray());
        Assert.Equal(3, jobs.Calls.Count);
        Assert.All(jobs.Calls, c => Assert.Equal("CBS_DATEN_LESEN", c.Job));
    }

    private sealed class PerModule : IJobRunner
    {
        public List<(string Sgbd, string Job)> Calls { get; } = [];
        public Task<IReadOnlyList<string>> ListJobsAsync(string sgbd, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> RunJobAsync(string sgbd, string job, string arguments, CancellationToken ct = default)
        {
            Calls.Add((sgbd, job));
            return sgbd switch
            {
                "komb87" => Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, string>>>([Item("CBS", "Brfl", 70, null, 2, 3, 28)]),
                "msv70" => Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, string>>>([Item("CBS", "Oel", 30, 8000, 5)]),
                _ => Task.FromException<IReadOnlyList<IReadOnlyDictionary<string, string>>>(new DiagnosticException("no answer")),
            };
        }
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
