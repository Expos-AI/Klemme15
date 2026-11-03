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

public class OperationJournalTests
{
    private static RunningOperation Op(OperationKind kind, string? stop = null) => new()
    {
        Kind = kind, Title = "Valvetronic learning", ModuleId = "E9x.DME", ModuleName = "engine computer (DME)",
        Car = "E9x 325i", Started = new DateTimeOffset(2026, 11, 3, 18, 5, 0, TimeSpan.Zero), StopAction = stop,
        BackupTaken = kind == OperationKind.CodingWrite ? new DateTimeOffset(2026, 11, 3, 18, 4, 0, TimeSpan.Zero) : null,
    };

    [Fact]
    public void A_job_cut_off_survives_a_restart_and_a_clean_end_leaves_nothing()
    {
        var path = Path.Combine(Path.GetTempPath(), "k15-journal-" + Guid.NewGuid().ToString("N"), "running-job.json");
        try
        {
            var journal = new OperationJournal(path);
            Assert.Null(journal.Pending());
            journal.Begin(Op(OperationKind.PartTest, "pump"));
            var again = new OperationJournal(path).Pending();      // as if Klemme15 restarted
            Assert.NotNull(again);
            Assert.Equal(OperationKind.PartTest, again!.Kind);
            Assert.Equal("pump", again.StopAction);
            journal.End();
            Assert.Null(journal.Pending());
            Assert.False(File.Exists(path));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void A_new_job_never_overwrites_the_advice_for_one_that_was_cut_off()
    {
        var dir = Directory.CreateTempSubdirectory("k15-journal").FullName;
        try
        {
            var journal = new OperationJournal(Path.Combine(dir, "running-job.json"));
            journal.Begin(Op(OperationKind.CodingWrite));
            Assert.Equal(OperationKind.CodingWrite, journal.CutOff()!.Kind);
            journal.Begin(Op(OperationKind.ServiceFunction));     // the owner carries on with something else
            journal.End();
            Assert.Equal(OperationKind.CodingWrite, journal.Pending()!.Kind);   // still there after a restart
            journal.Dismiss();
            Assert.Null(journal.Pending());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_damaged_journal_is_ignored()
    {
        var dir = Directory.CreateTempSubdirectory("k15-journal").FullName;
        var path = Path.Combine(dir, "running-job.json");
        File.WriteAllText(path, "{ not json");
        Assert.Null(new OperationJournal(path).Pending());
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Advice_says_what_to_do_for_each_kind_of_job()
    {
        var service = OperationJournal.Advice(Op(OperationKind.ServiceFunction));
        Assert.Contains("run a quick test", service);
        Assert.Contains("start 'Valvetronic learning' again", service);
        Assert.Contains("ignition off", service);
        Assert.StartsWith("'Valvetronic learning' was interrupted", OperationJournal.Headline(Op(OperationKind.ServiceFunction)));

        Assert.Contains("switch-off for the part", OperationJournal.Advice(Op(OperationKind.PartTest, "pump")));

        var coding = OperationJournal.Advice(Op(OperationKind.CodingWrite));
        Assert.Contains("restore the backup", coding);
        Assert.Contains("partly written", coding);
        Assert.All(new[] { service, coding }, a => Assert.True(a.Split(' ').Length < 45, "the banner stays short"));
    }

    [Fact]
    public void Only_a_lost_connection_counts_as_cut_off()
    {
        Assert.True(ServiceOutcome.IsCutOff(new TimeoutException()));
        Assert.True(ServiceOutcome.IsCutOff(new DiagnosticException("Cable error: gone", new IOException("gone"))));
        Assert.False(ServiceOutcome.IsCutOff(new DiagnosticException("Conditions not correct")));
    }
}
