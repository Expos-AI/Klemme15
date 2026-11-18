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

public class CrashReportTests
{
    [Fact]
    public void AnErrorIsWrittenDownAndOfferedOnce()
    {
        var folder = Path.Combine(Path.GetTempPath(), "k15-crash-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(CrashReport.Unseen(folder));
            Exception thrown;
            try { throw new InvalidOperationException("example failure"); }
            catch (InvalidOperationException e) { thrown = e; }
            var path = CrashReport.Write(thrown, folder, "0.99.44", "a background task");
            Assert.NotNull(path);
            var text = File.ReadAllText(path!);
            Assert.Contains("Klemme15 0.99.44 closed because of an error", text);
            Assert.Contains("example failure", text);
            Assert.Contains("Where: a background task", text);

            Assert.Equal(path, CrashReport.Unseen(folder));
            CrashReport.MarkSeen(path!);
            Assert.Null(CrashReport.Unseen(folder));
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [Fact]
    public void WritingNeverThrows()
    {
        // A folder that can't be created (a file is in the way) must not turn one crash into another.
        var file = Path.GetTempFileName();
        try { Assert.Null(CrashReport.Write(new Exception("x"), Path.Combine(file, "sub"), "1")); }
        finally { File.Delete(file); }
    }
}
