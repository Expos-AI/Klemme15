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

using System.Text.RegularExpressions;

namespace Klemme15.Core;

/// <summary>A model that matches what someone typed, with its family.</summary>
public sealed record VehicleMatch(VehicleFamily Family, VehicleModel Model);

/// <summary>
/// Finds a car from what an owner knows: "2011 328i", "E92", "X5 diesel", "M3". Every word has to match the model,
/// its family (whose name lists the chassis codes), engine, body or drive; a year has to fall in the model's years.
/// </summary>
public static partial class VehicleSearch
{
    /// <summary>The matches, best first, and a note when the search can't help (an unsupported chassis, nothing found).</summary>
    public static (IReadOnlyList<VehicleMatch> Matches, string? Note) Find(IEnumerable<VehicleFamily> families, string? query)
    {
        var tokens = Word().Matches((query ?? "").ToLowerInvariant()).Select(m => m.Value).ToList();
        if (tokens.Count == 0) return ([], null);
        var all = families.SelectMany(f => f.Models.Select(m => new VehicleMatch(f, m))).ToList();
        var matches = all.Where(x => tokens.All(t => Matches(x, t)))
            .OrderByDescending(x => tokens.Count(t => x.Model.Name.StartsWith(t, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (matches.Count > 0) return (matches, null);

        if (tokens.FirstOrDefault(t => Chassis().IsMatch(t)) is { } code)
        {
            var upper = code.ToUpperInvariant();
            return ([], upper[0] switch
            {
                'F' or 'G' => $"{upper} is an F- or G-series car. They use an Ethernet (ENET) cable and aren't supported yet.",
                'R' => $"{upper} is a Mini. Minis aren't supported yet.",
                _ => $"{upper} isn't supported yet. Supported cars are listed below.",
            });
        }
        return ([], "No car matches that. Try the model and year, like \"2011 328i\", or the chassis code from the door jamb sticker, like \"E92\".");
    }

    private static bool Matches(VehicleMatch x, string token)
    {
        if (Year(token) is { } year)
        {
            var years = FourDigits().Matches(x.Model.Years).Select(m => int.Parse(m.Value)).ToList();
            return years.Count > 0 && year >= years.Min() && year <= years.Max();
        }
        var haystack = $"{x.Family.Id} {x.Family.Name} {x.Model.Name} {x.Model.Engine} {x.Model.Body} {x.Model.Drive}".ToLowerInvariant();
        if (token is "awd" or "xdrive" or "4wd") return haystack.Contains("all-wheel") || haystack.Contains("xdrive");
        if (token is "bmw") return true;
        return Word().Matches(haystack).Any(w => w.Value.StartsWith(token, StringComparison.Ordinal));
    }

    /// <summary>"2011" → 2011; "08" → 2008; "97" → 1997; anything else → null.</summary>
    private static int? Year(string token)
    {
        if (token.Length == 4 && int.TryParse(token, out var y) && y is >= 1990 and <= 2020) return y;
        if (token.Length == 2 && token[0] is '0' or '9' && int.TryParse(token, out var s)) return s >= 90 ? 1900 + s : 2000 + s;
        return null;
    }

    [GeneratedRegex(@"[a-z0-9]+")]
    private static partial Regex Word();

    [GeneratedRegex(@"^[efgr]\d{2}$")]
    private static partial Regex Chassis();

    [GeneratedRegex(@"\b(19|20)\d{2}\b")]
    private static partial Regex FourDigits();
}
