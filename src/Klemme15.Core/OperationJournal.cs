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

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Klemme15.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OperationKind { ServiceFunction, PartTest, CodingWrite }

/// <summary>A job that changes something on the car, written to disk while it runs.</summary>
public sealed record RunningOperation
{
    public required OperationKind Kind { get; init; }
    public required string Title { get; init; }
    public required string ModuleId { get; init; }
    public required string ModuleName { get; init; }
    public string? Car { get; init; }
    public required DateTimeOffset Started { get; init; }

    /// <summary>"pump": Klemme15 drove the electric water pump and switches it off again on reconnect.</summary>
    public string? StopAction { get; init; }

    /// <summary>When the coding backup taken just before a write was made (to find it on the Coding page).</summary>
    public DateTimeOffset? BackupTaken { get; init; }
}

/// <summary>
/// Remembers a job that changes something on the car while it runs, so that if it's cut off (cable pulled, laptop
/// asleep, Windows restarting, app closed) Klemme15 knows next time and says exactly what to do. The entry is
/// removed when the job ends normally, including when you press Stop. One small JSON file; no VIN.
/// </summary>
public sealed class OperationJournal(string path)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public string Path { get; } = path;

    public void Begin(RunningOperation op)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(op, Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public void End()
    {
        try { if (File.Exists(Path)) File.Delete(Path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>A job that didn't end normally, or null.</summary>
    public RunningOperation? Pending()
    {
        try
        {
            if (!File.Exists(Path) || new FileInfo(Path).Length > 64_000) return null;
            return JsonSerializer.Deserialize<RunningOperation>(File.ReadAllText(Path), Json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    /// <summary>What happened and what to do now, in plain words.</summary>
    public static string Advice(RunningOperation op)
    {
        var when = op.Started.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture);
        var head = $"'{op.Title}' on the {op.ModuleName} was cut off ({when}): the cable, the computer or Klemme15 stopped before it finished.";
        var sleep = "Modules drop the diagnostic session by themselves within seconds once the tool goes quiet, so nothing stays stuck in it.";
        var off = "If you're done for now, switch the ignition off so the car can go to sleep and doesn't drain the battery.";
        return op.Kind switch
        {
            OperationKind.PartTest => $"{head} {sleep} When you reconnect, Klemme15 sends the switch-off for the part again to be sure. Then run the test again from the start if you need its result. {off}",
            OperationKind.CodingWrite => $"{head} The module's coding may be only partly written. Reconnect, open the Coding page and restore the backup Klemme15 took just before the write" +
                (op.BackupTaken is { } b ? $" ({b.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture)})" : "") + $", then check the module with a quick test. Keep the battery on a charger while you do. {off}",
            _ => $"{head} {sleep} Most routines either finish on their own (a cooling bleed keeps running its cycle) or stop with the session; an interrupted adaptation or learning run simply doesn't complete. Reconnect, run a quick test to check the {op.ModuleName} for new faults, then run '{op.Title}' again from the start. {off}",
        };
    }
}
