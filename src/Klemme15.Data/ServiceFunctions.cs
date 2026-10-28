using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Klemme15.Core;

namespace Klemme15.Data;

public enum ServiceRisk { Low, Medium, High }

/// <summary>What Klemme15 checks before a service function may run.</summary>
public sealed record ServiceChecks(bool Ignition, bool EngineOff, bool EngineRunning, double? MinVoltage, bool Stationary, IReadOnlyList<string> Confirm);

/// <summary>One BMW job in a procedure. <c>{input}</c> in the job name or arguments is replaced by the user's choice.</summary>
/// <param name="Wait">Seconds to wait after the job (while a part runs or a module learns).</param>
/// <param name="Show">Whether the job's results are shown to the user.</param>
/// <param name="Say">What the user sees while this step runs.</param>
public sealed record ServiceStep(string Job, string Args, double Wait = 0, bool Show = false, string? Say = null);

/// <summary>
/// How to run a function on some module variants: BMW's jobs in order (EDIABAS mode), jobs that always run afterwards
/// to hand the part back to the module (even after a failure or cancel), and/or a documented raw request (native).
/// <paramref name="Map"/> translates a choice into the value these variants expect ("oil" → "Oel").
/// </summary>
public sealed record ServiceImplementation(IReadOnlyList<string> Variants, IReadOnlyList<ServiceStep> Steps, IReadOnlyList<ServiceStep> Stop,
    byte[]? Native, byte[]? Expect, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? Map = null)
{
    /// <summary>The main job (first step).</summary>
    public string? Job => Steps.Count > 0 ? Steps[0].Job : null;

    /// <summary>Whether a choice is possible on these variants (a value missing from <see cref="Map"/> isn't).</summary>
    public bool Supports(string input, string value) =>
        Map is null || !Map.TryGetValue(input, out var m) || m.ContainsKey(value);
}

public sealed record ServiceChoice(string Value, string Label);

/// <summary>Something the user picks or types before running: which service item, which cylinder, an injector code.</summary>
public sealed record ServiceInput(string Id, string Label, bool IsChoice, IReadOnlyList<ServiceChoice> Choices, string? Pattern, string? Hint);

public sealed record ServiceFunction(
    string Id, string Title, string Category, IReadOnlyList<string> Modules, string Summary, string When, ServiceRisk Risk, bool Verified,
    IReadOnlyList<string> Before, IReadOnlyList<string> After, ServiceChecks Checks, IReadOnlyList<ServiceImplementation> Implementations,
    IReadOnlyList<ServiceInput> Inputs, string? Duration = null)
{
    /// <summary>The main module's short name ("DME").</summary>
    public string Module => Modules.Count > 0 ? Modules[0] : "";
}

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
        static ServiceStep Step(JsonElement e) => new(Str(e, "job")!, Str(e, "args") ?? "",
            e.TryGetProperty("wait", out var w) && w.ValueKind == JsonValueKind.Number ? w.GetDouble() : 0, Bool(e, "show"), Str(e, "say"));

        var list = new List<ServiceFunction>();
        foreach (var f in doc.RootElement.GetProperty("functions").EnumerateArray())
        {
            var c = f.GetProperty("checks");
            var checks = new ServiceChecks(Bool(c, "ignition"), Bool(c, "engineOff"), Bool(c, "engineRunning"),
                c.TryGetProperty("minVoltage", out var mv) && mv.ValueKind == JsonValueKind.Number ? mv.GetDouble() : null,
                Bool(c, "stationary"), List(c, "confirm"));
            var impls = f.GetProperty("implementations").EnumerateArray().Select(i =>
            {
                var steps = new List<ServiceStep>();
                if (Str(i, "job") is { } job) steps.Add(new ServiceStep(job, Str(i, "args") ?? "", Show: Str(i, "resultJob") is null));
                if (Str(i, "resultJob") is { } rj) steps.Add(new ServiceStep(rj, Str(i, "resultArgs") ?? "", Show: true));
                if (i.TryGetProperty("steps", out var st)) steps.AddRange(st.EnumerateArray().Select(Step));
                var stop = i.TryGetProperty("stop", out var sp) ? sp.EnumerateArray().Select(Step).ToList() : [];
                Dictionary<string, IReadOnlyDictionary<string, string>>? map = null;
                if (i.TryGetProperty("map", out var mp))
                    map = mp.EnumerateObject().ToDictionary(p => p.Name,
                        p => (IReadOnlyDictionary<string, string>)p.Value.EnumerateObject().ToDictionary(q => q.Name, q => q.Value.GetString()!));
                return new ServiceImplementation(List(i, "variants").Select(v => v.ToLowerInvariant()).ToList(), steps, stop,
                    Hex(Str(i, "native")), Hex(Str(i, "expect")), map);
            }).ToList();
            var modules = f.GetProperty("module") is { ValueKind: JsonValueKind.Array } ma
                ? ma.EnumerateArray().Select(x => x.GetString()!).ToList()
                : [f.GetProperty("module").GetString()!];
            var inputs = f.TryGetProperty("inputs", out var ins)
                ? ins.EnumerateArray().Select(x => new ServiceInput(Str(x, "id")!, Str(x, "label")!, Str(x, "type") != "text",
                    x.TryGetProperty("choices", out var ch) ? ch.EnumerateArray().Select(c => new ServiceChoice(Str(c, "value")!, Str(c, "label")!)).ToList() : [],
                    Str(x, "pattern"), Str(x, "hint"))).ToList()
                : [];
            list.Add(new ServiceFunction(
                Str(f, "id")!, Str(f, "title")!, Str(f, "category") ?? "Other", modules, Str(f, "summary")!, Str(f, "when") ?? "",
                Enum.Parse<ServiceRisk>(Str(f, "risk") ?? "high", ignoreCase: true), Bool(f, "verified"),
                List(f, "before"), List(f, "after"), checks, impls, inputs, Str(f, "duration")));
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
        foreach (var name in f.Modules)
            if (list.FirstOrDefault(m => m.Id.Equals(name, StringComparison.OrdinalIgnoreCase)
                                         || m.Id.EndsWith("." + name, StringComparison.OrdinalIgnoreCase)) is { } found)
                return found;
        return f.Modules.Any(n => n is "DME" or "DDE") ? list.FirstOrDefault(m => m.IsEngine) : null;
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
        if (canRunJobs && impl.Job is not null) return new(true, $"Runs BMW's own procedure for {variant.ToUpperInvariant()} from your BMW files.", impl, ServiceMode.Ediabas, v);
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

    /// <summary>A made-up car state for the demo car that meets the function's requirements.</summary>
    public static VehicleState DemoState(ServiceChecks checks) => new(12.6, checks.EngineRunning ? 760 : 0, 0, true);

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

    /// <summary>Checks the user's choices and typed values; null when they're fine for this implementation.</summary>
    public static string? ValidateInputs(ServiceFunction f, ServiceImplementation? impl, IReadOnlyDictionary<string, string> values)
    {
        foreach (var input in f.Inputs)
        {
            if (!values.TryGetValue(input.Id, out var v) || string.IsNullOrWhiteSpace(v)) return $"Choose or enter: {input.Label}.";
            if (input.IsChoice && input.Choices.All(c => c.Value != v)) return $"'{v}' isn't one of the choices for {input.Label}.";
            if (!input.IsChoice && input.Pattern is { } pattern && !System.Text.RegularExpressions.Regex.IsMatch(v.Trim(), pattern))
                return $"That doesn't look right for {input.Label}{(input.Hint is null ? "." : $": {input.Hint}")}";
            if (impl is not null && !impl.Supports(input.Id, v))
                return $"'{input.Choices.FirstOrDefault(c => c.Value == v)?.Label ?? v}' isn't available on this module version.";
        }
        return null;
    }

    /// <summary>Replaces {input} placeholders with the user's values, translated through the implementation's map.</summary>
    public static string Fill(string template, ServiceImplementation impl, IReadOnlyDictionary<string, string> values) =>
        System.Text.RegularExpressions.Regex.Replace(template, @"\{(\w+)\}", m =>
        {
            var id = m.Groups[1].Value;
            if (!values.TryGetValue(id, out var v)) return m.Value;
            v = v.Trim();
            return impl.Map is not null && impl.Map.TryGetValue(id, out var map) && map.TryGetValue(v, out var mapped) ? mapped : v;
        });

    /// <summary>Results not worth showing: protocol bytes, addresses and status flags.</summary>
    private static bool Hidden(string key) =>
        key.StartsWith('_') || key is "VARIANTE" or "JOB_STATUS" or "RESP_CODE" or "ID_SG_ADR" or "STAT_ID_SG_ADR" or "TELEGRAMM" or "TELEGRAMM_ANF" or "TELEGRAMM_ANT"
        || key.StartsWith("ECU_ADR", StringComparison.Ordinal) || key.EndsWith("_EINH", StringComparison.Ordinal);

    /// <summary>Runs the function once. Every request and reply also goes to the traffic log.</summary>
    public static Task<ServiceOutcome> RunAsync(ServiceFunction f, ServiceAvailability plan, IDiagnosticBackend backend, EcuDefinition ecu,
        TrafficLog? log = null, CancellationToken ct = default) =>
        RunAsync(f, plan, backend, ecu, new Dictionary<string, string>(), null, log, ct);

    /// <summary>Runs the function once with the user's choices; <paramref name="progress"/> gets what's happening now.</summary>
    public static async Task<ServiceOutcome> RunAsync(ServiceFunction f, ServiceAvailability plan, IDiagnosticBackend backend, EcuDefinition ecu,
        IReadOnlyDictionary<string, string> inputs, IProgress<string>? progress, TrafficLog? log = null, CancellationToken ct = default)
    {
        if (!plan.Available || plan.Implementation is not { } impl || plan.Variant is null)
            return new(false, plan.Reason, []);
        if (ValidateInputs(f, plan.Mode == ServiceMode.Demo ? null : impl, inputs) is { } problem)
            return new(false, problem, []);
        log?.Note($"Service function '{f.Title}' on {ecu.Name} ({plan.Variant.ToUpperInvariant()}, {plan.Mode})"
                  + (inputs.Count > 0 ? $" with {string.Join(", ", inputs.Select(kv => $"{kv.Key}={kv.Value}"))}" : ""));
        var details = new List<string>();
        var jobs = backend as IJobRunner;
        var started = false;
        try
        {
            if (plan.Mode == ServiceMode.Demo)
            {
                foreach (var step in impl.Steps)
                {
                    progress?.Report(step.Say ?? $"Running {step.Job}...");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(step.Wait, 0.5, 2)), ct);
                }
                details.Add("Demo car: nothing was sent to a real car.");
            }
            else if (plan.Mode == ServiceMode.Ediabas)
            {
                if (jobs is null) throw new DiagnosticException("This connection can't run BMW's jobs.");
                for (var i = 0; i < impl.Steps.Count; i++)
                {
                    var step = impl.Steps[i];
                    var job = Fill(step.Job, impl, inputs);
                    var args = Fill(step.Args, impl, inputs);
                    progress?.Report(step.Say ?? (impl.Steps.Count > 1 ? $"Step {i + 1} of {impl.Steps.Count}: {job}" : $"Running {job}..."));
                    started = true;
                    var sets = await jobs.RunJobAsync(plan.Variant, job, args, ct);
                    var show = step.Show || (i == impl.Steps.Count - 1 && !impl.Steps.Any(s => s.Show));
                    if (show)
                    {
                        var shown = 0;
                        foreach (var set in sets)
                            foreach (var (k, v) in set)
                                if (!Hidden(k) && !string.IsNullOrWhiteSpace(v))
                                {
                                    details.Add($"{ReadingLabels.ResultLabel(k, null)}: {ReadingLabels.ValueText(v)}");
                                    shown++;
                                }
                        if (shown == 0) details.Add($"{job}: OKAY");
                    }
                    else details.Add($"{job}: OKAY");
                    if (step.Wait > 0)
                    {
                        var until = DateTime.UtcNow.AddSeconds(step.Wait);
                        while (DateTime.UtcNow < until)
                        {
                            var left = (int)Math.Ceiling((until - DateTime.UtcNow).TotalSeconds);
                            progress?.Report($"{step.Say ?? "Running"} ({left} s left)");
                            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(1000, Math.Max(50, (until - DateTime.UtcNow).TotalMilliseconds))), ct);
                        }
                    }
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
            return new(true, plan.Mode == ServiceMode.Demo ? "Done (demo car)." : "Done. The module confirmed every step.", details);
        }
        catch (Exception e) when (e is DiagnosticException or NotSupportedException or TimeoutException)
        {
            log?.Note($"  -> failed: {e.Message}");
            return new(false, e.Message, details);
        }
        finally
        {
            // Always hand the part back to the module, even after a failure or when the user stopped the test.
            if (started && jobs is not null && plan.Mode == ServiceMode.Ediabas)
                foreach (var stop in impl.Stop)
                {
                    try
                    {
                        await jobs.RunJobAsync(plan.Variant, Fill(stop.Job, impl, inputs), Fill(stop.Args, impl, inputs), CancellationToken.None);
                        log?.Note($"  -> ended with {stop.Job}");
                    }
                    catch (Exception e) when (e is DiagnosticException or NotSupportedException or TimeoutException)
                    {
                        log?.Note($"  -> {stop.Job} failed: {e.Message}");
                    }
                }
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
