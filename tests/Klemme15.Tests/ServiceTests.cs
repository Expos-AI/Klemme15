using Klemme15.Core;
using Klemme15.Data;
using Xunit;

namespace Klemme15.Tests;

public class ServiceTests
{
    private static readonly ServiceLibrary Library = ServiceLibrary.Load();
    private static ServiceFunction Fn(string id) => Library.All.Single(f => f.Id == id);

    private static readonly EcuDefinition Dme = new() { Id = "E9x.DME", Name = "Engine (DME)", Address = 0x12, Protocol = BusProtocol.BmwFast, IsEngine = true };
    private static readonly EcuDefinition Dsc = new() { Id = "E9x.DSC", Name = "DSC", Address = 0x29, Protocol = BusProtocol.BmwFast };

    [Fact]
    public void Catalog_is_complete_and_consistent()
    {
        Assert.True(Library.All.Count >= 50);
        Assert.Equal(Library.All.Count, Library.All.Select(f => f.Id).Distinct().Count());
        foreach (var f in Library.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Summary));
            Assert.NotEmpty(f.Implementations);
            Assert.True(f.Checks.Ignition, $"{f.Id} must require the ignition");
            foreach (var i in f.Implementations)
            {
                Assert.NotEmpty(i.Variants);
                Assert.True(i.Job is not null || i.Native is not null, f.Id);
                Assert.All(i.Variants, v => Assert.Equal(v.ToLowerInvariant(), v));
                // Every {placeholder} in a job or its arguments is something the user chooses.
                foreach (var step in i.Steps.Concat(i.Stop))
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(step.Job + step.Args, @"\{(\w+)\}"))
                        Assert.Contains(f.Inputs, input => input.Id == m.Groups[1].Value);
            }
            // Every choice is possible on at least one module version, and no version is listed twice.
            foreach (var input in f.Inputs.Where(x => x.IsChoice))
                foreach (var c in input.Choices)
                    Assert.Contains(f.Implementations, i => i.Supports(input.Id, c.Value));
            var all = f.Implementations.SelectMany(i => i.Variants).ToList();
            Assert.Equal(all.Count, all.Distinct().Count());
        }
    }

    [Fact]
    public void Catalog_never_contains_coding_flashing_or_immobiliser_jobs()
    {
        // No coding, flashing, immobiliser, key, VIN or odometer jobs. (Injector correction codes are allowed: they are
        // calibration values printed on the part, which ISTA also writes as a service function.)
        var forbidden = new[] { "CODIER", "FLASH", "EWS", "ISN", "CAS_", "SCHLUESSEL", "FAHRGESTELL", "FG_NR", "VIN", "GWSZ", "STARTWERT", "SPEICHER_SCHREIBEN", "EEPROM" };
        foreach (var f in Library.All)
        {
            Assert.DoesNotContain(f.Modules, m => m is "CAS" or "EWS" or "ELV");
            foreach (var step in f.Implementations.SelectMany(i => i.Steps.Concat(i.Stop)))
                Assert.DoesNotContain(forbidden, w => step.Job.Contains(w, StringComparison.OrdinalIgnoreCase));
        }
        // Native requests may only start a routine ($31) or control an output ($30).
        foreach (var i in Library.All.SelectMany(f => f.Implementations))
            if (i.Native is { } n)
                Assert.Contains(n[0], new byte[] { 0x30, 0x31 });
    }

    [Fact]
    public void Unverified_functions_need_the_experimental_switch()
    {
        var f = Fn("battery-registration");
        Assert.False(f.Verified);
        Assert.False(ServiceRunner.Allowed(f, experimentalEnabled: false));
        Assert.True(ServiceRunner.Allowed(f, experimentalEnabled: true));
    }

    [Fact]
    public void Picks_bmw_job_in_ediabas_mode_and_native_request_without_files()
    {
        var f = Fn("battery-registration");
        var ediabas = ServiceRunner.Availability(f, Dme, "MSV80", answered: true, canRunJobs: true, canSendRaw: false);
        Assert.True(ediabas.Available);
        Assert.Equal(ServiceMode.Ediabas, ediabas.Mode);
        Assert.Equal("msv80", ediabas.Variant);

        var native = ServiceRunner.Availability(f, Dme, "msv80", answered: true, canRunJobs: false, canSendRaw: true);
        Assert.True(native.Available);
        Assert.Equal(ServiceMode.Native, native.Mode);

        // Steering angle calibration has no documented native request: it needs BMW files.
        var sas = ServiceRunner.Availability(Fn("steering-angle-calibration"), Dsc, "dsc_87", true, canRunJobs: false, canSendRaw: true);
        Assert.False(sas.Available);
        Assert.Contains("BMW files", sas.Reason);
    }

    [Fact]
    public void Not_available_for_unknown_versions_or_silent_modules()
    {
        var f = Fn("battery-registration");
        Assert.False(ServiceRunner.Availability(f, Dme, "ms45", true, true, true).Available);
        Assert.False(ServiceRunner.Availability(f, Dme, "msv80", answered: false, true, true).Available);
        Assert.False(ServiceRunner.Availability(f, Dme, null, true, true, true).Available);
        Assert.False(ServiceRunner.Availability(f, null, "msv80", true, true, true).Available);
    }

    [Fact]
    public void Finds_the_module_by_short_name_or_engine()
    {
        var modules = new[] { Dsc, Dme };
        Assert.Equal(Dme, ServiceRunner.ModuleFor(Fn("battery-registration"), modules));
        Assert.Equal(Dsc, ServiceRunner.ModuleFor(Fn("steering-angle-calibration"), modules));
        Assert.Null(ServiceRunner.ModuleFor(Fn("window-initialisation"), modules));
    }

    [Fact]
    public void Checks_the_car()
    {
        var cases = new (VehicleState State, CheckState Worst)[]
        {
            (new(12.6, 0, 0, true), CheckState.Pass),
            (new(11.4, 0, 0, true), CheckState.Fail),        // battery too low
            (new(12.6, 780, 0, true), CheckState.Fail),      // engine running
            (new(12.6, 0, 0, false), CheckState.Fail),       // ignition off
            (new(null, null, null, null), CheckState.Unknown), // nothing measurable: the user must confirm
        };
        foreach (var (state, worst) in cases)
        {
            var results = ServiceRunner.Evaluate(Fn("battery-registration").Checks, state);
            var actual = results.Any(r => r.State == CheckState.Fail) ? CheckState.Fail
                : results.Any(r => r.State == CheckState.Unknown) ? CheckState.Unknown : CheckState.Pass;
            Assert.Equal(worst, actual);
        }
    }

    [Fact]
    public void Steering_calibration_requires_a_stationary_car()
    {
        var results = ServiceRunner.Evaluate(Fn("steering-angle-calibration").Checks, new VehicleState(12.5, 750, 12, true));
        Assert.Contains(results, r => r.Text == "Car stationary" && r.State == CheckState.Fail);
    }

    [Fact]
    public async Task Runs_native_battery_registration_and_checks_the_reply()
    {
        var f = Fn("battery-registration");
        var plan = ServiceRunner.Availability(f, Dme, "msv80", true, false, true);
        var car = new FakeCar(req => req[0] == 0x31 ? [0x71, req[1], req[2], 0x00] : null);
        var log = new TrafficLog();
        var outcome = await ServiceRunner.RunAsync(f, plan, car, Dme, log);
        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal(Convert.FromHexString("3130001000"), car.Sent.Single());
        Assert.Contains(log.Snapshot(), e => e.ToString().Contains("Register a new battery"));
    }

    [Fact]
    public async Task Reports_a_refusal_in_plain_words()
    {
        var f = Fn("battery-registration");
        var plan = ServiceRunner.Availability(f, Dme, "msv80", true, false, true);
        var outcome = await ServiceRunner.RunAsync(f, plan, new FakeCar(_ => [0x7F, 0x31, 0x22]), Dme);
        Assert.False(outcome.Success);
        Assert.Contains("conditions not correct", outcome.Message);
    }

    [Fact]
    public async Task Runs_bmw_job_then_reads_the_result()
    {
        var f = Fn("steering-angle-calibration");
        var plan = ServiceRunner.Availability(f, Dsc, "DSC_87", true, canRunJobs: true, canSendRaw: false);
        var car = new FakeCar(_ => null);
        var outcome = await ServiceRunner.RunAsync(f, plan, car, Dsc);
        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal(["dsc_87/LENKWINKEL_DSC_ABGLEICHEN/", "dsc_87/STAT_ERGEBNIS_ABGLEICH_ROUTINE/LWS;R"], car.Jobs);
        Assert.Contains(outcome.Details, d => d.Contains("rgebnis", StringComparison.OrdinalIgnoreCase) || d.Contains("result", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Reads_voltage_and_engine_speed_for_the_checks()
    {
        var state = await ServiceRunner.ReadStateAsync(new FakeCar(_ => null), Dme);
        Assert.Equal(12.4, state.Voltage);
        Assert.Equal(0, state.Rpm);
        Assert.Equal(0, state.Speed);
        Assert.True(state.Ignition == true);
    }

    [Fact]
    public void Choices_are_translated_for_each_module_version()
    {
        var f = Fn("service-reset");
        var e9x = f.Implementations.Single(i => i.Variants.Contains("komb87"));
        var e46 = f.Implementations.Single(i => i.Variants.Contains("kombi46"));
        var oil = new Dictionary<string, string> { ["item"] = "oil" };
        Assert.Equal("Oel;100;31", ServiceRunner.Fill(e9x.Steps[0].Args, e9x, oil));
        Assert.Equal("Oel_Reset", ServiceRunner.Fill(e46.Steps[0].Args, e46, oil));

        // Brake pads exist on the E9x cluster only; the E46 cluster has inspections instead.
        Assert.Null(ServiceRunner.ValidateInputs(f, e9x, new Dictionary<string, string> { ["item"] = "brakes-front" }));
        Assert.NotNull(ServiceRunner.ValidateInputs(f, e46, new Dictionary<string, string> { ["item"] = "brakes-front" }));
        Assert.NotNull(ServiceRunner.ValidateInputs(f, e9x, new Dictionary<string, string>()));
    }

    [Fact]
    public void Typed_values_are_checked()
    {
        var f = Fn("injector-code-program");
        var impl = f.Implementations[0];
        Assert.Null(ServiceRunner.ValidateInputs(f, impl, new Dictionary<string, string> { ["cyl"] = "2", ["code"] = "B4T7VJ1" }));
        Assert.NotNull(ServiceRunner.ValidateInputs(f, impl, new Dictionary<string, string> { ["cyl"] = "2", ["code"] = "B4T7;VJ1" }));
        Assert.Equal("IMA2;B4T7VJ1", ServiceRunner.Fill(impl.Steps[0].Args, impl, new Dictionary<string, string> { ["cyl"] = "2", ["code"] = " B4T7VJ1 " }));
    }

    [Fact]
    public void Job_names_can_depend_on_a_choice()
    {
        var f = Fn("abs-bleed");
        var impl = f.Implementations.Single(i => i.Variants.Contains("dsc_87"));
        Assert.Equal("NA_ENTLUEFTUNG_VA_DX", ServiceRunner.Fill(impl.Steps[0].Job, impl, new Dictionary<string, string> { ["circuit"] = "front" }));
    }

    [Fact]
    public void Component_tests_get_the_arguments_each_engine_computer_expects()
    {
        // MSV80 takes the pump duty in percent; MSV70 takes on/off. Sending "1" to an MSV80 would barely run the pump.
        var pump = Fn("test-fuel-pump");
        Assert.Equal("99;10", pump.Implementations.Single(i => i.Variants.Contains("msv80")).Steps[0].Args);
        Assert.Equal("1;10", pump.Implementations.Single(i => i.Variants.Contains("msv70")).Steps[0].Args);
        // Tests without a built-in timeout always switch the part off again.
        var me9 = pump.Implementations.Single(i => i.Variants.Contains("me9n45"));
        Assert.Equal("STEUERN_EKP_AUS", me9.Stop.Single().Job);
    }

    private static ServiceFunction Synthetic(params ServiceStep[] steps) =>
        new("synthetic", "Synthetic test", "Tests", ["DME"], "x", "", ServiceRisk.Low, false, [], [],
            new ServiceChecks(true, false, false, null, false, []),
            [new ServiceImplementation(["msv80"], steps, [new ServiceStep("STEUERN_ENDE", "")], null, null)], []);

    [Fact]
    public async Task Stop_job_runs_after_success_failure_and_cancel()
    {
        var f = Synthetic(new ServiceStep("STEUERN_AN", "1", Wait: 0.2), new ServiceStep("STATUS_X", "", Show: true));
        var plan = ServiceRunner.Availability(f, Dme, "msv80", true, canRunJobs: true, canSendRaw: false);

        var ok = new FakeCar(_ => null);
        Assert.True((await ServiceRunner.RunAsync(f, plan, ok, Dme)).Success);
        Assert.Equal("msv80/STEUERN_ENDE/", ok.Jobs[^1]);

        var failing = new FakeCar(_ => null) { FailJob = "STATUS_X" };
        var outcome = await ServiceRunner.RunAsync(f, plan, failing, Dme);
        Assert.False(outcome.Success);
        Assert.Equal("msv80/STEUERN_ENDE/", failing.Jobs[^1]);

        var cancelled = new FakeCar(_ => null);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var stopped = false;
        try { await ServiceRunner.RunAsync(f, plan, cancelled, Dme, ct: cts.Token); }
        catch (OperationCanceledException) { stopped = true; }
        Assert.True(stopped);
        Assert.Equal("msv80/STEUERN_ENDE/", cancelled.Jobs[^1]);
        Assert.False(cancelled.Jobs.Contains("msv80/STATUS_X/"));
    }

    [Fact]
    public void Demo_state_meets_the_function_requirements()
    {
        foreach (var f in Library.All)
            Assert.DoesNotContain(ServiceRunner.Evaluate(f.Checks, ServiceRunner.DemoState(f.Checks)), c => c.State == CheckState.Fail);
    }

    private sealed class FakeCar(Func<byte[], byte[]?> answer) : IDiagnosticBackend, IRawRequests, IJobRunner
    {
        public List<byte[]> Sent { get; } = [];
        public List<string> Jobs { get; } = [];
        public string Name => "fake";
        public BackendCapabilities Capabilities => BackendCapabilities.None;
        public bool? IgnitionOn => true;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<string?> ReadVinAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<EcuIdentity?> IdentifyAsync(EcuDefinition ecu, CancellationToken ct = default) => Task.FromResult<EcuIdentity?>(new EcuIdentity());
        public Task<IReadOnlyList<Fault>> ReadFaultsAsync(EcuDefinition ecu, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Fault>>([]);
        public Task ClearFaultsAsync(EcuDefinition ecu, CancellationToken ct = default) => Task.CompletedTask;

        public IReadOnlyList<LiveParameter> GetLiveParameters(EcuDefinition ecu) =>
            new[] { "rpm", "speed", "voltage" }.Select(id => new LiveParameter { Id = id, Name = id, Source = "obd:" + id }).ToList();

        public Task<IReadOnlyList<LiveValue>> ReadLiveAsync(EcuDefinition ecu, IReadOnlyList<LiveParameter> parameters, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LiveValue>>(parameters.Select(p => new LiveValue(p, p.Id switch { "voltage" => 12.4, _ => 0 }, null, DateTimeOffset.Now)).ToList());

        public Task<byte[]?> RequestAsync(EcuDefinition ecu, byte[] request, CancellationToken ct = default)
        {
            Sent.Add(request);
            return Task.FromResult(answer(request));
        }

        public Task<IReadOnlyList<string>> ListJobsAsync(string sgbd, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);

        public string? FailJob { get; init; }

        public Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> RunJobAsync(string sgbd, string job, string arguments, CancellationToken ct = default)
        {
            Jobs.Add($"{sgbd}/{job}/{arguments}");
            if (job == FailJob) throw new DiagnosticException($"{sgbd} {job}: ERROR_ECU_CONDITIONS_NOT_CORRECT");
            IReadOnlyDictionary<string, string> set = new Dictionary<string, string> { ["JOB_STATUS"] = "OKAY", ["STAT_ERGEBNIS_ABGLEICH_ROUTINE_TEXT"] = "Abgleich erfolgreich" };
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, string>>>([set]);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
