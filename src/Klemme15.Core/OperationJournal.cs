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

    /// <summary>The job running now. If this file is still there when Klemme15 starts, the job was cut off.</summary>
    public string Path { get; } = path;

    /// <summary>A job that was cut off and not yet dismissed, kept apart so a new job can't overwrite its advice.</summary>
    public string InterruptedPath => System.IO.Path.ChangeExtension(Path, ".interrupted.json");

    public void Begin(RunningOperation op) => Write(Path, op);

    /// <summary>The job ended normally: forget it.</summary>
    public void End() => Delete(Path);

    /// <summary>The job was cut off (or Klemme15 found it still recorded at start-up): keep it until dismissed.</summary>
    public RunningOperation? CutOff()
    {
        if (Read(Path) is not { } op) return Pending();
        Write(InterruptedPath, op);
        Delete(Path);
        return op;
    }

    /// <summary>The interrupted job waiting to be dismissed, or a job still recorded as running (from a crash), or null.</summary>
    public RunningOperation? Pending() => Read(InterruptedPath) ?? Read(Path);

    /// <summary>The user has dealt with it.</summary>
    public void Dismiss()
    {
        Delete(InterruptedPath);
        Delete(Path);
    }

    private static void Write(string path, RunningOperation op)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(op, Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static RunningOperation? Read(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 64_000) return null;
            return JsonSerializer.Deserialize<RunningOperation>(File.ReadAllText(path), Json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    /// <summary>The banner's title: what was interrupted.</summary>
    public static string Headline(RunningOperation op) =>
        $"'{op.Title}' was interrupted ({op.Started.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture)})";

    /// <summary>What it means and what to do next, in two or three short sentences.</summary>
    public static string Advice(RunningOperation op) => op.Kind switch
    {
        OperationKind.PartTest =>
            $"Nothing is stuck: the {op.ModuleName} ends the session by itself. When you reconnect, Klemme15 sends the switch-off for the part again. Then run the test again if you need its result.",
        OperationKind.CodingWrite =>
            $"The {op.ModuleName}'s coding may be only partly written. Reconnect, put the battery on a charger and restore the backup taken just before" +
            (op.BackupTaken is { } b ? $" ({b.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)})" : "") + " on the Coding page.",
        _ => $"Nothing is stuck: the {op.ModuleName} ends the session by itself, and an interrupted procedure just doesn't complete. Reconnect, run a quick test, then start '{op.Title}' again. Done for now? Switch the ignition off so the battery doesn't drain.",
    };
}
