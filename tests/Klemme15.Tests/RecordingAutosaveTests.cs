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

public class RecordingAutosaveTests
{
    [Fact]
    public void ARecordingSurvivesWithoutBeingSavedAndIsOfferedBack()
    {
        var folder = Directory.CreateTempSubdirectory("klemme15-rec").FullName;
        var rec = new RecordingAutosave(folder, new DateTime(2026, 11, 12, 14, 5, 9));
        rec.AppendLine("time,\"Engine speed (rpm)\"");
        for (var i = 0; i < 25; i++) rec.AppendLine($"14:05:{i:00}.000,{700 + i}");
        // The app stops without saving: nothing is disposed. Every row up to the last flush is on disk.
        var onDisk = File.ReadAllLines(CopyOf(rec.Path));
        Assert.True(onDisk.Length >= 20, $"{onDisk.Length} lines on disk");
        Assert.Equal("time,\"Engine speed (rpm)\"", onDisk[0]);

        rec.Dispose();
        var left = Assert.Single(RecordingAutosave.Leftovers(folder));
        Assert.Equal(rec.Path, left);
        Assert.Equal("12 November 2026, 14:05", RecordingAutosave.When(left));
        Assert.Equal(26, File.ReadAllLines(left).Length);
    }

    [Fact]
    public void ASavedOrEmptyRecordingLeavesNothingBehind()
    {
        var folder = Directory.CreateTempSubdirectory("klemme15-rec").FullName;
        var saved = new RecordingAutosave(folder, new DateTime(2026, 1, 1, 8, 0, 0));
        saved.AppendLine("time,a"); saved.AppendLine("1,2"); saved.AppendLine("2,3");
        saved.Discard();
        Assert.False(File.Exists(saved.Path));

        var headerOnly = new RecordingAutosave(folder, new DateTime(2026, 1, 1, 9, 0, 0));
        headerOnly.AppendLine("time,a");
        headerOnly.Dispose();
        Assert.Empty(RecordingAutosave.Leftovers(folder));
        Assert.False(File.Exists(headerOnly.Path));
        saved.AppendLine("after the end");   // ignored, no exception
        Assert.Empty(RecordingAutosave.Leftovers(Path.Combine(folder, "missing")));
    }

    private static string CopyOf(string path)
    {
        var copy = path + ".copy";
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var target = File.Create(copy);
        source.CopyTo(target);
        return copy;
    }
}
