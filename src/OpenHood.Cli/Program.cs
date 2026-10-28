using System.Globalization;
using OpenHood;
using OpenHood.Core;
using OpenHood.Data;
using OpenHood.Serial;

return await Cli.RunAsync(args);

internal static class Cli
{
    private const string Usage = """
        OpenHood - open-source BMW E-series diagnostics

        Usage: openhood <command> [options]

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

        Options
          --vehicle <id>     E46 or E9x (default E9x)
          --model <name>     e.g. 325i or 335i (picks the demo car; see 'openhood vehicles')
          --port <name>      Cable port, e.g. COM3 or /dev/ttyUSB0
          --demo             Use the built-in virtual car instead of a cable
          --ediabas <dir>    Use your own SGBD files from this folder (Windows build)
          --trust-dsr        Your cable reports ignition on DSR; refuse to run with ignition off
          --report <file>    Save the scan as .html, .md or .json
          --csv <file>       Log live data to CSV
          --log <file>       Save the raw cable traffic (attach this to bug reports)

        Examples
          openhood scan --vehicle E9x --port COM3 --report scan.html
          openhood live --vehicle E46 --port /dev/ttyUSB0 --pids rpm,coolant,stft1,ltft1
          openhood scan --demo --vehicle E9x --model 335i
        """;

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
        }

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
            Console.Error.WriteLine("No cable found. Pass --port (see 'openhood ports') or try --demo.");
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
        Console.Error.WriteLine($"Unknown command '{command}'. Run 'openhood help'.");
        return 2;
    }

    private static EcuDefinition Module(VehicleFamily vehicle, Args a)
    {
        var id = a.Get("module") ?? vehicle.Modules.First(m => m.IsEngine).Id;
        return vehicle.Modules.FirstOrDefault(m =>
                   m.Id.Equals(id, StringComparison.OrdinalIgnoreCase) ||
                   m.Id.EndsWith("." + id, StringComparison.OrdinalIgnoreCase))
               ?? throw new DiagnosticException($"Unknown module '{id}'. See 'openhood vehicles'.");
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
        var faults = new VehicleScanner(backend, db).Describe(await backend.ReadFaultsAsync(ecu, ct));
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
        var sgbd = a.Positional.ElementAtOrDefault(0) ?? throw new DiagnosticException("Usage: openhood jobs <sgbd>");
        foreach (var job in await runner.ListJobsAsync(sgbd, ct)) Console.WriteLine(job);
        return 0;
    }

    private static async Task<int> Job(IDiagnosticBackend backend, Args a, CancellationToken ct)
    {
        if (backend is not IJobRunner runner) throw new DiagnosticException("Running jobs needs EDIABAS mode (--ediabas <folder>).");
        if (a.Positional.Count < 2) throw new DiagnosticException("Usage: openhood job <sgbd> <job> [args]");
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
