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

using System.Runtime.CompilerServices;
using Klemme15.Data;
using Xunit;

namespace Klemme15.Tests;

public class UpdateCheckTests
{
    private static string Root([CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    [Fact]
    public void ANewerReleaseIsOfferedWithTheFileForThisComputer()
    {
        var update = UpdateCheck.FromJson("{\"tag\": \"v0.99.24\"}", new Version(0, 99, 23, 0));
        Assert.NotNull(update);
        Assert.Equal("v0.99.24", update!.Tag);
        Assert.Equal($"https://github.com/Expos-AI/Klemme15/releases/download/v0.99.24/Klemme15-v0.99.24-{UpdateCheck.Rid}.zip", update.DownloadUrl);
        Assert.Equal("https://github.com/Expos-AI/Klemme15/releases/tag/v0.99.24", update.ReleaseUrl);
    }

    [Theory]
    [InlineData("{\"tag\": \"v0.99.23\"}")]
    [InlineData("{\"tag\": \"v0.99.9\"}")]
    [InlineData("{\"tag\": \"latest\"}")]
    [InlineData("{}")]
    [InlineData("not json")]
    public void TheSameOlderOrAGarbledVersionIsNotOffered(string json) => Assert.Null(UpdateCheck.FromJson(json, new Version(0, 99, 23, 0)));

    [Fact]
    public void VersionsCompareAsNumbers()
    {
        Assert.NotNull(UpdateCheck.FromJson("{\"tag\": \"v1.0.0\"}", new Version(0, 99, 23)));
        Assert.NotNull(UpdateCheck.FromJson("{\"tag\": \"v0.100.0\"}", new Version(0, 99, 23)));
        Assert.Equal(new Version(1, 2, 3), UpdateCheck.ParseTag("v1.2.3-beta"));
        Assert.Null(UpdateCheck.ParseTag("v1.2"));
    }

    [Fact]
    public void TheCheckOnlyReadsTheWebsitesVersionFile()
    {
        Assert.Equal("https://expos-ai.github.io/Klemme15/latest.json", UpdateCheck.LatestUrl);
        Assert.Contains("\"tag\"", File.ReadAllText(Path.Combine(Root(), "docs", "latest.json")));
    }
}
