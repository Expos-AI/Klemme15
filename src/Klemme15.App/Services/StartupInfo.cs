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

using Klemme15.Serial;

namespace Klemme15.App.Services;

/// <summary>
/// What the splash screen found while the app started: the official fault texts, the user's BMW files and the
/// serial ports. The Garage uses it instead of looking again, so it opens ready instead of "Looking for...".
/// Any field left null is simply done the usual way.
/// </summary>
public sealed record StartupInfo
{
    public OfficialStatus? Official { get; init; }

    /// <summary>True when the search for BMW files ran (whether or not it found any).</summary>
    public bool EdiabasSearched { get; init; }
    public SgbdFolder? Ediabas { get; init; }

    public IReadOnlyList<PortInfo>? Ports { get; init; }
}
