namespace Klemme15;

/// <summary>A folder of SGBD files found on this computer.</summary>
public sealed record SgbdFolder(string Path, int PrgCount, string Origin);

/// <summary>
/// Finds the user's own EDIABAS SGBD files (from INPA, Tool32 or ISTA installs) so EDIABAS mode works
/// without setup. Only looks; never copies or uploads anything.
/// </summary>
public static class EdiabasLocator
{
    private static readonly string[] RootNames = ["EDIABAS", "ISTA", "Rheingold", "INPA", "BMW", "ISPI", "Standard Tools"];

    public static SgbdFolder? Find()
    {
        var candidates = new List<(string Path, string Origin)>();

        foreach (var variable in new[] { "EDIABAS_PATH", "EDIABAS" })
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } env)
                candidates.Add((System.IO.Path.Combine(env, "Ecu"), $"%{variable}%"));

        foreach (var drive in SafeDrives())
        {
            candidates.Add((System.IO.Path.Combine(drive, "EDIABAS", "Ecu"), "EDIABAS"));
            foreach (var dir in SafeDirs(drive))
            {
                var name = System.IO.Path.GetFileName(dir);
                if (RootNames.Any(r => name.Contains(r, StringComparison.OrdinalIgnoreCase)))
                    candidates.AddRange(EcuFoldersUnder(dir, depth: 4).Select(p => (p, name)));
            }
        }
        foreach (var programFiles in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var root = Environment.GetFolderPath(programFiles);
            if (string.IsNullOrEmpty(root)) continue;
            foreach (var dir in SafeDirs(root).Where(d => RootNames.Any(r => System.IO.Path.GetFileName(d).Contains(r, StringComparison.OrdinalIgnoreCase))))
                candidates.AddRange(EcuFoldersUnder(dir, depth: 5).Select(p => (p, System.IO.Path.GetFileName(dir))));
        }

        return candidates
            .DistinctBy(c => c.Path.ToUpperInvariant())
            .Select(c => new SgbdFolder(c.Path, CountPrg(c.Path), c.Origin))
            .Where(f => f.PrgCount >= 20)
            .OrderByDescending(f => f.PrgCount)
            .FirstOrDefault();
    }

    /// <summary>Number of SGBD files in a folder (0 if it doesn't exist).</summary>
    public static int CountPrg(string path)
    {
        try { return Directory.Exists(path) ? Directory.EnumerateFiles(path, "*.prg").Count() : 0; }
        catch (Exception) { return 0; }
    }

    private static IEnumerable<string> EcuFoldersUnder(string dir, int depth)
    {
        if (depth < 0) yield break;
        foreach (var sub in SafeDirs(dir))
        {
            if (System.IO.Path.GetFileName(sub).Equals("Ecu", StringComparison.OrdinalIgnoreCase)) yield return sub;
            else foreach (var deeper in EcuFoldersUnder(sub, depth - 1)) yield return deeper;
        }
    }

    private static IEnumerable<string> SafeDrives()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => d.RootDirectory.FullName)
                .ToList();
        }
        catch (Exception) { return []; }
    }

    private static IEnumerable<string> SafeDirs(string path)
    {
        try { return Directory.EnumerateDirectories(path).ToList(); }
        catch (Exception) { return []; }
    }
}
