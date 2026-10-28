using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Klemme15.Core;

namespace Klemme15.Data;

public enum ServiceRisk { Low, Medium, High }

/// <summary>What Klemme15 checks before a service function may run.</summary>
public sealed record ServiceChecks(bool Ignition, bool EngineOff, bool EngineRunning, double? MinVoltage, bool Stationary, IReadOnlyList<string> Confirm);

/// <summary>How to run a function on some module variants: BMW's job (EDIABAS mode) and/or a documented raw request (native).</summary>
public sealed record ServiceImplementation(IReadOnlyList<string> Variants, string? Job, string Args, string? ResultJob, string ResultArgs, byte[]? Native, byte[]? Expect);

public sealed record ServiceFunction(
    string Id, string Title, string Category, string Module, string Summary, string When, ServiceRisk Risk, bool Verified,
    IReadOnlyList<string> Before, IReadOnlyList<string> After, ServiceChecks Checks, IReadOnlyList<ServiceImplementation> Implementations);

public enum ServiceMode { Ediabas, Native, Demo }

/// <summary>Whether a function can run on this car right now, and how.</summary>
public sealed record ServiceAvailability(bool Available, string Reason, ServiceImplementation? Implementation = null, ServiceMode Mode = ServiceMode.Ediabas, string? Variant = null);

public enum CheckState { Pass, Fail, Unknown }

/// <summary>One requirement and whether it is met. Unknown measurements must be confirmed by the user instead.</summary>
public sealed record CheckResult(string Text, CheckState State, string? Detail = null);

/// <summary>What Klemme15 could measure about the car just before running a function.</summary>
public sealed record VehicleState(double? Voltage, double? Rpm, double? Speed, bool? Ignition);

public sealed record ServiceOutcome(bool Success, string Message, IReadOnlyList<string> Details);

/// <summary>Klemme15's catalog of service functions (db/service.json).</summary>
public sealed class ServiceLibrary
{
    public IReadOnlyList<ServiceFunction> All { get; }

    private ServiceLibrary(IReadOnlyList<ServiceFunction> all) => All = all;

    public static ServiceLibrary Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Klemme15.Data.service.json");
        if (stream is null) return new ServiceLibrary([]);
        using var doc = JsonDocument.Parse(stream);
        static IReadOnlyList<string> List(JsonElement e, string name) =>
            e.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
        static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
        static byte[]? Hex(string? s) => string.IsNullOrWhiteSpace(s) ? null : Convert.FromHexString(s.Replace(" ", ""));

        var list = new List<ServiceFunction>();
        foreach (var f in doc.RootElement.GetProperty("functions").EnumerateArray())
        {
            var c = f.GetProperty("checks");
            var checks = new ServiceChecks(Bool(c, "ignition"), Bool(c, "engineOff"), Bool(c, "engineRunning"),
                c.TryGetProperty("minVoltage", out var mv) && mv.ValueKind == JsonValueKind.Number ? mv.GetDouble() : null,
                Bool(c, "stationary"), List(c, "confirm"));
            var impls = f.GetProperty("implementations").EnumerateArray().Select(i => new ServiceImplementation(
                List(i, "variants").Select(v => v.ToLowerInvariant()).ToList(), Str(i, "job"), Str(i, "args") ?? "",
                Str(i, "resultJob"), Str(i, "resultArgs") ?? "", Hex(Str(i, "native")), Hex(Str(i, "expect")))).ToList();
            list.Add(new ServiceFunction(
                Str(f, "id")!, Str(f, "title")!, Str(f, "category") ?? "Other", Str(f, "module")!, Str(f, "summary")!, Str(f, "when") ?? "",
                Enum.Parse<ServiceRisk>(Str(f, "risk") ?? "high", ignoreCase: true), Bool(f, "verified"),
                List(f, "before"), List(f, "after"), checks, impls));
        }
        return new ServiceLibrary(list);
    }
}

/// <summary>Decides whether a function may run, checks the car, runs it and reports what happened.</summary>
public static class ServiceRunner
{
    /// <summary>The car's module a function talks to ("DME" → "E9x.DME"; the engine computer for DME/DDE).</summary>
    public static EcuDefinition? ModuleFor(ServiceFunction f, IEnumerable<EcuDefinition> modules)
    {
        var list = modules.ToList();
        return list.FirstOrDefault(m => m.Id.Equals(f.Module, StringComparison.OrdinalIgnoreCase)
                                        || m.Id.EndsWith("." + f.Module, StringComparison.OrdinalIgnoreCase))
               ?? (f.Module is "DME" or "DDE" ? list.FirstOrDefault(m => m.IsEngine) : null);
    }

    /// <param name="variant">The module's variant: the name the module reported (EDIABAS mode) or the most likely one (native).</param>
    /// <param name="answered">Whether the module answered the last quick test.</param>
    public static ServiceAvailability Availability(ServiceFunction f, EcuDefinition? ecu, string? variant, bool answered,
        bool canRunJobs, bool canSendRaw, bool demo = false)
    {
        if (demo && ecu is not null && f.Implementations.Count > 0)
        {
            var first = f.Implementations[0];
            return new(true, "Demo car: nothing is sent, the result is made up.", first, ServiceMode.Demo, variant?.ToLowerInvariant() ?? first.Variants.FirstOrDefault() ?? "demo");
        }
        if (ecu is null) return new(false, $"This car family has no {f.Module} module in Klemme15's list.");
        if (!answered) return new(false, $"Run the quick test first: {ecu.Name} has to answer.");
        if (variant is null) return new(false, $"{ecu.Name}'s version isn't known yet. Run the quick test (and download the fault database) first.");
        var v = variant.ToLowerInvariant();
        var impl = f.Implementations.FirstOrDefault(i => i.Variants.Contains(v));
        if (impl is null) return new(false, $"Not available for {ecu.Name} version {variant.ToUpperInvariant()} yet.");
        if (canRunJobs && impl.Job is not null) return new(true, $"Runs BMW's own job {impl.Job} from your files.", impl, ServiceMode.Ediabas, v);
        if (canSendRaw && impl.Native is not null) return new(true, "Runs without BMW files (Klemme15 sends the documented request itself).", impl, ServiceMode.Native, v);
        return new(false, impl.Job is not null
            ? "Needs your BMW files: connect with \"Use my own BMW files\" ticked."
            : "Not available with this connection.");
    }

    /// <summary>Reads voltage, engine speed and road speed where the connection allows; unknown values stay null.</summary>
    public static async Task<VehicleState> ReadStateAsync(IDiagnosticBackend backend, EcuDefinition? engine, CancellationToken ct = default)
    {
        double? voltage = null, rpm = null, speed = null;
        if (engine is not null)
        {
            try
            {
                var wanted = backend.GetLiveParameters(engine).Where(p => p.Id is "voltage" or "rpm" or "speed").ToList();
                if (wanted.Count > 0)
                    foreach (var v in await backend.ReadLiveAsync(engine, wanted, ct))
                        switch (v.Parameter.Id)
                        {
                            case "voltage": voltage = v.Value; break;
                            case "rpm": rpm = v.Value; break;
                            case "speed": speed = v.Value; break;
                        }
            }
            catch (Exception e) when (e is DiagnosticException or NotSupportedException or TimeoutException) { }

            // EDIABAS mode: the engine computer's own jobs, where it has them.
            if (backend is IJobRunner jobs)
            {
                voltage ??= await JobNumber(jobs, engine, "STATUS_UBATT", ct);
                rpm ??= await JobNumber(jobs, engine, "STATUS_MOTORDREHZAHL", ct);
            }
        }
        return new VehicleState(voltage, rpm, speed, backend.IgnitionOn);
    }

    private static async Task<double?> JobNumber(IJobRunner jobs, EcuDefinition engine, string job, CancellationToken ct)
    {
        try
        {
            var sgbd = engine.Sgbd.FirstOrDefault() ?? $"D_{engine.Address:X4}";
            foreach (var set in await jobs.RunJobAsync(sgbd, job, "", ct))
                foreach (var (k, v) in set)
                    if (k.EndsWith("_WERT", StringComparison.OrdinalIgnoreCase) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                        return d;
        }
        catch (Exception e) when (e is DiagnosticException or NotSupportedException) { }
        return null;
    }

    /// <summary>Evaluates the measurable requirements. Unknown means the user has to confirm it by hand.</summary>
    public static IReadOnlyList<CheckResult> Evaluate(ServiceChecks checks, VehicleState s)
    {
        var list = new List<CheckResult>();
        if (checks.Ignition)
            list.Add(s.Ignition switch
            {
                true => new("Ignition on", CheckState.Pass),
                false => new("Ignition on", CheckState.Fail, "The cable reports the ignition is off."),
                _ => new("Ignition on", CheckState.Unknown, "Klemme15 can't see this with your cable: confirm it."),
            });
        if (checks.EngineOff)
            list.Add(s.Rpm switch
            {
                null => new("Engine off", CheckState.Unknown, "Engine speed couldn't be read: confirm the engine is off."),
                0 => new("Engine off", CheckState.Pass),
                var r => new("Engine off", CheckState.Fail, $"The engine is running ({r:0} rpm)."),
            });
        if (checks.EngineRunning)
            list.Add(s.Rpm switch
            {
                null => new("Engine running", CheckState.Unknown, "Engine speed couldn't be read: confirm the engine is running."),
                > 300 => new("Engine running", CheckState.Pass),
                _ => new("Engine running", CheckState.Fail, "Start the engine first."),
            });
        if (checks.MinVoltage is { } min)
            list.Add(s.Voltage switch
            {
                null => new($"Battery at least {min:0.0} V", CheckState.Unknown, "Voltage couldn't be read: check it with a meter or connect a charger."),
                var v when v >= min => new($"Battery at least {min:0.0} V", CheckState.Pass, $"{v:0.0} V"),
                var v => new($"Battery at least {min:0.0} V", CheckState.Fail, $"{v:0.0} V: charge the battery or connect a charger."),
            });
        if (checks.Stationary)
            list.Add(s.Speed switch
            {
                null => new("Car stationary", CheckState.Unknown, "Road speed couldn't be read: confirm the car is parked."),
                0 => new("Car stationary", CheckState.Pass),
                _ => new("Car stationary", CheckState.Fail, "The car is moving."),
            });
        return list;
    }

    /// <summary>Unverified functions run only when the user has switched on experimental service functions.</summary>
    public static bool Allowed(ServiceFunction f, bool experimentalEnabled) => f.Verified || experimentalEnabled;

    /// <summary>True when every check passed or was confirmed by hand, and every confirmation item is ticked.</summary>
    public static bool Ready(IReadOnlyList<CheckResult> checks, IReadOnlySet<string> confirmedUnknowns, int confirmItems, int confirmedItems) =>
        checks.All(c => c.State == CheckState.Pass || (c.State == CheckState.Unknown && confirmedUnknowns.Contains(c.Text)))
        && confirmedItems >= confirmItems;

    /// <summary>Runs the function once. Every request and reply also goes to the traffic log.</summary>
    public static async Task<ServiceOutcome> RunAsync(ServiceFunction f, ServiceAvailability plan, IDiagnosticBackend backend, EcuDefinition ecu,
        TrafficLog? log = null, CancellationToken ct = default)
    {
        if (!plan.Available || plan.Implementation is not { } impl || plan.Variant is null)
            return new(false, plan.Reason, []);
        log?.Note($"Service function '{f.Title}' on {ecu.Name} ({plan.Variant.ToUpperInvariant()}, {plan.Mode})");
        var details = new List<string>();
        try
        {
            if (plan.Mode == ServiceMode.Demo)
            {
                await Task.Delay(1500, ct);
                details.Add("Demo car: nothing was sent to a real car.");
            }
            else if (plan.Mode == ServiceMode.Ediabas)
            {
                var jobs = backend as IJobRunner ?? throw new DiagnosticException("This connection can't run BMW's jobs.");
                await jobs.RunJobAsync(plan.Variant, impl.Job!, impl.Args, ct);
                details.Add($"{impl.Job}: OKAY");
                if (impl.ResultJob is { } resultJob)
                {
                    foreach (var set in await jobs.RunJobAsync(plan.Variant, resultJob, impl.ResultArgs, ct))
                        foreach (var (k, v) in set)
                            if (!k.StartsWith('_') && k is not "VARIANTE" and not "JOB_STATUS")
                                details.Add($"{k}: {v}");
                }
            }
            else
            {
                var raw = backend as IRawRequests ?? throw new DiagnosticException("This connection can't send the request.");
                var reply = await raw.RequestAsync(ecu, impl.Native!, ct) ?? throw new DiagnosticException("No reply from the module.");
                details.Add($"Sent {Convert.ToHexString(impl.Native!)}, reply {Convert.ToHexString(reply)}");
                if (reply.Length >= 3 && reply[0] == 0x7F)
                    throw new DiagnosticException($"The module refused ({NegativeResponse(reply[2])}).");
                if (impl.Expect is { } expect && !reply.AsSpan().StartsWith(expect))
                    throw new DiagnosticException("The module's reply wasn't the expected confirmation.");
            }
            log?.Note($"  -> done: {string.Join("; ", details)}");
            return new(true, plan.Mode == ServiceMode.Demo ? "Done (demo car)." : "Done. The module confirmed the request.", details);
        }
        catch (Exception e) when (e is DiagnosticException or NotSupportedException or TimeoutException)
        {
            log?.Note($"  -> failed: {e.Message}");
            return new(false, e.Message, details);
        }
    }

    private static string NegativeResponse(byte code) => code switch
    {
        0x11 => "service not supported",
        0x12 => "not supported in this form",
        0x22 => "conditions not correct (check ignition, engine, voltage)",
        0x31 => "request out of range",
        0x33 => "security access denied",
        0x78 => "still busy",
        _ => $"code 0x{code:X2}",
    };
}
