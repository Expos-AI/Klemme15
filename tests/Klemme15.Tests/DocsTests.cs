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
/// Keeps the README, roadmap and release notes honest: every release must have notes, every number the README quotes
/// must match the data, and every link between the docs must point at a file that exists. CI fails otherwise.
/// </summary>
public class DocsTests
{
    private static string Root([CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
    private static string Read(string relative) => File.ReadAllText(Path.Combine(Root(), relative));
    private static JsonElement Json(string db) => JsonDocument.Parse(Read(Path.Combine("src", "Klemme15.Data", "db", db))).RootElement;

    private static string Version => Regex.Match(Read("Directory.Build.props"), @"<Version>([^<]+)</Version>").Groups[1].Value;

    [Fact]
    public void The_safety_and_legal_notice_has_the_short_version_the_app_asks_people_to_read()
    {
        // The app builds its "Before you start" screen from the bullets under "## In short" in docs/LEGAL.md.
        var legal = Read(Path.Combine("docs", "LEGAL.md")).Replace("\r\n", "\n");   // Windows checkouts use CRLF
        var summary = Regex.Match(legal, @"## In short\n\n((?:- .+\n)+)").Groups[1].Value;
        Assert.InRange(summary.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length, 5, 10);
        foreach (var must in new[] { "no warranty", "own risk", "experimental", "never", "BMW", "affiliated" })
            Assert.Contains(must.ToLowerInvariant(), summary.ToLowerInvariant());
        Assert.True(Regex.IsMatch(summary, @"\bAI\b"), "the short version says AI tools help make Klemme15");
        Assert.Contains("docs/LEGAL.md", Read("README.md"));
        Assert.Contains("docs/LEGAL.md", Read(Path.Combine("docs", "index.html")));
        Assert.Contains("LEGAL.md", Read(Path.Combine("src", "Klemme15.App", "Klemme15.App.csproj")));
    }

    [Fact]
    public void ReleaseNotesStartWithThisVersion()
    {
        var notes = Read(Path.Combine(".github", "release-notes.md"));
        var first = Regex.Match(notes, @"\*\*New in ([0-9.]+):\*\*");
        Assert.True(first.Success, "release-notes.md needs a '**New in x.y.z:**' section");
        Assert.Equal(Version, first.Groups[1].Value);
    }

    [Fact]
    public void ReadmeNumbersMatchTheData()
    {
        var readme = Read("README.md");
        var roadmap = Read(Path.Combine("docs", "ROADMAP.md"));
        var service = Json("service.json").GetProperty("functions").GetArrayLength();
        var cc = Json("check-control.json").GetProperty("messages").GetArrayLength();
        var obd = Json("obd-generic.json").GetProperty("codes").EnumerateObject().Count();
        var bmw = Json("bmw-codes.json").GetProperty("modules").EnumerateObject().Sum(m => m.Value.EnumerateObject().Count());
        var modules = Json("modules.json").GetProperty("modules").EnumerateArray().Select(m => m.GetProperty("id").GetString()!.Split('.')[0]).ToList();

        void Says(string doc, string text, string name) => Assert.True(doc.Contains(text), $"{name} should say \"{text}\" (the data changed: update the doc)");
        Says(readme, $"{service} functions", "README");
        Says(roadmap, $"{service} service functions", "ROADMAP");
        Says(readme, $"{cc} messages", "README");
        Says(readme, $"{bmw} BMW codes", "README");
        Says(readme, obd.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " standard OBD-II codes", "README");
        Says(readme, $"{modules.Count(m => m == "E9x")} E90-family modules", "README");
        Says(readme, $"{modules.Count(m => m == "E46")} E46 modules", "README");
    }

    [Fact]
    public void EveryLinkBetweenTheDocsWorks()
    {
        var docs = new[] { "README.md", "CONTRIBUTING.md", "PRIVACY.md", "SECURITY.md" }
            .Concat(Directory.GetFiles(Path.Combine(Root(), "docs"), "*.md").Select(f => Path.GetRelativePath(Root(), f)));
        var broken = new List<string>();
        foreach (var doc in docs.Where(d => File.Exists(Path.Combine(Root(), d))))
        {
            var dir = Path.GetDirectoryName(Path.Combine(Root(), doc))!;
            foreach (Match m in Regex.Matches(Read(doc), @"\]\(([^)\s]+)\)|srcset=""([^""]+)""|src=""([^""]+)"""))
            {
                var target = new[] { m.Groups[1], m.Groups[2], m.Groups[3] }.First(g => g.Success).Value;
                if (target.StartsWith("http") || target.StartsWith('#') || target.StartsWith("mailto:")) continue;
                var path = target.Split('#')[0];
                if (!File.Exists(Path.Combine(dir, path)) && !Directory.Exists(Path.Combine(dir, path))) broken.Add($"{doc}: {target}");
            }
        }
        Assert.True(broken.Count == 0, "Broken links: " + string.Join(", ", broken));
    }
}
