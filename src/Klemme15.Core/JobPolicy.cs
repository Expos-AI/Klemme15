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
/// The jobs Klemme15 refuses to run, whoever asks (the Expert page, the command line, a service function). It keeps
/// the promises in docs/LEGAL.md true even when someone types a job name by hand: no flashing, nothing that reads or
/// changes keys or the immobiliser, no mileage changes, and no writes to the key module (CAS) other than clearing the
/// steering lock's counters.
/// </summary>
public static partial class JobPolicy
{
    /// <summary>
    /// The only jobs that may change anything in the key module: the steering lock counter reset (docs/LEGAL.md) and
    /// clearing its fault and info memory, like every other module's.
    /// </summary>
    public static readonly IReadOnlyList<string> KeyModuleWrites = ["STEUERN_ELVCOUNTER_CAS_LOESCHEN", "STEUERN_ELVCOUNTER_ELV_LOESCHEN", "FS_LOESCHEN", "IS_LOESCHEN"];

    /// <summary>Why Klemme15 won't run this job, or null when it may.</summary>
    public static string? Refusal(string sgbd, string job)
    {
        var j = job.Trim().ToUpperInvariant();
        var s = sgbd.Trim().ToUpperInvariant();
        if (Flash().IsMatch(j))
            return $"Klemme15 never flashes or reprograms a module's software, so it won't run {job}.";
        if (Immobiliser().IsMatch(j))
            return $"Klemme15 never reads or changes keys or the immobiliser, so it won't run {job}.";
        if (Mileage().IsMatch(j) && !IsRead(j))
            return $"Klemme15 only ever reads mileage, so it won't run {job}.";
        if ((s.StartsWith("CAS", StringComparison.Ordinal) || s == "D_0040") && !IsRead(j) && !KeyModuleWrites.Contains(j))
            return $"Klemme15 doesn't write to the key module (CAS), apart from clearing the steering lock's counters, so it won't run {job}.";
        return null;
    }

    /// <summary>Jobs that only read: identification, fault memories, status values, and EDIABAS's own lookups.</summary>
    public static bool IsRead(string job)
    {
        var j = job.Trim().ToUpperInvariant();
        if (WriteWord().IsMatch(j)) return false;
        return j.StartsWith("STATUS_", StringComparison.Ordinal) || j.StartsWith("IDENT", StringComparison.Ordinal) || j.StartsWith('_')
               || j is "INFO" or "INITIALISIERUNG" || j.EndsWith("_LESEN", StringComparison.Ordinal) || j.Contains("_LESEN_", StringComparison.Ordinal);
    }

    [GeneratedRegex("FLASH|BOOT")]
    private static partial Regex Flash();

    [GeneratedRegex("ISN|SCHLUESSEL|SCHLÜSSEL|TRANSPONDER|WEGFAHR|EWS|(^|_)KEY")]
    private static partial Regex Immobiliser();

    [GeneratedRegex("(^|_)KM(_|$)|KM_?STAND|KILOMETER|MEILE|ODOMETER|WEGSTRECKE|LAUFLEISTUNG|TACHO_?STAND")]
    private static partial Regex Mileage();

    [GeneratedRegex("SCHREIBEN|STEUERN|PROGRAMM|LOESCHEN|RESET|SETZEN|WRITE")]
    private static partial Regex WriteWord();
}
