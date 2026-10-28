using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Klemme15.Core;
using Klemme15.Data;
using Microsoft.Data.Sqlite;

namespace Klemme15.DataImport;

/// <summary>
/// Optional second download: the full bmw-unified-db SQLite file (about 130 MB) from that project's GitHub release.
/// Klemme15 keeps only the measurement tables (BMW's MESSWERTETAB) of the module variants for the cars it supports,
/// as small JSON files in the user's data folder, then deletes the big file. Nothing here ships with Klemme15.
/// </summary>
public static class FullDatabaseImport
{
    public const string AssetName = "bmw.sqlite.gz";
    public const string ApproxSize = "130 MB";

    /// <summary>Downloads, checks the release checksum, extracts the tables. Returns how many variants got a table.</summary>
    public static async Task<int> DownloadAsync(VehicleDatabase db, string? folder = null, IProgress<double>? progress = null,
        HttpClient? http = null, CancellationToken ct = default)
    {
        folder ??= FaultDataDownload.DefaultFolder;
        var ownClient = http is null;
        http ??= new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.TryParseAdd("Klemme15");
        var gz = Path.Combine(Path.GetTempPath(), $"klemme15-{Guid.NewGuid():N}.sqlite.gz");
        try
        {
            var release = await FaultDataDownload.LatestTagAsync(http, ct).ConfigureAwait(false);
            var baseUrl = release is null ? $"{FaultDataDownload.ProjectUrl}/releases/latest/download" : $"{FaultDataDownload.ProjectUrl}/releases/download/{release}";
            var sums = await http.GetStringAsync($"{baseUrl}/SHA256SUMS", ct).ConfigureAwait(false);
            var expected = sums.Split('\n').Select(l => l.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .FirstOrDefault(p => p.Length == 2 && p[1].TrimStart('*') == AssetName)?[0]
                ?? throw new DiagnosticException($"The database release does not list {AssetName}. Try again later.");

            using (var response = await http.GetAsync($"{baseUrl}/{AssetName}", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var file = File.Create(gz);
                var buffer = new byte[1 << 16];
                long done = 0;
                var lastPct = -1;
                int n;
                while ((n = await body.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, n);
                    done += n;
                    var pct = total > 0 ? (int)(Math.Min(0.99, (double)done / total.Value) * 100) : -1;
                    if (pct > lastPct) { lastPct = pct; progress?.Report(pct / 100.0); }
                }
                var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new DiagnosticException("The downloaded database failed its checksum, so it was not used. Please try again.");
            }
            progress?.Report(1);
            return await Task.Run(() => ImportFromFile(gz, db, folder), ct).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(gz);
            if (ownClient) http.Dispose();
        }
    }

    /// <summary>Imports from a bmw.sqlite or bmw.sqlite.gz the user downloaded by hand.</summary>
    public static int ImportFromFile(string path, VehicleDatabase db, string? folder = null)
    {
        folder ??= FaultDataDownload.DefaultFolder;
        if (!path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) return ImportTables(path, db, folder);
        var sqlite = Path.Combine(Path.GetTempPath(), $"klemme15-{Guid.NewGuid():N}.sqlite");
        try
        {
            using (var input = new GZipStream(File.OpenRead(path), CompressionMode.Decompress))
            using (var output = File.Create(sqlite))
                input.CopyTo(output);
            return ImportTables(sqlite, db, folder);
        }
        finally { TryDelete(sqlite); }
    }

    /// <summary>Writes tables/&lt;variant&gt;.json for every supported variant that has a measurement table.</summary>
    public static int ImportTables(string sqlitePath, VehicleDatabase db, string folder)
    {
        var wanted = new HashSet<string>(db.Modules.SelectMany(m => m.Variants), StringComparer.OrdinalIgnoreCase);
        var variantsDir = Path.Combine(folder, "variants");
        if (Directory.Exists(variantsDir))
            foreach (var f in Directory.EnumerateFiles(variantsDir, "*.json")) wanted.Add(Path.GetFileNameWithoutExtension(f));

        var staging = Path.Combine(folder, "tables.new");
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);

        var count = 0;
        using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sqlitePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            conn.Open();
            foreach (var variant in wanted.Order(StringComparer.OrdinalIgnoreCase))
            {
                var measurements = ReadMeasurements(conn, variant.ToLowerInvariant());
                if (measurements.Count == 0) continue;
                // Never trust names: variant ids are plain file names.
                if (variant.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || variant.Contains("..", StringComparison.Ordinal)) continue;
                File.WriteAllText(Path.Combine(staging, variant.ToLowerInvariant() + ".json"),
                    JsonSerializer.Serialize(new { source = "bmw-unified-db (MESSWERTETAB)", measurements }));
                count++;
            }
        }
        SqliteConnection.ClearAllPools();

        var target = Path.Combine(folder, "tables");
        if (Directory.Exists(target)) Directory.Delete(target, true);
        Directory.Move(staging, target);
        return count;
    }

    private static List<object> ReadMeasurements(SqliteConnection conn, string variant)
    {
        var result = new List<object>();
        string? columnsJson;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT columns FROM ecu_table WHERE sgbd = $s AND name = 'MESSWERTETAB'";
            cmd.Parameters.AddWithValue("$s", variant);
            columnsJson = cmd.ExecuteScalar() as string;
        }
        if (columnsJson is null) return result;
        var columns = JsonSerializer.Deserialize<string[]>(columnsJson) ?? [];
        int Col(params string[] names) => Array.FindIndex(columns, c => names.Any(n => c.Equals(n, StringComparison.OrdinalIgnoreCase)));
        int id = Col("ID"), name = Col("RESULTNAME"), comment = Col("KOMMENTAR", "INFO"), unit = Col("EINHEIT"), type = Col("DATENTYP"),
            lh = Col("L/H"), mul = Col("MUL"), div = Col("DIV"), add = Col("ADD");
        if (id < 0 || name < 0) return result;

        using var rows = conn.CreateCommand();
        rows.CommandText = "SELECT cells FROM table_row WHERE sgbd = $s AND \"table\" = 'MESSWERTETAB' ORDER BY idx";
        rows.Parameters.AddWithValue("$s", variant);
        using var reader = rows.ExecuteReader();
        while (reader.Read())
        {
            var cells = JsonSerializer.Deserialize<string?[]>(reader.GetString(0)) ?? [];
            string? Cell(int i) => i >= 0 && i < cells.Length && cells[i] is { } v && v != "-" ? v.Trim() : null;
            if (Cell(id) is not { } idText || Cell(name) is not { } resultName) continue;
            result.Add(new
            {
                id = idText,
                result = resultName,
                comment = Cell(comment),
                unit = Cell(unit),
                type = Cell(type) ?? "-",
                lh = Cell(lh)?.StartsWith("low", StringComparison.OrdinalIgnoreCase) == true,
                mul = MeasurementTables.ParseBmwNumber(Cell(mul)) ?? 1,
                div = MeasurementTables.ParseBmwNumber(Cell(div)) ?? 1,
                add = MeasurementTables.ParseBmwNumber(Cell(add)) ?? 0,
            });
        }
        return result;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
