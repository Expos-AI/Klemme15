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
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Klemme15.Core;

/// <summary>
/// The last minute of live data, kept while live data or a test runs, so a snapshot can carry what the car was doing.
/// Rows older than the window are dropped. Columns are "Name (unit)", like the Live page's CSV.
/// </summary>
public sealed class LiveBuffer(TimeSpan window)
{
    private readonly object _lock = new();
    private readonly LinkedList<(DateTimeOffset Time, Dictionary<string, double?> Values)> _rows = new();
    private readonly List<string> _columns = [];

    public LiveBuffer() : this(TimeSpan.FromSeconds(60)) { }

    public TimeSpan Window { get; } = window;

    public int Count { get { lock (_lock) return _rows.Count; } }

    public void Add(DateTimeOffset time, IReadOnlyDictionary<string, double?> values)
    {
        lock (_lock)
        {
            foreach (var k in values.Keys) if (!_columns.Contains(k)) _columns.Add(k);
            _rows.AddLast((time, new Dictionary<string, double?>(values)));
            while (_rows.First is { } first && time - first.Value.Time > Window) _rows.RemoveFirst();
        }
    }

    public void Clear() { lock (_lock) { _rows.Clear(); _columns.Clear(); } }

    /// <summary>The buffer as CSV: seconds since the first row, then one column per value. Empty when nothing was recorded.</summary>
    public string ToCsv()
    {
        lock (_lock)
        {
            if (_rows.Count == 0) return "";
            var sb = new StringBuilder();
            sb.AppendLine("time," + string.Join(",", _columns.Select(c => $"\"{c.Replace("\"", "'")}\"")));
            var t0 = _rows.First!.Value.Time;
            foreach (var (time, values) in _rows)
                sb.AppendLine((time - t0).TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture) + "," +
                    string.Join(",", _columns.Select(c => values.TryGetValue(c, out var v) && v is { } d ? d.ToString(CultureInfo.InvariantCulture) : "")));
            return sb.ToString();
        }
    }
}

/// <summary>A guided test's result inside a snapshot.</summary>
public sealed record SnapshotTest(string TestId, string Title, DateTimeOffset Time, TestReport Report);

/// <summary>One comfort module's coding inside a snapshot. For viewing and comparing only: never written to a car.</summary>
public sealed record SnapshotCoding(string Module, string? PartNumber, string? CodingIndex, IReadOnlyList<SnapshotCodingBlock> Blocks);

public sealed record SnapshotCodingBlock(ushort Id, string Hex);

/// <summary>What a snapshot says about itself.</summary>
public sealed record SnapshotManifest
{
    public const string FormatName = "klemme15-snapshot";
    public const int CurrentVersion = 1;

    public string Format { get; init; } = FormatName;
    public int Version { get; init; } = CurrentVersion;
    public string? App { get; init; }
    public DateTimeOffset Created { get; init; }
    public string? FamilyId { get; init; }
    public string? ModelId { get; init; }
    public string? ModelName { get; init; }
    public string? Chassis { get; init; }

    /// <summary>The owner's own words: what's wrong, what they already tried.</summary>
    public string? Note { get; init; }

    /// <summary>Why the snapshot was taken, when the Ghost Hunter caught something (older Klemme15 versions ignore it).</summary>
    public Klemme15.Core.GhostHunter.TriggerEvent? Trigger { get; init; }

    public IReadOnlyList<SnapshotSection> Sections { get; init; } = [];
}

public sealed record SnapshotSection(string Name, string Sha256, long Bytes);

/// <summary>
/// A whole car as it was at one moment: its modules, faults with their details, dash messages and story, health trends,
/// the last minute of live data, test graphs and (optionally) comfort-module coding. Opens in Klemme15 read-only, as if
/// plugged in. Never contains the VIN.
/// </summary>
public sealed record CarSnapshot
{
    public required SnapshotManifest Manifest { get; init; }
    public required VehicleScanResult Scan { get; init; }
    public string LiveCsv { get; init; } = "";
    public IReadOnlyList<SnapshotTest> Tests { get; init; } = [];
    public IReadOnlyList<SnapshotCoding> Coding { get; init; } = [];
    public string? TrafficLog { get; init; }

    public bool HasLive => LiveCsv.Length > 0;
}

/// <summary>Why a .k15 file was refused.</summary>
public sealed class SnapshotException(string message) : Exception(message);

/// <summary>Writes and reads .k15 files: a plain zip, so anyone can unzip one and see exactly what's in it.</summary>
public static class SnapshotFile
{
    public const string Extension = ".k15";

    // Limits for opening a stranger's file: generous for a real car, small enough to stop a zip bomb.
    public const long MaxFileBytes = 25L * 1024 * 1024;
    public const long MaxEntryBytes = 20L * 1024 * 1024;
    public const long MaxTotalBytes = 60L * 1024 * 1024;
    public const int MaxEntries = 64;

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 64,
    };

    private static readonly Regex AllowedName = new(@"^(manifest\.json|scan\.json|live\.csv|log\.txt|tests/[a-z0-9-]{1,40}-\d{1,3}\.json|coding/[A-Za-z0-9]{1,12}\.json)$", RegexOptions.Compiled);

    // ---------------------------------------------------------------- writing

    /// <summary>
    /// Builds the .k15 file. The VIN is removed everywhere it appears (full and short form, as text and inside coding bytes)
    /// before anything is written. Returns the file's bytes.
    /// </summary>
    public static byte[] Write(CarSnapshot snapshot)
    {
        var vin = snapshot.Scan.Vin;
        var scan = snapshot.Scan with { Vin = null };
        var entries = new List<(string Name, byte[] Data)>
        {
            ("scan.json", Text(Scrub(JsonSerializer.Serialize(scan, Json), vin))),
        };
        if (snapshot.LiveCsv.Length > 0) entries.Add(("live.csv", Text(Scrub(snapshot.LiveCsv, vin))));
        for (var i = 0; i < snapshot.Tests.Count; i++)
        {
            var t = snapshot.Tests[i];
            var id = Regex.Replace(t.TestId.ToLowerInvariant(), "[^a-z0-9-]", "-");
            entries.Add(($"tests/{id[..Math.Min(id.Length, 40)]}-{i + 1}.json", Text(Scrub(JsonSerializer.Serialize(t, Json), vin))));
        }
        foreach (var c in snapshot.Coding.Take(12))
        {
            var module = Regex.Replace(c.Module, "[^A-Za-z0-9]", "");
            if (module.Length == 0) continue;
            var clean = c with { Blocks = c.Blocks.Select(b => b with { Hex = ScrubHex(b.Hex, vin) }).ToList() };
            entries.Add(($"coding/{module[..Math.Min(module.Length, 12)]}.json", Text(Scrub(JsonSerializer.Serialize(clean, Json), vin))));
        }
        if (snapshot.TrafficLog is { Length: > 0 } log) entries.Add(("log.txt", Text(Scrub(log, vin))));

        var manifest = snapshot.Manifest with
        {
            Format = SnapshotManifest.FormatName,
            Version = SnapshotManifest.CurrentVersion,
            Note = snapshot.Manifest.Note is { } n ? Scrub(n, vin) : null,
            Sections = entries.Select(e => new SnapshotSection(e.Name, Hash(e.Data), e.Data.Length)).ToList(),
        };

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Put(string name, byte[] data)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var s = entry.Open();
                s.Write(data);
            }
            Put("manifest.json", Text(JsonSerializer.Serialize(manifest, Json)));
            foreach (var (name, data) in entries) Put(name, data);
        }
        return ms.ToArray();
    }

    // ---------------------------------------------------------------- reading

    /// <summary>Opens a .k15 file someone shared. Strict: anything unexpected is refused rather than guessed at.</summary>
    public static CarSnapshot Read(byte[] file)
    {
        if (file.Length > MaxFileBytes) throw new SnapshotException($"This file is {file.Length / 1024 / 1024} MB; snapshots are at most {MaxFileBytes / 1024 / 1024} MB.");
        ZipArchive zip;
        try { zip = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read); }
        catch (InvalidDataException) { throw new SnapshotException("This isn't a Klemme15 snapshot (it isn't a valid .k15 file)."); }
        using (zip)
        {
            if (zip.Entries.Count > MaxEntries) throw new SnapshotException("The file has too many parts to be a Klemme15 snapshot.");
            var data = new Dictionary<string, byte[]>();
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue; // a folder
                if (!AllowedName.IsMatch(entry.FullName)) continue; // unknown parts are ignored, never extracted
                if (entry.Length > MaxEntryBytes) throw new SnapshotException($"Part {entry.FullName} is too large.");
                var bytes = ReadLimited(entry, MaxEntryBytes);
                total += bytes.Length;
                if (total > MaxTotalBytes) throw new SnapshotException("The file unpacks to more than a snapshot can hold.");
                data[entry.FullName] = bytes;
            }

            if (!data.TryGetValue("manifest.json", out var manifestBytes)) throw new SnapshotException("This isn't a Klemme15 snapshot (no manifest).");
            var manifest = Parse<SnapshotManifest>(manifestBytes, "manifest");
            if (manifest.Format != SnapshotManifest.FormatName) throw new SnapshotException("This isn't a Klemme15 snapshot.");
            if (manifest.Version > SnapshotManifest.CurrentVersion)
                throw new SnapshotException($"This snapshot was made by a newer Klemme15 ({manifest.App ?? "unknown version"}). Update Klemme15 to open it.");

            // Every part must match the checksum the manifest lists: a damaged or edited file is refused.
            foreach (var section in manifest.Sections)
            {
                if (!data.TryGetValue(section.Name, out var bytes)) throw new SnapshotException($"The snapshot is incomplete: {section.Name} is missing.");
                if (!Hash(bytes).Equals(section.Sha256, StringComparison.OrdinalIgnoreCase)) throw new SnapshotException($"The snapshot is damaged: {section.Name} doesn't match its checksum.");
            }
            var listed = manifest.Sections.Select(s => s.Name).ToHashSet();
            foreach (var extra in data.Keys.Where(k => k != "manifest.json" && !listed.Contains(k)).ToList()) data.Remove(extra);

            if (!data.TryGetValue("scan.json", out var scanBytes)) throw new SnapshotException("The snapshot has no scan in it.");
            var scan = Parse<VehicleScanResult>(scanBytes, "scan") with { Vin = null };
            var tests = data.Where(kv => kv.Key.StartsWith("tests/", StringComparison.Ordinal)).OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => Parse<SnapshotTest>(kv.Value, kv.Key)).ToList();
            var coding = data.Where(kv => kv.Key.StartsWith("coding/", StringComparison.Ordinal)).OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => Parse<SnapshotCoding>(kv.Value, kv.Key))
                .Select(c => c with { Blocks = c.Blocks.Where(b => IsHex(b.Hex)).ToList() }).ToList();
            return new CarSnapshot
            {
                Manifest = manifest,
                Scan = scan,
                LiveCsv = data.TryGetValue("live.csv", out var live) ? Encoding.UTF8.GetString(live) : "",
                Tests = tests,
                Coding = coding,
                TrafficLog = data.TryGetValue("log.txt", out var log) ? Encoding.UTF8.GetString(log) : null,
            };
        }
    }

    // ---------------------------------------------------------------- helpers

    private static readonly Regex VinPattern = new(@"\b[A-HJ-NPR-Z0-9]{17}\b", RegexOptions.Compiled);
    private static readonly Regex ShortVinPattern = new(@"\b[A-HJ-NPR-Z]{2}[0-9]{5}\b", RegexOptions.Compiled);

    /// <summary>Removes the car's VIN (and anything shaped like one, and BMW's short VIN) from text.</summary>
    public static string Scrub(string text, string? vin)
    {
        if (vin is { Length: 17 } v)
        {
            text = text.Replace(v, "[VIN removed]", StringComparison.OrdinalIgnoreCase);
            text = text.Replace(v[^7..], "[VIN removed]", StringComparison.OrdinalIgnoreCase);
            // As raw bytes in the traffic log ("57 42 41 ..."), with or without spaces.
            foreach (var part in new[] { v, v[^7..] })
            {
                var hex = Convert.ToHexString(Encoding.ASCII.GetBytes(part.ToUpperInvariant()));
                var spaced = string.Join(" ", hex.Chunk(2).Select(c => new string(c)));
                text = text.Replace(spaced, "[VIN removed]", StringComparison.OrdinalIgnoreCase).Replace(hex, "[VIN removed]", StringComparison.OrdinalIgnoreCase);
            }
        }
        text = VinPattern.Replace(text, m => m.Value.Any(char.IsLetter) && m.Value.Any(char.IsDigit) ? "[VIN removed]" : m.Value);
        return ShortVinPattern.Replace(text, "[VIN removed]");
    }

    /// <summary>Zeroes the VIN's characters wherever they appear inside coding bytes (stored as ASCII).</summary>
    public static string ScrubHex(string hex, string? vin)
    {
        if (!IsHex(hex)) return "";
        if (vin is not { Length: 17 }) return hex.ToUpperInvariant();
        var bytes = Convert.FromHexString(hex);
        foreach (var needle in new[] { vin, vin[^7..] })
        {
            var pattern = Encoding.ASCII.GetBytes(needle.ToUpperInvariant());
            for (var i = 0; i + pattern.Length <= bytes.Length; i++)
                if (bytes.AsSpan(i, pattern.Length).SequenceEqual(pattern)) bytes.AsSpan(i, pattern.Length).Clear();
        }
        return Convert.ToHexString(bytes);
    }

    private static bool IsHex(string s) => s.Length % 2 == 0 && s.Length <= 16384 && s.All(Uri.IsHexDigit);

    private static byte[] Text(string s) => Encoding.UTF8.GetBytes(s);

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static byte[] ReadLimited(ZipArchiveEntry entry, long limit)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (ms.Length + n > limit) throw new SnapshotException($"Part {entry.FullName} unpacks to more than a snapshot can hold.");
            ms.Write(buffer, 0, n);
        }
        return ms.ToArray();
    }

    private static T Parse<T>(byte[] bytes, string what)
    {
        try { return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new SnapshotException($"The snapshot's {what} is empty."); }
        catch (JsonException e) { throw new SnapshotException($"The snapshot's {what} can't be read: {e.Message}"); }
    }
}
