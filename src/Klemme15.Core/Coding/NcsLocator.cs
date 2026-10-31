namespace Klemme15.Core.Coding;

/// <summary>
/// Finds the user's own NCS Expert install (from BMW Standard Tools) so quick codes work without setup: the DATEN folder
/// with the module files, and the WORK folder where NCS Expert saves its latest reading of a module (FSW_PSW.TRC).
/// Only looks; never copies anything.
/// </summary>
public static class NcsLocator
{
    public sealed record NcsInstall(string Root, string Daten, string Work);

    private static readonly string[] Roots = ["NCSEXPER", Path.Combine("EC-APPS", "NCSEXPER"), Path.Combine("EDIABAS", "NCSEXPER"), Path.Combine("BMW", "NCSEXPER")];

    public static NcsInstall? Find()
    {
        foreach (var root in Candidates())
        {
            var daten = Path.Combine(root, "DATEN");
            if (!Directory.Exists(daten)) continue;
            return new NcsInstall(root, daten, Path.Combine(root, "WORK"));
        }
        return null;
    }

    /// <summary>The install a DATEN folder belongs to (its parent), if that looks like NCS Expert.</summary>
    public static NcsInstall? FromDaten(string? daten)
    {
        if (string.IsNullOrWhiteSpace(daten) || !Directory.Exists(daten)) return null;
        var root = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(daten));
        // The user may have picked a chassis folder (DATEN\E89): walk up to DATEN.
        for (var dir = daten; dir is not null && root is not null; dir = Path.GetDirectoryName(dir))
            if (Path.GetFileName(dir).Equals("DATEN", StringComparison.OrdinalIgnoreCase))
            {
                var r = Path.GetDirectoryName(dir)!;
                return new NcsInstall(r, dir, Path.Combine(r, "WORK"));
            }
        return root is null ? null : new NcsInstall(root, daten, Path.Combine(root, "WORK"));
    }

    /// <summary>NCS Expert's latest reading of a module, if there is one: (path, when it was written).</summary>
    public static (string Path, DateTime Written)? LatestTrace(NcsInstall install)
    {
        if (!Directory.Exists(install.Work)) return null;
        var file = Directory.EnumerateFiles(install.Work)
            .Where(f => Path.GetFileName(f).Equals("FSW_PSW.TRC", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        return file is null ? null : (file, File.GetLastWriteTime(file));
    }

    private static IEnumerable<string> Candidates()
    {
        if (Environment.GetEnvironmentVariable("NCSEXPER") is { Length: > 0 } env) yield return env;
        foreach (var drive in SafeDrives())
            foreach (var r in Roots) yield return Path.Combine(drive, r);
        foreach (var pf in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var p = Environment.GetFolderPath(pf);
            if (!string.IsNullOrEmpty(p)) yield return Path.Combine(p, "NCSEXPER");
        }
    }

    private static IEnumerable<string> SafeDrives()
    {
        string[] drives;
        try { drives = DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed).Select(d => d.RootDirectory.FullName).ToArray(); }
        catch (Exception) { drives = []; }
        return drives;
    }
}
