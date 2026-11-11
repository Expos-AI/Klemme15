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
using System.Text;

namespace Klemme15.Core;

/// <summary>
/// Writes a live data recording to disk while it runs, so a crash, a flat laptop battery or a closed window doesn't
/// lose a drive's worth of data. The file is removed once the recording is saved or thrown away; one that is still
/// there at the next start is offered back.
/// </summary>
public sealed class RecordingAutosave : IDisposable
{
    private const string Prefix = "unsaved-";
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private int _lines;

    public static string DefaultFolder =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Klemme15", "recordings");

    public string Path { get; }

    public RecordingAutosave(string folder, DateTime? now = null)
    {
        Directory.CreateDirectory(folder);
        Path = System.IO.Path.Combine(folder, $"{Prefix}{(now ?? DateTime.Now).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.csv");
        _writer = new StreamWriter(new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
    }

    /// <summary>Adds a row. Flushed every few rows, so at most a second or two is lost if the computer stops.</summary>
    public void AppendLine(string line)
    {
        lock (_lock)
        {
            if (_writer is null) return;
            try
            {
                _writer.WriteLine(line);
                if (++_lines % 10 == 0 || _lines <= 2) _writer.Flush();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                // A full disk must not stop the recording in memory.
                try { _writer.Dispose(); } catch (Exception) { }
                _writer = null;
            }
        }
    }

    /// <summary>The recording was saved elsewhere or thrown away: remove the copy.</summary>
    public void Discard()
    {
        Dispose();
        try { File.Delete(Path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            try { _writer?.Dispose(); } catch (Exception e) when (e is IOException or ObjectDisposedException) { }
            _writer = null;
        }
    }

    /// <summary>Recordings left behind by a session that didn't end normally, newest first. Empty ones are removed.</summary>
    public static IReadOnlyList<string> Leftovers(string folder, string? except = null)
    {
        try
        {
            if (!Directory.Exists(folder)) return [];
            var found = new List<string>();
            foreach (var file in Directory.GetFiles(folder, Prefix + "*.csv").OrderByDescending(f => f, StringComparer.Ordinal))
            {
                if (file == except) continue;
                // A header alone (or nothing) isn't worth offering back.
                if (new FileInfo(file).Length < 40 || File.ReadLines(file).Take(3).Count() < 3) { try { File.Delete(file); } catch (IOException) { } continue; }
                found.Add(file);
            }
            return found;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>"12 November 2026, 14:05" from the file's name, for the offer to recover it.</summary>
    public static string When(string path) =>
        DateTime.TryParseExact(System.IO.Path.GetFileNameWithoutExtension(path)[Prefix.Length..], "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t.ToString("d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture) : "an earlier session";
}
