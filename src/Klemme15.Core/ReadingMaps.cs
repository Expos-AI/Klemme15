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

namespace Klemme15.Core;

/// <summary>Where one value sits in a module's reply, and how to turn its bits into a number.</summary>
public sealed record MapField
{
    public required string Name { get; init; }
    public string Unit { get; init; } = "";

    /// <summary>The first byte of the value, counted from the start of the reply (the service byte is 0).</summary>
    public required int Byte { get; init; }

    /// <summary>Bits to skip at the low end of the value (0 for whole bytes).</summary>
    public int Bit { get; init; }
    public required int Bits { get; init; }
    public bool Signed { get; init; }
    public bool LittleEndian { get; init; }
    public double Factor { get; init; } = 1;
    public double Offset { get; init; }

    /// <summary>How the author worked this out on a car, in their own words. Required: a map is only as good as its evidence.</summary>
    public required string Observed { get; init; }
    public string? ConfirmedOn { get; init; }
}

/// <summary>One read request to one kind of module, and the values in its reply.</summary>
public sealed record ReadingMap
{
    /// <summary>The module, as Klemme15 names it (an id such as "E90.DME") or its version (such as "MSV70").</summary>
    public required string Module { get; init; }

    /// <summary>The request, as hex. Only read requests are accepted.</summary>
    public required string Request { get; init; }

    /// <summary>What a good reply starts with, as hex. Left out: the request's first byte + 0x40, then the rest of the request.</summary>
    public string? ReplyPrefix { get; init; }
    public string? Author { get; init; }
    public required IReadOnlyList<MapField> Fields { get; init; }

    public byte[] RequestBytes => Convert.FromHexString(Clean(Request));
    public byte[] ReplyPrefixBytes
    {
        get
        {
            if (ReplyPrefix is not null) return Convert.FromHexString(Clean(ReplyPrefix));
            var request = RequestBytes;
            request[0] += 0x40;
            return request;
        }
    }

    internal static string Clean(string hex) => hex.Replace(" ", "").Replace("-", "").Replace("0x", "", StringComparison.OrdinalIgnoreCase);
}

public sealed record MappedValue(string Name, double Value, string Unit);

/// <summary>
/// Community reading maps: JSON files, written from what people saw on their own cars, that say where values sit in
/// a module's replies. With a map Klemme15 can show those values without anyone's BMW files. A map can only describe
/// a read: the loader refuses every request that isn't one of the read services below, so no map can write to a
/// module, drive a part or clear anything.
/// </summary>
public static class ReadingMaps
{
    /// <summary>KWP2000 read services a map may use: read by local identifier, read by common identifier, read identification.</summary>
    public static readonly IReadOnlySet<byte> ReadServices = new HashSet<byte> { 0x21, 0x22, 0x1A };

    public const int MaxFile = 1 << 20;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
    };

    private sealed record MapFile(IReadOnlyList<ReadingMap>? Maps);

    /// <summary>Reads a map file. Throws <see cref="DiagnosticException"/> saying what is wrong with it.</summary>
    public static IReadOnlyList<ReadingMap> Load(string json)
    {
        if (json.Length > MaxFile) throw new DiagnosticException("That map file is too large to be a reading map.");
        MapFile? file;
        try { file = JsonSerializer.Deserialize<MapFile>(json, Json); }
        catch (JsonException e) { throw new DiagnosticException($"That file isn't a Klemme15 reading map: {e.Message}"); }
        if (file?.Maps is not { Count: > 0 } maps) throw new DiagnosticException("That file has no maps in it (it needs a \"maps\" list).");
        foreach (var m in maps)
            if (Problem(m) is { } problem) throw new DiagnosticException($"Map for {m?.Module ?? "?"} {m?.Request ?? ""}: {problem}");
        return maps;
    }

    public static string ToJson(IEnumerable<ReadingMap> maps) => JsonSerializer.Serialize(new MapFile(maps.ToList()), Json);

    /// <summary>What is wrong with a map, or null when it can be used.</summary>
    public static string? Problem(ReadingMap? map)
    {
        if (map is null) return "it is empty.";
        if (string.IsNullOrWhiteSpace(map.Module)) return "it doesn't name a module.";
        byte[] request, prefix;
        try { request = map.RequestBytes; prefix = map.ReplyPrefixBytes; }
        catch (Exception e) when (e is FormatException or ArgumentException or NullReferenceException or IndexOutOfRangeException) { return "the request or reply prefix isn't valid hex."; }
        if (request.Length is < 2 or > 16) return "the request must be 2 to 16 bytes.";
        if (!ReadServices.Contains(request[0])) return $"request {request[0]:X2} isn't a read. Maps can only use read requests ({string.Join(", ", ReadServices.Order().Select(s => s.ToString("X2")))}).";
        if (prefix.Length == 0 || prefix[0] != request[0] + 0x40) return "the reply prefix doesn't belong to the request.";
        if (map.Fields is not { Count: > 0 }) return "it has no fields.";
        foreach (var f in map.Fields)
        {
            if (f is null || string.IsNullOrWhiteSpace(f.Name)) return "a field has no name.";
            if (f.Bits is < 1 or > 32) return $"{f.Name}: bits must be 1 to 32.";
            if (f.Bit is < 0 or > 7) return $"{f.Name}: bit must be 0 to 7.";
            if (f.Byte < 0 || f.Byte > 4095) return $"{f.Name}: byte is out of range.";
            if (!double.IsFinite(f.Factor) || !double.IsFinite(f.Offset) || f.Factor == 0) return $"{f.Name}: the factor and offset must be numbers, and the factor can't be 0.";
            if (string.IsNullOrWhiteSpace(f.Observed)) return $"{f.Name}: say how you worked it out (\"observed\").";
        }
        return null;
    }

    /// <summary>
    /// The number a field holds in a reply. False when the reply is too short for it. Bit 0 is the least significant
    /// bit of the value; values are big-endian unless the field says otherwise.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> reply, MapField field, out double value)
    {
        value = 0;
        if (field.Bits is < 1 or > 32 || field.Bit is < 0 or > 7 || field.Byte < 0) return false;
        var count = (field.Bit + field.Bits + 7) / 8;
        if (field.Byte > reply.Length - count) return false;
        var bytes = reply.Slice(field.Byte, count);
        ulong raw = 0;
        if (field.LittleEndian) for (var i = count - 1; i >= 0; i--) raw = raw << 8 | bytes[i];
        else foreach (var b in bytes) raw = raw << 8 | b;
        var mask = (1UL << field.Bits) - 1;
        raw = raw >> field.Bit & mask;
        var number = field.Signed && (raw >> (field.Bits - 1) & 1) == 1 ? (long)(raw | ~mask) : (long)raw;
        value = number * field.Factor + field.Offset;
        return true;
    }

    /// <summary>Every value a map finds in a reply (the reply without the frame around it). Empty when the reply isn't the one the map describes.</summary>
    public static IReadOnlyList<MappedValue> Decode(ReadingMap map, ReadOnlySpan<byte> reply)
    {
        if (!reply.StartsWith(map.ReplyPrefixBytes)) return [];
        var values = new List<MappedValue>(map.Fields.Count);
        foreach (var f in map.Fields)
            if (TryDecode(reply, f, out var v)) values.Add(new MappedValue(f.Name, v, f.Unit));
        return values;
    }

    public static string Format(MappedValue v) =>
        $"{v.Name}: {v.Value.ToString("0.###", CultureInfo.InvariantCulture)}{(v.Unit.Length > 0 ? " " + v.Unit : "")}";
}
