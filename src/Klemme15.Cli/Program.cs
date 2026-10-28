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
          readings --module <id>        Read every status value a module offers (needs --ediabas, or --demo)
                   [--filter lambda] [--job STATUS_X]
          clear   --module <id> [--yes] Clear one module's fault memory
          test                          List guided tests (fuel trims, VANOS, cylinder roughness)
          test <id>                     Run one: measures while telling you what to do, then explains the result
          service                       List service functions (battery registration, calibrations, tests)
          service <id> [--experimental] Run one, e.g. 'service battery-registration --port COM3 --experimental'
                                        Choices as options: 'service service-reset --item oil', 'service cylinder-cutout --cyl 3'
                                        (checks the car, asks you to confirm each step; --yes skips the questions)
          live    [--pids rpm,coolant]  Stream live engine data (Ctrl+C to stop)
          serve   [--http 8080]         Headless telemetry: live data as JSON over HTTP (and MQTT)
                  [--mqtt host[:port]]  for a Raspberry Pi, Grafana or a home dashboard
                  [--pids ...] [--interval 1] [--mqtt-topic klemme15] [--mqtt-user u --mqtt-pass p]
          discover                      Probe every other address for modules the database doesn't list
          jobs    <sgbd>                List EDIABAS jobs in an SGBD (EDIABAS mode)
          job     <sgbd> <job> [args]   Run any EDIABAS job (EDIABAS mode)
          db status                     Show which official BMW fault texts are available
          db download                   Download the optional BMW fault database (bmw-unified-db, ~13 MB)
          db download-full              Also add engine readings without BMW files (~130 MB once, keeps a few MB)
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
          --forum            After a scan, print a short summary for a forum post (VIN removed)
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
                    if (pct / 10 > last / 10) { last = pct; Console.Error.Write($" {pct}%"); }
                }));
                Console.Error.WriteLine();
                Console.WriteLine($"Installed {m.Variants} module dictionaries ({m.Release ?? "latest"}, sha256 {m.Sha256[..12]}...) in {FaultDataDownload.DefaultFolder}");
                return 0;
            case "install":
                if (a.Positional.Count < 2) { Console.Error.WriteLine("usage: klemme15 db install <json.tar.gz | bmw.sqlite.gz>"); return 2; }
                if (a.Positional[1].Contains(".sqlite", StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine($"Added measurement tables for {Klemme15.DataImport.FullDatabaseImport.ImportFromFile(a.Positional[1], db)} engine computers.");
                else
                    Console.WriteLine($"Installed {FaultDataDownload.InstallFromFile(a.Positional[1], db)} module dictionaries in {FaultDataDownload.DefaultFolder}");
                return 0;
            case "download-full":
            {
                if (FaultDataDownload.ReadManifest() is null)
                {
                    Console.WriteLine("Downloading the fault database first ...");
                    await FaultDataDownload.DownloadAsync(db);
                }
                Console.WriteLine($"Downloading the full database ({Klemme15.DataImport.FullDatabaseImport.ApproxSize}) to extract engine measurement tables ...");
                var shown = -1;
                var n = await Klemme15.DataImport.FullDatabaseImport.DownloadAsync(db, progress: new Progress<double>(p =>
                {
                    var pct = (int)(p * 100);
                    if (pct / 10 > shown / 10) { shown = pct; Console.Error.Write($" {pct}%"); }
                }));
                Console.Error.WriteLine();
                Console.WriteLine($"Added measurement tables for {n} engine computers; the large download was deleted again.");
                return 0;
            }
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
        if (db.Guides.For(f, ecu) is { } guide)
        {
            Console.WriteLine($"  troubleshooting: {guide.Title}");
            var n = 0;
            foreach (var step in guide.StepsFor(model?.EngineCode)) Console.WriteLine($"    {++n}. {step.Text}");
        }
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
            case "test" when a.Positional.Count == 0:
                foreach (var t in GuidedTests.All) Console.WriteLine($"  {t.Id,-12} {t.Title}\n               {t.Requirements}");
                return 0;
            case "service" when a.Positional.Count == 0:
                return ListService(db);
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
                "readings" => await Readings(backend, db, model, settings.Demo ? new Klemme15.Protocols.Simulation.DemoReadings(model) : backend as IModuleReadings, Module(vehicle, a), a, cts.Token),
                "test" => await GuidedTest(backend, db, vehicle, model, settings.Demo, log, a, cts.Token),
                "service" => await Service(backend, db, vehicle, model, settings.Demo, log, a, cts.Token),
                "clear" => await Clear(backend, Module(vehicle, a), a.Has("yes"), cts.Token),
                "live" => await Live(backend, vehicle, a, cts.Token),
                "serve" => await Serve(backend, vehicle, log, a, cts.Token),
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
            var faults = r.Faults.Count > 0 ? r.Faults.Count + " fault(s)" : "";
            Console.WriteLine($"  [{mark}] {r.Ecu.Name,-34} {r.Ecu.AddressHex}  {faults,-10} {r.Identity?.Summary} {r.Error}".TrimEnd());
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
        if (a.Has("forum"))
        {
            Console.WriteLine();
            Console.WriteLine("----- forum post (VIN removed), copy from here -----");
            Console.WriteLine(ReportWriter.ToForumPost(scan));
        }
        return 0;
    }

    /// <summary>
    /// BMW's measurement values for the engine: from BMW's files in EDIABAS mode, otherwise read by Klemme15 itself
    /// with the downloaded tables, for the engine computer version its part number identifies.
    /// </summary>
    private static async Task<IMeasurementReader?> MeasurementsFor(IDiagnosticBackend backend, VehicleDatabase db, VehicleFamily vehicle,
        VehicleModel? model, TrafficLog log, CancellationToken ct)
    {
        if (backend is IMeasurementReader own) return own;
        if (backend is not IRawRequests raw || db.Measurements is not { } tables) return null;
        var engine = vehicle.Modules.FirstOrDefault(m => m.IsEngine);
        if (engine is null) return null;
        var identity = await backend.IdentifyAsync(engine, ct);
        return new Klemme15.Protocols.NativeReadings(raw, tables,
            e => OfficialLookup.ResolveVariant(db.OfficialSources, e, identity, model?.EngineCode, v => tables.Has(v)), log);
    }

    /// <summary>klemme15 readings --module DME [--job STATUS_X] [--filter lambda]: BMW status values, read-only.</summary>
    private static async Task<int> GuidedTest(IDiagnosticBackend backend, VehicleDatabase db, VehicleFamily vehicle, VehicleModel? model, bool demo,
        TrafficLog log, Args a, CancellationToken ct)
    {
        var id = a.Positional.ElementAtOrDefault(0);
        var info = GuidedTests.All.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (info is null) { Console.Error.WriteLine($"Unknown test '{id}'. See 'klemme15 test'."); return 2; }
        var engine = vehicle.Modules.First(m => m.IsEngine);
        var measurements = demo ? new Klemme15.Protocols.Simulation.DemoReadings(model) : await MeasurementsFor(backend, db, vehicle, model, log, ct);
        ISignalSource signals = demo ? new Klemme15.Protocols.Simulation.DemoSignals(measurements!, engine) : new EngineSignals(backend, engine, measurements);
        Console.WriteLine();
        Console.WriteLine(info.Title);
        Console.WriteLine($"  {info.Requirements}");
        var last = "";
        var progress = new Progress<TestProgress>(p =>
        {
            if (p.Instruction != last) { Console.WriteLine(); Console.WriteLine($">> {p.Instruction}"); last = p.Instruction; }
            Console.Write($"\r   {p.Status}".PadRight(60));
        });
        var report = await GuidedTests.RunAsync(info.Id, signals, progress, ct, demo ? 8 : 1);
        Console.WriteLine();
        Console.WriteLine();
        foreach (var v in report.Values) Console.WriteLine($"  {v.Key,-28} {v.Value,8:0.0} {report.Unit}");
        foreach (var f in report.Findings)
        {
            Console.WriteLine();
            Console.WriteLine($"[{f.Severity}] {f.Title}");
            Console.WriteLine($"  {f.Explanation}");
        }
        return report.Findings.Any(f => f.Severity == TestSeverity.Problem) ? 3 : 0;
    }

    private static int ListService(VehicleDatabase db)
    {
        foreach (var group in db.Service.All.GroupBy(f => f.Category))
        {
            Console.WriteLine(group.Key);
            foreach (var f in group)
                Console.WriteLine($"  {f.Id,-30} {f.Title}{(f.Verified ? "" : "  [experimental]")}");
            Console.WriteLine();
        }
        Console.WriteLine("Run one with: klemme15 service <id> --port <cable port> [--ediabas <BMW Ecu folder>] [--experimental]");
        return 0;
    }

    /// <summary>klemme15 service ID: shows the function, checks the car, asks for every confirmation, runs it once.</summary>
    private static async Task<int> Service(IDiagnosticBackend backend, VehicleDatabase db, VehicleFamily vehicle, VehicleModel? model, bool demo,
        TrafficLog log, Args a, CancellationToken ct)
    {
        var id = a.Positional.ElementAtOrDefault(0);
        var f = db.Service.All.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (f is null) { Console.Error.WriteLine($"Unknown service function '{id}'. See 'klemme15 service'."); return 2; }

        Console.WriteLine();
        Console.WriteLine(f.Title);
        Console.WriteLine($"  {f.Summary}");
        Console.WriteLine($"  When: {f.When}");
        Console.WriteLine($"  Risk: {f.Risk}{(f.Verified ? "" : ", EXPERIMENTAL (not yet confirmed on a real car)")}");
        foreach (var b in f.Before) Console.WriteLine($"  Before: {b}");
        if (!ServiceRunner.Allowed(f, a.Has("experimental")))
        {
            Console.Error.WriteLine("This function is experimental. Add --experimental to run it at your own risk.");
            return 2;
        }

        var ecu = ServiceRunner.ModuleFor(f, vehicle.Modules);
        var identity = ecu is null ? null : await backend.IdentifyAsync(ecu, ct);
        string? variant = identity?.Variant
            ?? (ecu is not null && backend is IModuleReadings r && backend is IJobRunner ? r.VariantOf(ecu) : null)
            ?? (ecu is null ? null : OfficialLookup.ResolveVariant(db.OfficialSources, ecu, identity, model?.EngineCode,
                v => f.Implementations.Any(i => i.Variants.Contains(v.ToLowerInvariant()))));
        var plan = ServiceRunner.Availability(f, ecu, variant, identity is not null, backend is IJobRunner, backend is IRawRequests, demo);
        Console.WriteLine();
        Console.WriteLine(plan.Reason);
        if (!plan.Available) return 1;

        // Choices: --item oil, --cyl 3, --code ABC1234, or pick from a list.
        var values = new Dictionary<string, string>();
        foreach (var input in f.Inputs)
        {
            var offered = input.Choices.Where(c => plan.Mode == ServiceMode.Demo || plan.Implementation!.Supports(input.Id, c.Value)).ToList();
            var given = a.Get(input.Id);
            if (given is null && a.Has("yes")) { Console.Error.WriteLine($"Pass --{input.Id} <value> ({input.Label})."); return 2; }
            if (given is null && input.IsChoice)
            {
                Console.WriteLine($"  {input.Label}:");
                for (var i = 0; i < offered.Count; i++) Console.WriteLine($"    {i + 1}. {offered[i].Label} ({offered[i].Value})");
                Console.Write("  Number: ");
                given = int.TryParse(Console.ReadLine(), out var n) && n >= 1 && n <= offered.Count ? offered[n - 1].Value : null;
            }
            else if (given is null)
            {
                Console.Write($"  {input.Label}{(input.Hint is null ? "" : $" ({input.Hint})")}: ");
                given = Console.ReadLine();
            }
            if (string.IsNullOrWhiteSpace(given)) { Console.WriteLine("Not run."); return 1; }
            values[input.Id] = given.Trim();
        }
        if (ServiceRunner.ValidateInputs(f, plan.Mode == ServiceMode.Demo ? null : plan.Implementation, values) is { } problem)
        {
            Console.Error.WriteLine(problem);
            return 2;
        }

        var state = demo ? ServiceRunner.DemoState(f.Checks) : await ServiceRunner.ReadStateAsync(backend, vehicle.Modules.FirstOrDefault(m => m.IsEngine),
                await MeasurementsFor(backend, db, vehicle, model, log, ct), ct);
        var ok = true;
        foreach (var c in ServiceRunner.Evaluate(f.Checks, state))
        {
            Console.WriteLine($"  [{c.State switch { CheckState.Pass => "OK", CheckState.Fail => "NO", _ => "??" }}] {c.Text}{(c.Detail is null ? "" : $": {c.Detail}")}");
            if (c.State == CheckState.Fail) ok = false;
            else if (c.State == CheckState.Unknown && !Confirm($"Confirm: {c.Text}?", a)) ok = false;
        }
        foreach (var item in f.Checks.Confirm)
            if (!Confirm(item, a)) ok = false;
        if (!ok) { Console.WriteLine("Not run: a requirement isn't met."); return 1; }
        if (!Confirm($"Run '{f.Title}' on {ecu!.Name} now?", a)) { Console.WriteLine("Not run."); return 1; }

        var progress = new Progress<string>(text => Console.Write($"\r  {text}".PadRight(70)));
        var outcome = await ServiceRunner.RunAsync(f, plan, backend, ecu, values, progress, log, ct);
        Console.WriteLine();
        Console.WriteLine(outcome.Success ? outcome.Message : $"Failed: {outcome.Message}");
        foreach (var d in outcome.Details) Console.WriteLine($"  {d}");
        if (outcome.Success) foreach (var s in f.After) Console.WriteLine($"Afterwards: {s}");
        return outcome.Success ? 0 : 1;
    }

    private static bool Confirm(string question, Args a)
    {
        if (a.Has("yes")) { Console.WriteLine($"  {question} yes (--yes)"); return true; }
        Console.Write($"  {question} [y/N] ");
        var answer = Console.ReadLine()?.Trim();
        return answer is not null && (answer.Equals("y", StringComparison.OrdinalIgnoreCase) || answer.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<int> Readings(IDiagnosticBackend backend, VehicleDatabase db, VehicleModel? model, IModuleReadings? readings, EcuDefinition ecu, Args a, CancellationToken ct)
    {
        var identity = await backend.IdentifyAsync(ecu, ct);
        if (identity is null) { Console.Error.WriteLine($"{ecu.Name} did not answer."); return 1; }
        // Without BMW files: Klemme15 reads engine values itself from the downloaded measurement tables.
        readings ??= backend is IRawRequests raw && db.Measurements is { } tables
            ? new Klemme15.Protocols.NativeReadings(raw, tables,
                e => OfficialLookup.ResolveVariant(db.OfficialSources, e, identity, model?.EngineCode, v => tables.Has(v)))
            : null;
        if (readings is null || !readings.Supports(ecu))
        {
            Console.Error.WriteLine("No readings for this module yet. Engine readings without BMW files: run 'klemme15 db download-full'. Every module: add --ediabas <your BMW Ecu folder>.");
            return 2;
        }
        var list = await readings.ListReadingsAsync(ecu, ct);
        var filter = a.Get("filter");
        var jobs = list.Where(r => a.Get("job") is { } j ? r.Job.Equals(j, StringComparison.OrdinalIgnoreCase)
                : filter is null || ReadingLabels.JobTitle(r.Job, r.Comment).Contains(filter, StringComparison.OrdinalIgnoreCase)
                  || r.Job.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        Console.WriteLine($"{ecu.Name}, module version {readings.VariantOf(ecu) ?? "?"}: {list.Count} readings{(jobs.Count != list.Count ? $", showing {jobs.Count}" : "")}");
        foreach (var group in jobs.GroupBy(r => ReadingLabels.GroupOf(r.Job, r.Comment)).OrderBy(g => ReadingLabels.GroupOrder.ToList().IndexOf(g.Key)))
        {
            Console.WriteLine();
            Console.WriteLine($"== {group.Key}");
            foreach (var r in group)
            {
                Console.WriteLine($"  {ReadingLabels.JobTitle(r.Job, r.Comment)}  [{r.Job}]");
                try
                {
                    foreach (var v in await readings.ReadAsync(ecu, r.Job, ct))
                        Console.WriteLine($"      {ReadingLabels.ResultLabel(v.Name, v.Comment),-48} {(v.Number is null ? ReadingLabels.ValueText(v.Text) : v.Text)} {v.Unit}".TrimEnd());
                }
                catch (DiagnosticException e) { Console.WriteLine($"      ({e.Message})"); }
            }
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
        foreach (var f in faults.OrderBy(f => FaultTriage.Assess(f).Priority))
        {
            var v = FaultTriage.Assess(f);
            Console.WriteLine($"  {f.Code,-6} {(f.IsShadow ? "Shadow" : f.IsInfo ? "Info" : f.State.ToString()),-12} {v.Label,-17} {f.DisplayDescription}");
            Console.WriteLine($"         {v.Advice}");
        }
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

    private static async Task<int> Serve(IDiagnosticBackend backend, VehicleFamily vehicle, TrafficLog log, Args a, CancellationToken ct)
    {
        var engine = vehicle.Modules.First(m => m.IsEngine);
        var available = backend.GetLiveParameters(engine);
        var wanted = (a.Get("pids") ?? "rpm,speed,coolant,iat,load,throttle,stft1,ltft1,maf,voltage").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var parameters = available.Where(p => wanted.Contains(p.Id, StringComparer.OrdinalIgnoreCase)).ToList();
        if (parameters.Count == 0)
        {
            Console.Error.WriteLine($"None of those values are available. Available: {string.Join(", ", available.Select(p => p.Id))}");
            return 2;
        }
        string? mqttHost = null; var mqttPort = 1883;
        if (a.Get("mqtt") is { } m)
        {
            var parts = m.Split(':', 2);
            mqttHost = parts[0];
            if (parts.Length > 1 && int.TryParse(parts[1], out var mp)) mqttPort = mp;
        }
        var options = new Klemme15.Telemetry.TelemetryOptions(
            HttpPort: int.TryParse(a.Get("http"), out var hp) ? hp : 8080,
            MqttHost: mqttHost, MqttPort: mqttPort,
            MqttTopic: a.Get("mqtt-topic") ?? "klemme15", MqttUser: a.Get("mqtt-user"), MqttPass: a.Get("mqtt-pass"),
            IntervalSeconds: double.TryParse(a.Get("interval"), NumberStyles.Float, CultureInfo.InvariantCulture, out var iv) ? iv : 1);

        Console.WriteLine($"Serving {parameters.Count} live values on http://localhost:{options.HttpPort}/  (Ctrl+C to stop)");
        Console.WriteLine($"  JSON: http://localhost:{options.HttpPort}/live.json   faults: /faults   health: /health");
        if (mqttHost is not null) Console.WriteLine($"  MQTT: publishing to {mqttHost}:{mqttPort} under topic '{options.MqttTopic}/<value>'");
        var server = new Klemme15.Telemetry.TelemetryServer(backend, engine, parameters, options, log);
        try { await server.RunAsync(ct); }
        catch (OperationCanceledException) { Console.WriteLine("Stopped."); }
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

    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase) { "demo", "yes", "trust-dsr", "experimental", "forum" };

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
