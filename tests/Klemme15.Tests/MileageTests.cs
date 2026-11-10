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
using Klemme15.Data;
using Xunit;

namespace Klemme15.Tests;

public class MileageTests
{
    private static readonly EcuDefinition Dme = new() { Id = "E9x.DME", Name = "Engine (DME)", Address = 0x12, Protocol = BusProtocol.BmwFast, IsEngine = true, Sgbd = ["D_0012"] };
    private static readonly EcuDefinition Kombi = new() { Id = "E9x.KOMBI", Name = "Instrument cluster (KOMBI)", Address = 0x60, Protocol = BusProtocol.BmwFast, Sgbd = ["D_0060"] };
    private static readonly EcuDefinition Cas = new() { Id = "E9x.CAS", Name = "Key module (CAS)", Address = 0x40, Protocol = BusProtocol.BmwFast, Sgbd = ["D_0040"] };
    private static readonly EcuDefinition Ihka = new() { Id = "E9x.IHKA", Name = "Climate control (IHKA)", Address = 0x78, Protocol = BusProtocol.BmwFast, Sgbd = ["D_0078"] };

    private static MileageRecord Engine(long km) => new("Engine (DME)", "Engine computer's own mileage count", MileageKind.Current, km);
    private static MileageRecord Cluster(long km) => new("Instrument cluster (KOMBI)", "Cluster's stored mileage", MileageKind.Current, km);
    private static MileageRecord Programmed(string module, long km) => new(module, "Mileage when last programmed", MileageKind.Programmed, km);

    [Fact]
    public void EveryMileageJobOnlyReads()
    {
        Assert.All(MileageJobs.All, j =>
        {
            Assert.True(MileageJobs.IsReadOnly(j.Name), j.Name);
            Assert.True(JobPolicy.IsRead(j.Name), j.Name);
            Assert.Null(JobPolicy.Refusal("cas", j.Name));
            Assert.Null(JobPolicy.Refusal("msv70", j.Name));
        });
    }

    [Theory]
    [InlineData("05gk35", "KM_STAND_PROGRAMMIERUNG")]
    [InlineData("05gk35", "KILOMETERSTAND_FS_LOESCHEN")]
    [InlineData("komb87", "STEUERN_KM_STAND")]
    [InlineData("komb87", "KILOMETERSTAND_SCHREIBEN")]
    [InlineData("msv70", "FLASH_SCHREIBEN")]
    [InlineData("msv70", "FLASH_PROGRAMMIER_STATUS_LESEN")]
    [InlineData("msv70", "STATUS_ISN")]
    [InlineData("cas", "ISN_SCHREIBEN")]
    [InlineData("cas", "SCHLUESSEL_ANLERNEN")]
    [InlineData("ews3", "STATUS_EWS_DATEN")]
    [InlineData("cas", "CODIERUNG_SCHREIBEN")]
    [InlineData("D_0040", "STEUERN_ZV")]
    public void ForbiddenJobsAreRefusedWhoeverAsks(string sgbd, string job)
    {
        Assert.NotNull(JobPolicy.Refusal(sgbd, job));
        Assert.False(MileageJobs.IsReadOnly(job) && JobPolicy.Refusal(sgbd, job) is null);
    }

    [Theory]
    [InlineData("cas", "STEUERN_ELVCOUNTER_CAS_LOESCHEN")]
    [InlineData("cas", "STEUERN_ELVCOUNTER_ELV_LOESCHEN")]
    [InlineData("cas", "STATUS_ELV_WERTE")]
    [InlineData("cas", "IDENT")]
    [InlineData("cas", "FS_LOESCHEN")]
    [InlineData("cas", "IS_LOESCHEN")]
    [InlineData("komb87", "STATUS_BETRIEBSDATEN_HEADER")]
    [InlineData("msv70", "STEUERN_BATTERIETAUSCH_REGISTRIEREN")]
    [InlineData("d62m57a0", "ABGLEICH_PROGRAMMIEREN_IMA_ZYL")]
    public void NormalJobsStillRun(string sgbd, string job) => Assert.Null(JobPolicy.Refusal(sgbd, job));

    [Fact]
    public void NoServiceFunctionIsBlockedByTheRules()
    {
        foreach (var f in ServiceLibrary.Load().All)
            foreach (var i in f.Implementations)
                foreach (var step in i.Steps.Concat(i.Stop))
                    foreach (var v in i.Variants)
                        Assert.True(JobPolicy.Refusal(v, step.Job) is null, $"{f.Id}: {v} {step.Job}");
    }

    [Fact]
    public void AWoundBackClusterIsCaughtByTheEngineComputer()
    {
        var (items, _) = MileageAnalysis.Assess([Engine(212_400), Cluster(150_020), Programmed("Climate control (IHKA)", 98_000)], odometerKm: 150_000);
        Assert.Equal(CheckLevel.Warn, items[0].Level);
        Assert.Contains(items, i => i.Title == "Mileage doesn't add up" && i.Detail.Contains("strongest sign"));
        Assert.Contains(items, i => i.Title == "The engine computer has counted more than the cluster");
    }

    [Fact]
    public void AReplacedEngineComputerIsExplainedNotAlarmed()
    {
        var (items, _) = MileageAnalysis.Assess([Engine(40_000), Cluster(160_000)], odometerKm: 160_100);
        Assert.DoesNotContain(items, i => i.Level == CheckLevel.Warn);
        Assert.Contains(items, i => i.Title == "The engine computer has counted less than the cluster" && i.Level == CheckLevel.Info);
    }

    [Fact]
    public void ConsistentRecordsSayHowStrongTheCheckIs()
    {
        var (items, notChecked) = MileageAnalysis.Assess([Engine(160_050), Cluster(160_020), Programmed("Footwell module (FRM)", 120_000)], odometerKm: 160_100, miles: true);
        var good = Assert.Single(items);
        Assert.Equal(CheckLevel.Good, good.Level);
        Assert.Contains("2 running counts", good.Detail);
        Assert.Contains(" mi (", good.Detail);
        Assert.Contains("not proof", good.Detail);
        Assert.Empty(notChecked);
    }

    [Fact]
    public void ReferenceFiguresAreShownButNeverDecide()
    {
        var reference = new MileageRecord("Key module (CAS)", "Key module's vehicle state", MileageKind.Reference, 900_000);
        var (items, _) = MileageAnalysis.Assess([Engine(100_000), reference], odometerKm: 100_200);
        Assert.DoesNotContain(items, i => i.Level == CheckLevel.Warn);
    }

    [Fact]
    public void WithoutRunningCountsTheCheckSaysWhatsMissing()
    {
        var (_, notChecked) = MileageAnalysis.Assess([Programmed("Climate control (IHKA)", 50_000)], odometerKm: 90_000);
        Assert.Contains(notChecked, n => n.Contains("Read the mileage from every module"));
    }

    [Theory]
    [InlineData("182400", 182400L)]
    [InlineData("182400.000000", 182400L)]
    [InlineData("182.400 km", 182400L)]
    [InlineData("0", 0L)]
    [InlineData("16777215", 0L)]
    [InlineData("", 0L)]
    public void FiguresAreParsedAndImpossibleOnesIgnored(string text, long km) => Assert.Equal(km, MileageReader.Parse(text) ?? 0);

    /// <summary>Records every job asked for, and answers the mileage jobs with fixed figures.</summary>
    private sealed class FakeJobs : IJobRunner
    {
        public List<(string Sgbd, string Job, string Args)> Calls { get; } = [];
        public Task<IReadOnlyList<string>> ListJobsAsync(string sgbd, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> RunJobAsync(string sgbd, string job, string arguments, CancellationToken ct = default)
        {
            Calls.Add((sgbd, job, arguments));
            IReadOnlyDictionary<string, string>? set = (sgbd, job) switch
            {
                ("D_0012", "STATUS_SYSTEMCHECK_PM_INFO_1") => new Dictionary<string, string> { ["STAT_KM_STAND_AKTUELL_WERT"] = "212400" },
                ("D_0060", "STATUS_BETRIEBSDATEN_HEADER") => new Dictionary<string, string> { ["STAT_AKT_KM_STAND_WERT"] = "150020" },
                (_, "AIF_LESEN") => new Dictionary<string, string> { ["AIF_KM"] = "98000" },
                _ => null,
            };
            return set is null ? Task.FromException<IReadOnlyList<IReadOnlyDictionary<string, string>>>(new DiagnosticException("no such job"))
                               : Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, string>>>([set]);
        }
    }

    [Fact]
    public async Task TheReaderAsksOnlyForTheListedReadJobs()
    {
        var jobs = new FakeJobs();
        var records = await MileageReader.ReadAsync(jobs, null, [Dme, Kombi, Cas, Ihka]);
        Assert.All(jobs.Calls, c => Assert.Contains(MileageJobs.All, j => j.Name == c.Job));
        Assert.All(jobs.Calls, c => Assert.Null(JobPolicy.Refusal(c.Sgbd, c.Job)));
        Assert.Contains(records, r => r.Kind == MileageKind.Current && r.Km == 212_400 && r.Module == Dme.Name);
        Assert.Contains(records, r => r.Kind == MileageKind.Current && r.Km == 150_020 && r.Module == Kombi.Name);
        Assert.Equal(4, records.Count(r => r.Kind == MileageKind.Programmed));
        // The full buyer's check then flags it.
        var scan = new VehicleScanResult { Started = DateTimeOffset.Now, Backend = "test", Ecus = [] };
        var report = PurchaseCheck.Assess(scan, null, 150_000, records);
        Assert.Equal("Mileage doesn't add up", report.Items[0].Title);
        Assert.Equal(records.Count, report.Mileage!.Count);
    }
}
