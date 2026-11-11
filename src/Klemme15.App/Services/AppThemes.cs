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

using Avalonia.Styling;

namespace Klemme15.App.Services;

/// <summary>Klemme15's own theme besides dark and light.</summary>
public static class AppThemes
{
    /// <summary>
    /// Garage mode: black on white with heavy lines and larger small print, for a laptop on a fender in daylight, where
    /// the dark theme washes out and thin grey text disappears. Built on the light theme.
    /// </summary>
    public static readonly ThemeVariant Garage = new("Garage", ThemeVariant.Light);

    public static readonly IReadOnlyList<string> Names = ["Dark", "Light", "Garage"];

    public static ThemeVariant Variant(string? name) => name switch
    {
        "Light" => ThemeVariant.Light,
        "Garage" => Garage,
        _ => ThemeVariant.Dark,
    };

    /// <summary>The theme after this one: dark, light, garage, and round again.</summary>
    public static string Next(string? name) => Names[(Math.Max(0, Names.ToList().IndexOf(name ?? "Dark")) + 1) % Names.Count];
}
