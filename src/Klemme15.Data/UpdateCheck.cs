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

using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Klemme15.Data;

/// <summary>A newer release than the one running.</summary>
public sealed record AvailableUpdate(string Tag, Version Version, string DownloadUrl, string ReleaseUrl);

/// <summary>
/// Looks for a newer Klemme15. It downloads one small file from the project's website (latest.json, written by the
/// release workflow) and sends nothing else: not the version you run, nothing about you or your car. It never downloads
/// or installs anything by itself.
/// </summary>
public static partial class UpdateCheck
{
    /// <summary>The version file on the project's website (GitHub Pages, not GitHub's rate-limited API).</summary>
    public const string LatestUrl = "https://expos-ai.github.io/Klemme15/latest.json";
    public const string Releases = "https://github.com/Expos-AI/Klemme15/releases";

    /// <summary>"v0.99.23" or "0.99.23-beta" → 0.99.23; anything else → null.</summary>
    public static Version? ParseTag(string? tag) =>
        tag is not null && Tag().Match(tag.Trim()) is { Success: true } m && Version.TryParse(m.Groups[1].Value, out var v) ? v : null;

    [GeneratedRegex(@"^v?(\d+\.\d+\.\d+)(?:-[0-9A-Za-z.]+)?$")]
    private static partial Regex Tag();

    /// <summary>The release download for this computer, e.g. "win-x64" or "osx-arm64".</summary>
    public static string Rid =>
        OperatingSystem.IsWindows() ? "win-x64"
        : OperatingSystem.IsMacOS() ? (RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64")
        : "linux-x64";

    /// <summary>The newer release described by a latest.json file, or null when <paramref name="current"/> is up to date.</summary>
    public static AvailableUpdate? FromJson(string json, Version current)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.TryGetProperty("tag", out var t) ? t.GetString() : null;
            if (ParseTag(tag) is not { } latest || Normalise(latest) <= Normalise(current)) return null;
            return new(tag!.Trim(), latest, $"{Releases}/download/{tag!.Trim()}/Klemme15-{tag!.Trim()}-{Rid}.zip", $"{Releases}/tag/{tag!.Trim()}");
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Downloads latest.json and compares. Throws <see cref="HttpRequestException"/> when the website can't be reached.</summary>
    public static async Task<AvailableUpdate?> CheckAsync(Version current, HttpClient? http = null, CancellationToken ct = default)
    {
        using var own = http is null ? new HttpClient { Timeout = TimeSpan.FromSeconds(8) } : null;
        var client = http ?? own!;
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
        request.Headers.CacheControl = new() { NoCache = true };
        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return FromJson(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), current);
    }

    /// <summary>Compares major.minor.build only (assembly versions carry a fourth part).</summary>
    private static Version Normalise(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
}
