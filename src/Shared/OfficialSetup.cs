using Klemme15.Core;
using Klemme15.Data;

namespace Klemme15;

/// <summary>What official fault data this computer has, and where it came from.</summary>
public sealed record OfficialStatus(SgbdFolderSource? BmwFiles, UnifiedDbSource? Downloaded, FaultDataDownload.Manifest? Manifest, MeasurementTables? Tables = null)
{
    public bool Any => BmwFiles is not null || Downloaded is not null;

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (BmwFiles is not null) parts.Add($"your BMW files ({BmwFiles.VariantCount} modules, {BmwFiles.Folder})");
            if (Downloaded is not null)
                parts.Add($"bmw-unified-db {Manifest?.Release ?? ""} ({Downloaded.VariantCount} modules, downloaded {Manifest?.Downloaded.ToString("yyyy-MM-dd") ?? "earlier"})".Replace("  ", " "));
            var tables = Tables is { Count: > 0 } t ? $" Engine readings without BMW files: {t.Count} engine computers." : "";
            return parts.Count == 0
                ? "Not set up: BMW codes use Klemme15's researched meanings only." + tables
                : "Official BMW fault texts from " + string.Join(" and ", parts) + "." + tables;
        }
    }
}

/// <summary>Connects the official fault dictionaries the user has to the database: their own BMW files first, then the download.</summary>
public static class OfficialSetup
{
    /// <param name="ecuFolder">The user's EDIABAS Ecu folder; null to auto-detect ISTA/INPA/EDIABAS installs.</param>
    public static OfficialStatus Apply(VehicleDatabase db, string? ecuFolder = null, bool autoDetect = true)
    {
        var folder = ecuFolder ?? (autoDetect ? EdiabasLocator.Find()?.Path : null);
        var files = folder is not null && Directory.Exists(folder) ? new SgbdFolderSource(folder) : null;
        if (files is { VariantCount: 0 }) files = null;

        var downloaded = new UnifiedDbSource(FaultDataDownload.DefaultFolder);
        var dl = downloaded.VariantCount > 0 ? downloaded : null;

        db.SetOfficialSources(new IOfficialFaultSource?[] { files, dl }.OfType<IOfficialFaultSource>());
        var tables = new MeasurementTables(FaultDataDownload.DefaultFolder);
        db.Measurements = tables.Count > 0 ? tables : null;
        return new OfficialStatus(files, dl, dl is null ? null : FaultDataDownload.ReadManifest(), db.Measurements as MeasurementTables);
    }
}
