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

using System.Reflection;
using System.Text.Json;

namespace Klemme15.Data;

/// <summary>One thing to do for a symptom: a test, chain, guide or check by id, or a page of the app.</summary>
/// <param name="Kind">"test", "chain", "guide", "check" or "page".</param>
public sealed record SymptomStep(string Kind, string Id, string Why);

/// <summary>What an owner notices ("won't start"), and what to do about it, in order.</summary>
public sealed record Symptom(string Id, string Title, string Summary, IReadOnlyList<SymptomStep> Steps);

/// <summary>Symptom-first help (db/symptoms.json): start from what the car does, not from a code.</summary>
public static class SymptomLibrary
{
    /// <summary>Pages a step can point at.</summary>
    public static readonly IReadOnlyList<string> Pages = ["quick-test", "drain-finder", "ghost-hunter"];

    public static IReadOnlyList<Symptom> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Klemme15.Data.symptoms.json");
        if (stream is null) return [];
        using var doc = JsonDocument.Parse(stream);
        var list = new List<Symptom>();
        foreach (var s in doc.RootElement.GetProperty("symptoms").EnumerateArray())
            list.Add(new(s.GetProperty("id").GetString()!, s.GetProperty("title").GetString()!, s.GetProperty("summary").GetString()!,
                s.GetProperty("steps").EnumerateArray()
                    .Select(x => new SymptomStep(x.GetProperty("kind").GetString()!, x.GetProperty("id").GetString()!, x.GetProperty("why").GetString()!)).ToList()));
        return list;
    }
}
