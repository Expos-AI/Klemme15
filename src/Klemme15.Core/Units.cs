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

namespace Klemme15.Core;

/// <summary>Turns the unit spellings in BMW's data ("degC", "Grad KW", "mg/Hub", "1/min", "0/1") into one readable form.</summary>
public static class Units
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["degC"] = "°C", ["deg C"] = "°C", ["Grad C"] = "°C", ["°C"] = "°C", ["Grad Celsius"] = "°C",
        ["Grad KW"] = "° crank", ["°KW"] = "° crank", ["deg CrS"] = "° crank", ["degCrS"] = "° crank", ["°CRK"] = "° crank",
        ["Grad"] = "°", ["deg"] = "°", ["°TPS"] = "°",
        ["1/min"] = "rpm", ["U/min"] = "rpm",
        ["us"] = "µs", ["µs"] = "µs",
        ["mg/hub"] = "mg/stroke", ["mg/stk"] = "mg/stroke", ["mg/cyc"] = "mg/stroke", ["mg/inj"] = "mg/injection",
        ["Kg/h"] = "kg/h", ["Volt"] = "V", ["mv"] = "mV", ["ohm"] = "Ω", ["ul"] = "µl", ["0/oo"] = "‰", ["%."] = "%",
        ["%DK"] = "%", ["%PED"] = "%",
        // State values (on/off, counters, codes) need no unit.
        ["0/1"] = "", ["0_1"] = "", ["0-n"] = "", ["1/1"] = "", ["hex"] = "", ["-"] = "",
    };

    public static string? Normalize(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return null;
        var u = unit.Replace(" manuell", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (Map.TryGetValue(u, out var mapped)) u = mapped;
        return u.Length == 0 ? null : u;
    }
}

/// <summary>Which units the app shows values in.</summary>
public enum UnitSystem { Metric, US }

/// <summary>
/// Shows distances, speeds, temperatures and pressures in the units the person chose, on screen and in the reports
/// people read. The car and the data files Klemme15 writes (recordings, snapshots, baselines) stay metric.
/// </summary>
public static class DisplayUnits
{
    public const double KmPerMile = 1.609344;

    /// <summary>The chosen system. Metric unless the person (or their region) says otherwise.</summary>
    public static UnitSystem System { get; set; } = UnitSystem.Metric;

    public static bool IsUs => System == UnitSystem.US;

    /// <summary>A value and its unit as shown: 90 °C → 194 °F, 100 km/h → 62 mph. Other units pass through.</summary>
    public static (double Value, string Unit) Convert(double value, string? unit)
    {
        var u = unit ?? "";
        if (!IsUs) return (value, u);
        return u switch
        {
            "°C" => (value * 9 / 5 + 32, "°F"),
            "km/h" => (value / KmPerMile, "mph"),
            "km" => (value / KmPerMile, "mi"),
            "kPa" => (value * 0.1450377, "psi"),
            "bar" => (value * 14.50377, "psi"),
            "hPa" or "mbar" => (value * 0.01450377, "psi"),
            _ => (value, u),
        };
    }

    /// <summary>The unit as shown ("°C" → "°F" in US units).</summary>
    public static string Unit(string? unit) => Convert(0, unit).Unit;

    /// <summary>"98,120 km" or "60,969 mi".</summary>
    public static string Distance(long km) => IsUs ? $"{km / KmPerMile:N0} mi" : $"{km:N0} km";

    /// <summary>"90 °C" or "194 °F".</summary>
    public static string Temperature(double celsius) => IsUs ? $"{celsius * 9 / 5 + 32:0} °F" : $"{celsius:0} °C";

    /// <summary>The system a region uses: US units for the United States, metric elsewhere.</summary>
    public static UnitSystem ForRegion(string? twoLetterRegion) =>
        string.Equals(twoLetterRegion, "US", StringComparison.OrdinalIgnoreCase) ? UnitSystem.US : UnitSystem.Metric;
}
