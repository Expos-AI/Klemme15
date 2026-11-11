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
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Klemme15.Tests;

/// <summary>
/// Keeps three promises checkable instead of just stated: Klemme15 only talks to the hosts PRIVACY.md lists, the
/// repository ships no BMW software or data files, and reasoning written as data names its sources.
/// </summary>
public class ProvenanceTests
{
    private static string Root([CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    private static IEnumerable<string> Files(string folder, params string[] extensions) =>
        Directory.EnumerateFiles(Path.Combine(Root(), folder), "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains("node_modules"))
            .Where(f => extensions.Length == 0 || extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

    /// <summary>Every host the app's own code names. A new one must be added here and to PRIVACY.md on purpose.</summary>
    private static readonly string[] KnownHosts =
    [
        "www.gnu.org", "schemas.microsoft.com", "www.w3.org",           // license header and XML namespaces: never contacted
        "localhost",                                                      // the phone dashboard on your own computer
        "github.com", "api.github.com", "raw.githubusercontent.com",     // downloads you ask for, links
        "expos-ai.github.io",                                             // the update check's version file, links
        "translate.google.com", "duckduckgo.com", "paypal.me",           // opened in your browser when you click
        "www.bmwtechinfo.com", "aos.bmwgroup.com",                       // BMW's official repair information, as links
    ];

    [Fact]
    public void TheAppOnlyNamesHostsThePrivacyPageKnows()
    {
        var hosts = Files("src", ".cs", ".axaml")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"https?://([A-Za-z0-9.-]+)").Select(m => (File: f, Host: m.Groups[1].Value.TrimEnd('.').ToLowerInvariant())))
            .Where(x => x.Host.Contains('.') || x.Host == "localhost")
            .ToList();
        Assert.NotEmpty(hosts);
        foreach (var (file, host) in hosts)
            Assert.True(KnownHosts.Contains(host), $"{Path.GetFileName(file)} names {host}: add it to PRIVACY.md and to this list, or remove it");
        var privacy = File.ReadAllText(Path.Combine(Root(), "PRIVACY.md"));
        foreach (var contacted in new[] { "github.com", "api.github.com", "raw.githubusercontent.com", "expos-ai.github.io", "DuckDuckGo", "Google Translate" })
            Assert.Contains(contacted, privacy);
    }

    [Fact]
    public void NothingInTheAppReportsUsage()
    {
        // No analytics or crash-reporting library, and no code that posts anything anywhere.
        var projects = string.Join("\n", Files("src", ".csproj").Select(File.ReadAllText));
        foreach (var library in new[] { "ApplicationInsights", "Sentry", "AppCenter", "Segment", "Mixpanel", "Amplitude", "OpenTelemetry", "Bugsnag", "Raygun" })
            Assert.False(projects.Contains(library, StringComparison.OrdinalIgnoreCase), $"a project references {library}");
        foreach (var file in Files("src", ".cs").Where(f => !f.Contains("Klemme15.Dashboard")))
        {
            var code = File.ReadAllText(file);
            Assert.False(Regex.IsMatch(code, @"\.Post(AsJson)?Async\(|\.PutAsync\(|HttpMethod\.Post|HttpMethod\.Put"), $"{Path.GetFileName(file)} sends data to a server");
        }
    }

    [Fact]
    public void TheRepositoryShipsNoBmwSoftwareOrDataFiles()
    {
        // EDIABAS control-unit description files, ISTA and INPA databases, NCS coding data, flash files.
        string[] bmwExtensions = [".prg", ".grp", ".ipo", ".ips", ".ipp", ".ipk", ".sqlite", ".db", ".0pa", ".0da", ".daten", ".asw", ".c__", ".trc", ".istap", ".psdzdata"];
        foreach (var folder in new[] { "src", "docs", "data", "tools", "packaging", "tests" })
        {
            if (!Directory.Exists(Path.Combine(Root(), folder))) continue;
            foreach (var file in Files(folder))
                Assert.False(bmwExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()), $"{file} looks like a BMW data file");
        }
        // The app's own data is small, hand-written JSON and text. A dump of BMW's tables would be many times larger.
        foreach (var file in Files(Path.Combine("src", "Klemme15.Data", "db")))
            Assert.True(new FileInfo(file).Length < 600_000, $"{Path.GetFileName(file)} is {new FileInfo(file).Length / 1000} kB: too large to be hand-written data");
    }

    [Theory]
    [InlineData("chains.json", "chains")]
    [InlineData("checks.json", "checks")]
    public void ReasoningWrittenAsDataNamesItsSources(string file, string list)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "src", "Klemme15.Data", "db", file)));
        var entries = doc.RootElement.GetProperty(list).EnumerateArray().ToList();
        Assert.NotEmpty(entries);
        foreach (var e in entries)
        {
            var id = e.GetProperty("id").GetString();
            Assert.True(e.TryGetProperty("sources", out var sources) && sources.GetArrayLength() > 0, $"{file}: {id} has no source");
            foreach (var s in sources.EnumerateArray())
                Assert.True(s.GetString()!.StartsWith("https://", StringComparison.Ordinal), $"{file}: {id} has a source that isn't a link");
        }
    }
}
