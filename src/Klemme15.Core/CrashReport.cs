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

using System.Text;

namespace Klemme15.Core;

/// <summary>
/// When Klemme15 closes because of an error nothing caught, the error is written to a small text file in its data
/// folder, and the next start offers it for a bug report. The file holds the error and where in Klemme15 it happened,
/// plus the version and operating system. It is never sent anywhere.
/// </summary>
public static class CrashReport
{
    public const string FilePrefix = "crash-";

    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Klemme15");

    /// <summary>Writes the report. Never throws: it runs while the app is going down.</summary>
    public static string? Write(Exception e, string folder, string version, string? context = null)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{FilePrefix}{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            var text = new StringBuilder()
                .AppendLine($"Klemme15 {version} closed because of an error.")
                .AppendLine($"When: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}")
                .AppendLine($"System: {System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({System.Runtime.InteropServices.RuntimeInformation.OSArchitecture})");
            if (context is not null) text.AppendLine($"Where: {context}");
            text.AppendLine().AppendLine(e.ToString());
            File.WriteAllText(path, text.ToString());
            // Keep the newest few.
            foreach (var old in new DirectoryInfo(folder).GetFiles(FilePrefix + "*.txt").OrderByDescending(f => f.Name).Skip(5)) old.Delete();
            return path;
        }
        catch (Exception) { return null; }
    }

    /// <summary>The newest report the user hasn't seen yet, or null.</summary>
    public static string? Unseen(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).Exists
                ? new DirectoryInfo(folder).GetFiles(FilePrefix + "*.txt").Where(f => !File.Exists(f.FullName + ".seen"))
                    .OrderByDescending(f => f.Name).FirstOrDefault()?.FullName
                : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Marks a report as shown, so it is offered once.</summary>
    public static void MarkSeen(string path)
    {
        try { File.WriteAllText(path + ".seen", ""); } catch (Exception) { }
    }
}
