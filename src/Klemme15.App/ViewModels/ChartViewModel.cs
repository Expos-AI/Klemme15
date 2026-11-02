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

namespace Klemme15.App.ViewModels;

/// <summary>One graph on the Tests page: every line that shares a unit.</summary>
public sealed class ChartViewModel(string unit, IReadOnlyList<TestTrace> traces)
{
    public string Unit { get; } = unit;
    public IReadOnlyList<TestTrace> Traces { get; } = traces;

    public string Title => Unit switch
    {
        "rpm" => "Engine speed (rpm)",
        "%" => Traces.Any(t => t.Name.Contains("trim", StringComparison.OrdinalIgnoreCase)) ? "Fuel trim (%)" : "Command (%)",
        "°" => "Camshaft position (°)",
        "V" => "Battery voltage (V)",
        "pump" => "Water pump speed (as the engine computer reports it)",
        "g/s" => "Air mass (g/s)",
        "kPa" => "Pressure (kPa)",
        "°C" => "Temperature (°C)",
        _ => Unit,
    };

    public static IReadOnlyList<ChartViewModel> From(IReadOnlyList<TestTrace>? traces) =>
        traces is null ? [] : traces.GroupBy(t => t.Unit).Select(g => new ChartViewModel(g.Key, g.ToList())).ToList();
}
