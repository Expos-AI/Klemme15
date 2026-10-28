using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Klemme15.Core;

namespace Klemme15.Data;

/// <summary>
/// Optional, user-initiated download of the bmw-unified-db fault dictionaries (JSON mirror) from that project's
/// own GitHub releases. Klemme15 does not host or ship this data: it is BMW-derived and published by a third party
/// (github.com/gediz/bmw-unified-db). The download is checked against the release's SHA256SUMS, and only the module
/// variants for the cars Klemme15 supports are kept.
/// </summary>
public static class FaultDataDownload
{
    public const string ProjectUrl = "https://github.com/gediz/bmw-unified-db";
    public const string LicenseUrl = "https://github.com/gediz/bmw-unified-db/blob/main/CREDITS.md";
    public const string AssetUrl = ProjectUrl + "/releases/latest/download/json.tar.gz";
    public const string SumsUrl = ProjectUrl + "/releases/latest/download/SHA256SUMS";
    public const string AssetName = "json.tar.gz";
    public const string ApproxSize = "13 MB";

    /// <summary>Chassis the kept variants must be listed for (E9x is filed under E89 in BMW's data).</summary>
    private static readonly HashSet<string> Chassis = new(StringComparer.OrdinalIgnoreCase)
        { "E46", "E89", "E90", "E91", "E92", "E93", "E81", "E82", "E87", "E88" };

    /// <summary>Default folder: %APPDATA%/Klemme15/bmw-unified-db.</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Klemme15", "bmw-unified-db");

    public sealed record Manifest(string Source, string? Release, string Sha256, DateTimeOffset Downloaded, int Variants);

    public static Manifest? ReadManifest(string? folder = null)
    {
        var path = Path.Combine(folder ?? DefaultFolder, "klemme15-download.json");
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path)) : null; }
        catch (Exception) { return null; }
    }

    /// <summary>Progress: 0..1 while downloading, then 1 while unpacking.</summary>
    public static async Task<Manifest> DownloadAsync(VehicleDatabase db, string? folder = null, IProgress<double>? progress = null,
        HttpClient? http = null, CancellationToken ct = default)
    {
        folder ??= DefaultFolder;
        var ownClient = http is null;
        http ??= new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.TryParseAdd("Klemme15");
        try
        {
            // Pin to one release so the checksum and the archive always match.
            var release = await LatestTagAsync(http, ct);
            var assetUrl = release is null ? AssetUrl : $"{ProjectUrl}/releases/download/{release}/{AssetName}";
            var sumsUrl = release is null ? SumsUrl : $"{ProjectUrl}/releases/download/{release}/SHA256SUMS";

            var sums = await http.GetStringAsync(sumsUrl, ct);
            var expected = sums.Split('\n')
                .Select(l => l.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .FirstOrDefault(p => p.Length == 2 && p[1].TrimStart('*') == AssetName)?[0]
                ?? throw new DiagnosticException($"The fault database release does not list {AssetName}. Try again later or download it by hand from {ProjectUrl}.");

            using var response = await http.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            var buffer = new MemoryStream();
            await using (var body = await response.Content.ReadAsStreamAsync(ct))
            {
                var chunk = new byte[81920];
                int n;
                while ((n = await body.ReadAsync(chunk, ct)) > 0)
                {
                    buffer.Write(chunk, 0, n);
                    if (total > 0) progress?.Report(Math.Min(0.99, (double)buffer.Length / total.Value));
                }
            }

            var actual = Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length))).ToLowerInvariant();
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new DiagnosticException("The downloaded fault database failed its checksum, so it was not used. Please try again.");

            progress?.Report(1);
            buffer.Position = 0;
            var count = Unpack(buffer, folder, db);
            var manifest = new Manifest(assetUrl, release, actual, DateTimeOffset.Now, count);
            File.WriteAllText(Path.Combine(folder, "klemme15-download.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            return manifest;
        }
        finally
        {
            if (ownClient) http.Dispose();
        }
    }

    /// <summary>Installs an already-downloaded json.tar.gz (for offline machines). Returns the number of variants kept.</summary>
    public static int InstallFromFile(string archive, VehicleDatabase db, string? folder = null)
    {
        folder ??= DefaultFolder;
        using var file = File.OpenRead(archive);
        var hash = Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
        file.Position = 0;
        var count = Unpack(file, folder, db);
        var manifest = new Manifest(Path.GetFileName(archive), null, hash, DateTimeOffset.Now, count);
        File.WriteAllText(Path.Combine(folder, "klemme15-download.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        return count;
    }

    private static int Unpack(Stream gz, string folder, VehicleDatabase db)
    {
        var wanted = new HashSet<string>(db.Modules.SelectMany(m => m.Variants), StringComparer.OrdinalIgnoreCase);
        var staging = folder + ".new";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(Path.Combine(staging, "variants"));

        var count = 0;
        using (var unzip = new GZipStream(gz, CompressionMode.Decompress, leaveOpen: true))
        using (var tar = new TarReader(unzip))
        {
            while (tar.GetNextEntry() is { } entry)
            {
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null) continue;
                var name = entry.Name.Replace('\\', '/');
                var file = Path.GetFileName(name);
                if (name.EndsWith("/index.json", StringComparison.Ordinal) && !name.Contains("/variants/", StringComparison.Ordinal))
                {
                    using var copy = File.Create(Path.Combine(staging, "index.json"));
                    entry.DataStream.CopyTo(copy);
                    continue;
                }
                if (!name.Contains("/variants/", StringComparison.Ordinal) || !file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                // Never trust names from an archive: keep plain file names only.
                if (file.Contains("..", StringComparison.Ordinal) || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;

                var data = new MemoryStream();
                entry.DataStream.CopyTo(data);
                if (!wanted.Contains(Path.GetFileNameWithoutExtension(file)) && !ForOurChassis(data)) continue;
                File.WriteAllBytes(Path.Combine(staging, "variants", file.ToLowerInvariant()), data.ToArray());
                count++;
            }
        }
        if (count == 0) throw new DiagnosticException("The fault database archive had no usable module data.");

        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.Move(staging, folder);
        return count;
    }

    private static bool ForOurChassis(MemoryStream json)
    {
        try
        {
            json.Position = 0;
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("chassis", out var c) && c.ValueKind == JsonValueKind.Array
                && c.EnumerateArray().Any(x => x.GetString() is { } s && Chassis.Contains(s))
                && doc.RootElement.TryGetProperty("dtcs", out var d) && d.GetArrayLength() > 0;
        }
        catch (JsonException) { return false; }
    }

    private static async Task<string?> LatestTagAsync(HttpClient http, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/gediz/bmw-unified-db/releases/latest");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            return tag is { Length: > 0 } && tag.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_') ? tag : null;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException) { return null; }
    }
}
