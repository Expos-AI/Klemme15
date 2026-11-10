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

/// <summary>
/// A plain web search for the part a fault points at, for the car in hand: no shop, no referral, nothing sent until
/// the owner clicks. The search words are the car and the component's name; the owner chooses where to buy.
/// </summary>
public static partial class PartSearch
{
    /// <summary>"BMW E90 328i N52 VANOS solenoids" from a component title like "VANOS solenoids, front of cylinder head".</summary>
    public static string Query(string component, VehicleFamily? family, VehicleModel? model)
    {
        // "Cylinder 3: ignition coil, spark plug, injector" names the parts after the colon; otherwise the name is the part.
        var part = component.Contains(':') ? component[(component.IndexOf(':') + 1)..] : component;
        part = Brackets().Replace(part, " ");
        // Where it sits ("front of cylinder head", "cylinders 1-3") helps find it on the car, not in a shop.
        part = Place().Replace(part, " ");
        part = Spaces().Replace(part.Replace(",", " "), " ").Trim();
        var chassis = family?.Id switch { "E9x" => "E90", "E8x" => "E87", { } id => id, null => "" };
        var name = model is null ? "" : Regex.Match(model.Name, @"^[A-Za-z0-9]+(\s?[A-Z]?\d\.\d[a-z]*)?").Value;
        return Spaces().Replace($"BMW {chassis} {name} {model?.EngineCode} {part}", " ").Trim();
    }

    /// <summary>The search as a web address (DuckDuckGo: no account, no tracking of the search).</summary>
    public static string Url(string query) => "https://duckduckgo.com/?q=" + Uri.EscapeDataString(query);

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Brackets();

    [GeneratedRegex(@"\b(front|rear|side) of [a-z ]+|\bcylinders? \d(-\d)?\b|\bat the [a-z\- ]+", RegexOptions.IgnoreCase)]
    private static partial Regex Place();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
