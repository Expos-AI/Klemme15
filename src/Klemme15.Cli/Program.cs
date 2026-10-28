using System.Globalization;
using Klemme15;
using Klemme15.Core;
using Klemme15.Data;
using Klemme15.Serial;

return await Cli.RunAsync(args);

internal static class Cli
{
    private const string Usage = """
        Klemme15 - open-source BMW E-series diagnostics

        Usage: klemme15 <command> [options]

        Commands
          ports                         List serial ports (K+DCAN cables are marked)
          vehicles                      List supported vehicles and their modules
          scan                          Quick test: identify every module and read faults
          faults  --module <id>         Read one module's fault memory
          clear   --module <id> [--yes] Clear one module's fault memory
          live    [--pids rpm,coolant]  Stream live engine data (Ctrl+C to stop)
          discover                      Probe every other address for modules the database doesn't list
          jobs    <sgbd>                List EDIABAS jobs in an SGBD (EDIABAS mode)
          job     <sgbd> <job> [args]   Run any EDIABAS job (EDIABAS mode)
          db status                     Show which official BMW fault texts are available
          db download                   Download the optional BMW fault database (bmw-unified-db, ~13 MB)
          db install <json.tar.gz>      Install that database from a file you downloaded yourself
          code <module> <code>          Look up what a code means, e.g. 'code E9x.DSC 5E1A --model 325i'
          sgbd <file.prg> [TABLE]       List the tables in one of your BMW files, or print one

        Options
          --vehicle <id>     E46 or E9x (default E9x)
          --model <name>     e.g. 325i or 335i (picks the demo car; see 'klemme15 vehicles')
          --port <name>      Cable port, e.g. COM3 or /dev/ttyUSB0
          --demo             Use the built-in virtual car instead of a cable
          --ediabas <dir>    Use your own SGBD files from this folder (Windows build)
          --ecu <dir>        Read official fault texts from this BMW Ecu folder (any OS; auto-detected on Windows)
          --trust-dsr        Your cable reports ignition on DSR; refuse to run with ignition off
          --report <file>    Save the scan as .html, .md or .json
          --csv <file>       Log live data to CSV
          --log <file>       Save the raw cable traffic (attach this to bug reports)

        Examples
          klemme15 scan --vehicle E9x --port COM3 --report scan.html
          klemme15 live --vehicle E46 --port /dev/ttyUSB0 --pids rpm,coolant,stft1,ltft1
          klemme15 scan --demo --vehicle E9x --model 335i
        """;

    /// <summary>klemme15 sgbd FILE.prg [TABLE]: lists the tables in a local EDIABAS file, or prints one as TSV.</summary>
    private static int DumpSgbd(Args a)
    {
        if (a.Positional.Count == 0) { Console.Error.WriteLine("usage: klemme15 sgbd <file.prg|file.grp> [TABLE]"); return 2; }
        var f = SgbdFile.Open(a.Positional[0]);
        if (a.Positional.Count == 1)
        {
            foreach (var t in f.TableNames.OrderBy(n => n)) Console.WriteLine($"{t,-32} {f.ReadTable(t)!.Count} rows");
            return 0;
        }
        var rows = f.ReadTable(a.Positional[1]);
        if (rows is null) { Console.Error.WriteLine($"No table {a.Positional[1]} in {f.Name}."); return 1; }
        if (rows.Count == 0) return 0;
        Console.WriteLine(string.Join('\t', rows[0].Keys));
        foreach (var r in rows) Console.WriteLine(string.Join('\t', r.Values));
        return 0;
    }

    private static async Task<int> Database(VehicleDatabase db, Args a)
    {
        switch (a.Positional.FirstOrDefault() ?? "status")
        {
            case "status":
                Console.WriteLine(OfficialSetup.Apply(db, a.Get("ecu")).Summary);
                Console.WriteLine($"Optional database: {FaultDataDownload.ProjectUrl} (third-party; BMW-derived data, see {FaultDataDownload.LicenseUrl})");
                return 0;
            case "download":
                Console.WriteLine($"Downloading {FaultDataDownload.AssetName} from {FaultDataDownload.ProjectUrl} ...");
                var last = -1;
                var m = await FaultDataDownload.DownloadAsync(db, progress: new Progress<double>(p =>
                {
                    var pct = (int)(p * 100);
                    if (pct / 10 != last / 10) { last = pct; Console.Error.Write($"\r{pct,3}%"); }
                }));
                Console.Error.WriteLine();
                Console.WriteLine($"Installed {m.Variants} module dictionaries ({m.Release ?? "latest"}, sha256 {m.Sha256[..12]}...) in {FaultDataDownload.DefaultFolder}");
                return 0;
            case "install":
                if (a.Positional.Count < 2) { Console.Error.WriteLine("usage: klemme15 db install <json.tar.gz>"); return 2; }
                Console.WriteLine($"Installed {FaultDataDownload.InstallFromFile(a.Positional[1], db)} module dictionaries in {FaultDataDownload.DefaultFolder}");
                return 0;
            default:
                Console.Error.WriteLine("usage: klemme15 db status|download|install <file>");
                return 2;
        }
    }

    /// <summary>klemme15 code E9x.DSC 5E1A [--model 325i] [--variant dsc_87]</summary>
    private static int LookupCode(VehicleDatabase db, Args a)
    {
        if (a.Positional.Count < 2) { Console.Error.WriteLine("usage: klemme15 code <module id> <code> [--model 325i] [--variant name]"); return 2; }
        var ecu = db.Modules.FirstOrDefault(m => m.Id.Equals(a.Positional[0], StringComparison.OrdinalIgnoreCase));
        if (ecu is null) { Console.Error.WriteLine($"Unknown module {a.Positional[0]}. See 'klemme15 vehicles'."); return 1; }
        var model = a.Get("model") is { } mn ? db.FindModel(mn) ?? db.FindVehicle(ecu.Chassis[0])?.Models.FirstOrDefault(x => x.Name.Equals(mn, StringComparison.OrdinalIgnoreCase)) : null;
        var identity = a.Get("variant") is { } v ? new EcuIdentity { Variant = v } : null;
        var code = a.Positional[1].ToUpperInvariant();
        var fault = new Fault { EcuId = ecu.Id, Code = code, Source = code.Length == 5 && char.IsLetter(code[0]) ? FaultSource.Obd2 : FaultSource.Bmw };
        var f = db.Describe(fault, ecu, identity, model?.EngineCode);
        Console.WriteLine($"{ecu.Id} {f.Code}: {f.DisplayDescription}");
        if (f.ModuleVariant is not null) Console.WriteLine($"  module variant: {f.ModuleVariant}{(f.VariantConfirmed ? "" : " (most likely; read the car to confirm)")}");
        if (f.OfficialText is not null && f.OfficialText != f.Description) Console.WriteLine($"  BMW text: {f.OfficialText}");
        foreach (var alt in f.Alternatives) Console.WriteLine($"  on other variants: {alt}");
        if (f.Hint is not null) Console.WriteLine($"  tip: {f.Hint}");
        if (f.OfficialSource is not null) Console.WriteLine($"  source: {f.OfficialSource}");
        foreach (var src in f.Sources) Console.WriteLine($"  community source: {src}");
        return 0;
    }

    public static async Task<int> RunAsync(string[] argv)
    {
        var a = Args.Parse(argv);
        if (a.Command is null or "help" or "--help" or "-h")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        var db = VehicleDatabase.Load();
        foreach (var w in db.LoadWarnings) Console.Error.WriteLine($"warning: {w}");

        switch (a.Command)
        {
            case "ports":
                var ports = PortDiscovery.List();
                if (ports.Count == 0) Console.WriteLine("No serial ports found. Is the cable plugged in and its driver installed?");
                foreach (var p in ports) Console.WriteLine($"{p.Name,-22} {p.Description}{(p.LikelyKDCan ? "   <- likely K+DCAN" : "")}");
                return 0;
            case "vehicles":
                foreach (var v in db.Vehicles)
                {
                    Console.WriteLine($"{v.Id,-5} {v.Name} {v.Years}  (cable switch: {v.Cable})");
                    foreach (var m in v.Models)
                        Console.WriteLine($"      model {m.Name,-7} {m.Years,-10} {m.Engine}, {m.Drive}");
                    foreach (var m in v.Modules)
                        Console.WriteLine($"      {m.Id,-12} {m.AddressHex}  {m.Name}{(m.Verified ? "" : "  [unverified]")}");
                }
                return 0;
            case "sgbd":
                return DumpSgbd(a);
            case "db":
                return await Database(db, a);
        }

        var official = OfficialSetup.Apply(db, a.Get("ecu"));
        if (a.Command is "scan" or "faults" or "code") Console.Error.WriteLine(official.Summary);
        if (a.Command == "code") return LookupCode(db, a);

        var vehicle = db.FindVehicle(a.Get("vehicle") ?? "E9x");
        if (vehicle is null)
        {
            Console.Error.WriteLine($"Unknown vehicle. Known: {string.Join(", ", db.Vehicles.Select(v => v.Id))}");
            return 2;
        }

        var model = a.Get("model") is { } wanted
            ? vehicle.Models.FirstOrDefault(m => m.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase) || m.Id.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            : vehicle.Models.FirstOrDefault(m => m.Name == "325i") ?? vehicle.Models.FirstOrDefault();
        if (a.Get("model") is { } asked && model is null)
        {
            Console.Error.WriteLine($"Unknown model '{asked}' for {vehicle.Id}. Known: {string.Join(", ", vehicle.Models.Select(m => m.Name))}");
            return 2;
        }
        IFaultCodeDatabase codes = a.Has("demo") && model is not null ? db.ForDemo(model) : db.ForEngine(model?.EngineCode);

        var settings = new ConnectionSettings
        {
            VehicleId = vehicle.Id,
            ModelId = model?.Id,
            Port = a.Get("port") ?? (a.Has("demo") ? null : PortDiscovery.List().FirstOrDefault(p => p.LikelyKDCan)?.Name),
            Demo = a.Has("demo"),
            EdiabasPath = a.Get("ediabas"),
            TrustDsr = a.Has("trust-dsr"),
        };
        if (!settings.Demo && settings.Port is null)
        {
            Console.Error.WriteLine("No cable found. Pass --port (see 'klemme15 ports') or try --demo.");
            return 2;
        }

        var log = new TrafficLog();
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            await using var backend = BackendFactory.Create(settings, vehicle, log, model);
            Console.WriteLine($"Connecting: {vehicle.Id} {model?.Name} via {backend.Name}{(settings.Demo ? " [demo car]" : "")}");
            await backend.ConnectAsync(cts.Token);
            if (backend.IgnitionOn is { } ign) Console.WriteLine($"Ignition: {(ign ? "on" : "OFF")}");

            return a.Command switch
            {
                "scan" => await Scan(backend, codes, vehicle, a, cts.Token),
                "faults" => await Faults(backend, codes, Module(vehicle, a), cts.Token),
                "clear" => await Clear(backend, Module(vehicle, a), a.Has("yes"), cts.Token),
                "live" => await Live(backend, vehicle, a, cts.Token),
                "discover" => await Discover(backend, vehicle, cts.Token),
                "jobs" => await Jobs(backend, a, cts.Token),
                "job" => await Job(backend, a, cts.Token),
                _ => Unknown(a.Command),
            };
        }
        catch (DiagnosticException e)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 1;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Stopped.");
            return 130;
        }
        finally
        {
            if (a.Get("log") is { } logFile)
            {
                File.WriteAllText(logFile, log.Export());
                Console.WriteLine($"Traffic log saved to {logFile}");
            }
        }
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'. Run 'klemme15 help'.");
        return 2;
    }

    private static EcuDefinition Module(VehicleFamily vehicle, Args a)
    {
        var id = a.Get("module") ?? vehicle.Modules.First(m => m.IsEngine).Id;
        return vehicle.Modules.FirstOrDefault(m =>
                   m.Id.Equals(id, StringComparison.OrdinalIgnoreCase) ||
                   m.Id.EndsWith("." + id, StringComparison.OrdinalIgnoreCase))
               ?? throw new DiagnosticException($"Unknown module '{id}'. See 'klemme15 vehicles'.");
    }

    private static async Task<int> Scan(IDiagnosticBackend backend, IFaultCodeDatabase db, VehicleFamily vehicle, Args a, CancellationToken ct)
    {
        var scanner = new VehicleScanner(backend, db);
        var progress = new Progress<ScanProgress>(p =>
        {
            if (p.Latest is not { } r) return;
            var mark = r.Status switch
            {
                EcuScanStatus.Ok => "OK   ",
                EcuScanStatus.Faults => "FAULT",
                EcuScanStatus.NoResponse => " -   ",
                EcuScanStatus.Error => "ERROR",
                _ => "     ",
            };
            Console.WriteLine($"  [{mark}] {r.Ecu.Name,-34} {r.Ecu.AddressHex}  {(r.Faults.Count > 0 ? r.Faults.Count + " fault(s)" : "")} {r.Error}");
        });
        var scan = await scanner.ScanAsync(vehicle.Modules, $"{vehicle.Id} {a.Get("model")}".Trim(), progress, ct);
        await Task.Delay(50, CancellationToken.None);   // let the last progress line print

        Console.WriteLine();
        Console.WriteLine($"VIN {scan.Vin ?? "not read"} | {scan.RespondingEcus}/{scan.Ecus.Count} modules answered | {scan.TotalFaults} fault(s) | {scan.Elapsed.TotalSeconds:0.0}s");
        foreach (var e in scan.Ecus.Where(e => e.Faults.Count > 0))
        {
            Console.WriteLine();
            Console.WriteLine($"{e.Ecu.Name}:");
            PrintFaults(e.Faults);
        }
        foreach (var group in scan.Ecus.Where(e => e.Note is not null).GroupBy(e => e.Note))
            Console.WriteLine($"\nnote: {string.Join(", ", group.Select(e => e.Ecu.Id))}: {group.Key}");

        if (a.Get("report") is { } file)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            File.WriteAllText(file, ext switch
            {
                ".json" => ReportWriter.ToJson(scan),
                ".md" => ReportWriter.ToMarkdown(scan),
                _ => ReportWriter.ToHtml(scan),
            });
            Console.WriteLine($"Report saved to {file}");
        }
        return 0;
    }

    private static async Task<int> Faults(IDiagnosticBackend backend, IFaultCodeDatabase db, EcuDefinition ecu, CancellationToken ct)
    {
        var identity = await backend.IdentifyAsync(ecu, ct);
        var faults = new VehicleScanner(backend, db).Describe(await backend.ReadFaultsAsync(ecu, ct), ecu, identity);
        Console.WriteLine($"{ecu.Name}: {faults.Count} fault(s)");
        PrintFaults(faults);
        return 0;
    }

    private static void PrintFaults(IReadOnlyList<Fault> faults)
    {
        foreach (var f in faults)
            Console.WriteLine($"  {f.Code,-6} {f.State,-12} {f.DisplayDescription}");
    }

    private static async Task<int> Clear(IDiagnosticBackend backend, EcuDefinition ecu, bool yes, CancellationToken ct)
    {
        if (!yes)
        {
            Console.Write($"Clear all faults in {ecu.Name}? Readiness monitors reset too. Type YES: ");
            if (Console.ReadLine()?.Trim() != "YES") { Console.WriteLine("Cancelled."); return 0; }
        }
        await backend.ClearFaultsAsync(ecu, ct);
        Console.WriteLine("Fault memory cleared. Read it again after a drive to see what returns.");
        return 0;
    }

    private static async Task<int> Live(IDiagnosticBackend backend, VehicleFamily vehicle, Args a, CancellationToken ct)
    {
        var engine = vehicle.Modules.First(m => m.IsEngine);
        var available = backend.GetLiveParameters(engine);
        var wanted = (a.Get("pids") ?? "rpm,speed,coolant,iat,load,throttle,stft1,ltft1,voltage").Split(',', StringSplitOptions.TrimEntries);
        var parameters = available.Where(p => wanted.Contains(p.Id, StringComparer.OrdinalIgnoreCase)).ToList();
        if (parameters.Count == 0)
        {
            Console.Error.WriteLine($"None of those values are available. Available: {string.Join(", ", available.Select(p => p.Id))}");
            return 2;
        }

        StreamWriter? csv = null;
        if (a.Get("csv") is { } csvFile)
        {
            csv = new StreamWriter(csvFile);
            csv.WriteLine("time," + string.Join(",", parameters.Select(p => $"\"{p.Name} ({p.Unit})\"")));
        }
        Console.WriteLine("Live data, Ctrl+C to stop\n");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var values = await backend.ReadLiveAsync(engine, parameters, ct);
                Console.WriteLine(string.Join(" | ", values.Select(v => $"{v.Parameter.Id} {v.Display}")));
                csv?.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "," +
                               string.Join(",", values.Select(v => v.Value?.ToString(CultureInfo.InvariantCulture) ?? "")));
                csv?.Flush();
                await Task.Delay(100, ct);
            }
        }
        catch (OperationCanceledException) { Console.WriteLine("\nStopped."); }
        finally { csv?.Dispose(); }
        return 0;
    }

    private static async Task<int> Discover(IDiagnosticBackend backend, VehicleFamily vehicle, CancellationToken ct)
    {
        if (backend is not IModuleDiscovery discovery) throw new DiagnosticException("Module discovery needs the native backend (no --ediabas).");
        var addresses = VehicleDatabase.UnlistedAddresses(vehicle);
        Console.WriteLine($"Probing {addresses.Count} addresses the database doesn't list (read-only, about {addresses.Count * 0.15:0} s)...");
        var progress = new Progress<int>(n => { if (n % 32 == 0) Console.WriteLine($"  {n}/{addresses.Count}"); });
        var found = await discovery.DiscoverAsync(addresses, progress, ct);
        Console.WriteLine(found.Count == 0 ? "No other modules answered." : $"{found.Count} other module(s) answered:");
        foreach (var m in found) Console.WriteLine($"  {m.AddressHex}  {m.Identity.Summary}");
        if (found.Count > 0) Console.WriteLine("Please report these (with --log traffic.txt) so they can be added to the database.");
        return 0;
    }

    private static async Task<int> Jobs(IDiagnosticBackend backend, Args a, CancellationToken ct)
    {
        if (backend is not IJobRunner runner) throw new DiagnosticException("Listing jobs needs EDIABAS mode (--ediabas <folder>).");
        var sgbd = a.Positional.ElementAtOrDefault(0) ?? throw new DiagnosticException("Usage: klemme15 jobs <sgbd>");
        foreach (var job in await runner.ListJobsAsync(sgbd, ct)) Console.WriteLine(job);
        return 0;
    }

    private static async Task<int> Job(IDiagnosticBackend backend, Args a, CancellationToken ct)
    {
        if (backend is not IJobRunner runner) throw new DiagnosticException("Running jobs needs EDIABAS mode (--ediabas <folder>).");
        if (a.Positional.Count < 2) throw new DiagnosticException("Usage: klemme15 job <sgbd> <job> [args]");
        var sets = await runner.RunJobAsync(a.Positional[0], a.Positional[1], string.Join(";", a.Positional.Skip(2)), ct);
        for (var i = 0; i < sets.Count; i++)
        {
            Console.WriteLine($"--- set {i}");
            foreach (var kv in sets[i]) Console.WriteLine($"  {kv.Key,-32} {kv.Value}");
        }
        return 0;
    }
}

/// <summary>Tiny argument parser: command, --flags, --key value, and positional values.</summary>
internal sealed class Args
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);
    public string? Command { get; private set; }
    public List<string> Positional { get; } = [];

    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase) { "demo", "yes", "trust-dsr" };

    public static Args Parse(string[] argv)
    {
        var a = new Args();
        for (var i = 0; i < argv.Length; i++)
        {
            var arg = argv[i];
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var key = arg[2..];
                if (Flags.Contains(key) || i + 1 >= argv.Length) a._options[key] = null;
                else a._options[key] = argv[++i];
            }
            else if (a.Command is null) a.Command = arg.ToLowerInvariant();
            else a.Positional.Add(arg);
        }
        return a;
    }

    public bool Has(string key) => _options.ContainsKey(key);
    public string? Get(string key) => _options.TryGetValue(key, out var v) ? v : null;
}
