namespace Klemme15.Core;

public enum TestSeverity { Ok, Watch, Problem, Unknown }

/// <summary>One conclusion of a guided test, in plain words.</summary>
public sealed record TestFinding(TestSeverity Severity, string Title, string Explanation);

/// <summary>Mixture corrections (short + long term, in percent; + means the engine adds fuel because it runs lean).</summary>
public sealed record TrimSample(double Bank1, double? Bank2);

/// <summary>
/// Turns measured values into diagnoses. The thresholds are Klemme15's own rules of thumb, written down here so anyone
/// can check them: total fuel correction within ±8 % is healthy, beyond ±12 % something is wrong.
/// </summary>
public static class GuidedAnalysis
{
    public const double TrimOk = 8, TrimBad = 12;

    public static IReadOnlyList<TestFinding> FuelTrims(TrimSample idle, TrimSample load)
    {
        static string F(double v) => $"{(v >= 0 ? "+" : "")}{v:0.0} %";
        var one = Diagnose(idle.Bank1, load.Bank1);
        if (idle.Bank2 is not { } i2 || load.Bank2 is not { } l2)
            return [new(one.Severity, $"The engine: {one.Diagnosis}", $"Idle {F(idle.Bank1)}, 2,500 rpm {F(load.Bank1)}. {one.Advice}")];

        var two = Diagnose(i2, l2);
        var numbers = $"Bank 1 (cylinders 1-3): idle {F(idle.Bank1)}, 2,500 rpm {F(load.Bank1)}. Bank 2 (cylinders 4-6): idle {F(i2)}, 2,500 rpm {F(l2)}.";
        var findings = new List<TestFinding>();
        if (one.Diagnosis == two.Diagnosis)
            findings.Add(new(one.Severity, $"Both banks: {one.Diagnosis}", $"{numbers} {one.Advice}"));
        else
        {
            findings.Add(new(one.Severity, $"Bank 1 (cylinders 1-3): {one.Diagnosis}", $"Idle {F(idle.Bank1)}, 2,500 rpm {F(load.Bank1)}. {one.Advice}"));
            findings.Add(new(two.Severity, $"Bank 2 (cylinders 4-6): {two.Diagnosis}", $"Idle {F(i2)}, 2,500 rpm {F(l2)}. {two.Advice}"));
        }
        var d = Math.Abs(idle.Bank1 - i2);
        if (d > TrimOk && Math.Sign(idle.Bank1) != Math.Sign(i2) || d > TrimBad)
            findings.Add(new(TestSeverity.Watch, "The two banks disagree",
                "Only one side is off. Look at parts that serve one bank only: its injectors, its intake runners and gaskets, its oxygen sensor, and exhaust leaks before that bank's sensor (a leak there makes the sensor read lean)."));
        return findings;
    }

    private static (TestSeverity Severity, string Diagnosis, string Advice) Diagnose(double idle, double load)
    {
        static bool lean(double v) => v > TrimBad;
        static bool rich(double v) => v < -TrimBad;
        static bool ok(double v) => Math.Abs(v) <= TrimOk;

        if (ok(idle) && ok(load))
            return (TestSeverity.Ok, "mixture is healthy", "The engine computer barely has to correct the mixture.");
        if (lean(idle) && load < idle - 6 && load <= TrimOk + 2)
            return (TestSeverity.Problem, "lean at idle, fine under load: unmetered air (vacuum leak)",
                "Extra air that bypasses the air mass meter matters most at idle and gets diluted at higher rpm. Check, in this order: crankcase vent valve (oil filler cap sucks hard or whistles), intake boot between air mass meter and throttle, oil filler cap seal, DISA valve and intake manifold gaskets, brake booster hose, injector seals. A smoke test finds it fastest.");
        if (lean(idle) && lean(load))
            return (TestSeverity.Problem, "lean everywhere: not enough fuel or under-measured air",
                "Check fuel pressure first (weak pump, clogged filter, failing pressure regulator; on direct injection also the high-pressure pump), then a dirty or failing air mass meter, then exhaust leaks before the oxygen sensors.");
        if (lean(load) && !lean(idle))
            return (TestSeverity.Problem, "lean under load: fuel delivery or air mass meter",
                "The engine runs short of fuel when it needs more. Usual causes: weak fuel pump or clogged fuel filter, low fuel pressure, or an air mass meter reading too low at higher flow.");
        if (rich(idle) && rich(load))
            return (TestSeverity.Problem, "rich everywhere: too much fuel",
                "Look for a leaking injector, fuel pressure too high, a purge valve stuck open (worse right after refuelling), or an air mass meter reading too high. Oil diluted with fuel also makes it run rich.");
        if (rich(idle) && !rich(load))
            return (TestSeverity.Problem, "rich at idle: purge valve or a dripping injector",
                "Fuel vapour or a dripping injector matters most at idle. Test the purge valve (Service > Test the purge valve) and check the injectors.");
        return (TestSeverity.Watch, "slightly off",
            "Not bad enough to point at one cause. Run the test again after a drive; if it keeps getting worse, look at the most common causes for lean (vacuum leaks) or rich (purge valve, injectors).");
    }

    /// <param name="samples">Pairs of (target, actual) camshaft angles.</param>
    public static TestFinding Vanos(string cam, IReadOnlyList<(double Target, double Actual)> samples)
    {
        if (samples.Count < 3) return new(TestSeverity.Unknown, $"{cam} VANOS: not enough readings", "The engine computer didn't report target and actual positions often enough.");
        var errors = samples.Select(s => Math.Abs(s.Target - s.Actual)).ToList();
        var mean = errors.Average();
        var worst = errors.Max();
        var range = samples.Max(s => s.Target) - samples.Min(s => s.Target);
        var nums = $"Average deviation {mean:0.0}°, worst {worst:0.0}°, target moved {range:0.0}° during the test.";
        if (mean <= 3 && worst <= 8)
            return new(TestSeverity.Ok, $"{cam} VANOS follows its target", nums);
        if (mean <= 6)
            return new(TestSeverity.Watch, $"{cam} VANOS lags a little",
                $"{nums} Often dirty oil or a sticky solenoid. Change the oil, clean or swap the VANOS solenoids (intake and exhaust are identical on most engines, so swapping them shows whether the fault moves).");
        return new(TestSeverity.Problem, $"{cam} VANOS doesn't reach its target",
            $"{nums} Check the solenoid and its connector, the oil level and the solenoid's filter screen. If the solenoids are fine, the VANOS unit or low oil pressure at the cylinder head (worn seals, timing chain stretch) is likely.");
    }

    /// <summary>Roughness per cylinder (BMW's own units): the cylinder that stands out from the others is the weak one.</summary>
    public static IReadOnlyList<TestFinding> Roughness(IReadOnlyList<double> perCylinder)
    {
        if (perCylinder.Count < 3) return [new(TestSeverity.Unknown, "Not enough cylinders reported", "The engine computer didn't report the per-cylinder values.")];
        var abs = perCylinder.Select(Math.Abs).ToList();
        var median = abs.OrderBy(x => x).ElementAt(abs.Count / 2);
        var floor = Math.Max(median, abs.Max() * 0.05) + 1e-9;
        var findings = new List<TestFinding>();
        for (var i = 0; i < abs.Count; i++)
        {
            var ratio = abs[i] / floor;
            if (ratio >= 3 && abs[i] > 0)
                findings.Add(new(TestSeverity.Problem, $"Cylinder {i + 1} runs much rougher than the others",
                    $"About {ratio:0}× the typical value. Swap its ignition coil with a neighbouring cylinder and test again: if the problem moves, it's the coil; if not, check the spark plug, then the injector and compression."));
            else if (ratio >= 2)
                findings.Add(new(TestSeverity.Watch, $"Cylinder {i + 1} is rougher than the others",
                    $"About {ratio:0.0}× the typical value. Worth watching: check its spark plug and coil at the next service, or run the cylinder cut-out test."));
        }
        if (findings.Count == 0)
            findings.Add(new(TestSeverity.Ok, "All cylinders run evenly", "No cylinder stands out from the others."));
        return findings;
    }
}

/// <summary>What a running guided test shows: what the user should do now, and how far along it is.</summary>
public sealed record TestProgress(string Instruction, string Status, double Fraction);

/// <summary>The outcome of a guided test: findings plus the numbers behind them (for a chart or table).</summary>
public sealed record TestReport(IReadOnlyList<TestFinding> Findings, IReadOnlyList<KeyValuePair<string, double>> Values, string Unit = "")
{
    public static TestReport Stop(string title, string why) => new([new TestFinding(TestSeverity.Unknown, title, why)], []);
}

public sealed record GuidedTestInfo(string Id, string Title, string Summary, string Requirements, string Duration);

/// <summary>
/// Guided tests that measure the engine and explain the result: fuel trims at idle and 2,500 rpm, VANOS target vs
/// actual, and cylinder-by-cylinder roughness. They only read values; nothing is changed on the car.
/// </summary>
public static class GuidedTests
{
    public static readonly IReadOnlyList<GuidedTestInfo> All =
    [
        new("fuel-trims", "Fuel trim test (lean / rich diagnosis)",
            "Measures how much the engine computer corrects the mixture at idle and at 2,500 rpm, and tells you what the pattern points to: vacuum leak, fuel delivery, air mass meter, leaking injector or purge valve.",
            "Warm engine (coolant above 70 °C), idling, in P or neutral, handbrake on. You'll be asked to hold 2,500 rpm for about 15 seconds.",
            "About 1 minute"),
        new("vanos", "VANOS test (target vs actual)",
            "Compares where the engine computer wants each camshaft with where it actually is, while you blip the throttle. A VANOS that lags or never reaches its target shows up here before it sets a fault.",
            "Warm engine, idling, in P or neutral, handbrake on. You'll be asked to blip the throttle a few times.",
            "30 seconds"),
        new("roughness", "Cylinder roughness comparison",
            "Reads how smoothly each cylinder fires at idle and shows them side by side, so a weak cylinder stands out before it sets a misfire fault.",
            "Warm engine, idling, in P or neutral, all consumers (air conditioning, lights) off.",
            "20 seconds"),
        new("no-start", "Why won't it start? (read-only check)",
            "For a car that cranks but won't fire, or won't crank. Klemme15 checks the things it can see over the cable — battery voltage, whether the engine is actually turning, and the faults the modules logged during the failed start — and tells you what to check next. It never touches the immobiliser or keys.",
            "Ignition on. To check cranking too, turn the key to start (or press start with the brake) while this runs.",
            "20 seconds"),
        new("cooling-bleed", "Cooling system bleed assistant (electric water pump)",
            "Walks you through BMW's own bleed routine on engines with an electric water pump (N51, N52, N53, N54, N55): with the engine off you hold the accelerator to the floor, and the engine computer runs the pump through its 12-minute bleed cycle by itself. Klemme15 watches the pump speed, coolant temperature and battery voltage and tells you whether the cycle ran. It only reads values.",
            "Engine OFF, ignition ON, battery charger connected, expansion tank filled to MAX, heater set to maximum temperature with the blower on its lowest setting. Never open the cap on a hot engine.",
            "About 13 minutes"),
    ];

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(500);

    /// <param name="faults">Optional: reads the start-chain modules' fault memories, for the no-start triage.</param>
    public static Task<TestReport> RunAsync(string id, ISignalSource signals, IProgress<TestProgress>? progress, CancellationToken ct, double speed = 1,
        Func<CancellationToken, Task<IReadOnlyList<NoStartTriage.ModuleFault>>>? faults = null) => id switch
    {
        "fuel-trims" => FuelTrimsAsync(signals, progress, ct, speed),
        "vanos" => VanosAsync(signals, progress, ct, speed),
        "roughness" => RoughnessAsync(signals, progress, ct, speed),
        "no-start" => NoStartAsync(signals, progress, ct, speed, faults),
        "cooling-bleed" => CoolingBleedAsync(signals, progress, ct, speed),
        _ => throw new ArgumentException($"Unknown test {id}"),
    };

    private static Task Wait(double speed, CancellationToken ct) => Task.Delay(TimeSpan.FromMilliseconds(Tick.TotalMilliseconds / speed), ct);

    private static async Task<TestReport?> Ready(ISignalSource signals, IProgress<TestProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new("Checking the engine...", "", 0));
        var now = await signals.ReadAsync(["rpm", "coolant"], ct);
        if (!now.TryGetValue("rpm", out var rpm)) return TestReport.Stop("Engine speed can't be read", "This test needs the engine computer's live values. Connect again, and run the quick test first.");
        if (rpm < 400) return TestReport.Stop("Start the engine first", "The engine isn't running. Start it, let it warm up and run the test again.");
        if (now.TryGetValue("coolant", out var coolant) && coolant < 70)
            return TestReport.Stop("Warm the engine up first", $"Coolant is {coolant:0} °C. Mixture and VANOS behave differently on a cold engine; drive 10 minutes and test again.");
        return null;
    }

    public static async Task<TestReport> FuelTrimsAsync(ISignalSource signals, IProgress<TestProgress>? progress, CancellationToken ct, double speed = 1)
    {
        if (await Ready(signals, progress, ct) is { } stop) return stop;
        string[] wanted = ["rpm", "stft1", "ltft1", "stft2", "ltft2"];

        async Task<TrimSample?> Phase(string instruction, double minRpm, double maxRpm, int needed, int timeoutTicks, double start, double share)
        {
            var b1 = new List<double>(); var b2 = new List<double>();
            for (var tick = 0; tick < timeoutTicks && b1.Count < needed; tick++)
            {
                var v = await signals.ReadAsync(wanted, ct);
                var rpm = v.GetValueOrDefault("rpm");
                var inBand = rpm >= minRpm && rpm <= maxRpm;
                if (inBand && v.TryGetValue("stft1", out var s1) && v.TryGetValue("ltft1", out var l1))
                {
                    b1.Add(s1 + l1);
                    if (v.TryGetValue("stft2", out var s2) && v.TryGetValue("ltft2", out var l2)) b2.Add(s2 + l2);
                }
                progress?.Report(new(instruction, inBand ? $"{rpm:0} rpm: measuring ({b1.Count}/{needed})" : $"{rpm:0} rpm: waiting for {minRpm:0}-{maxRpm:0} rpm",
                    start + share * b1.Count / needed));
                await Wait(speed, ct);
            }
            if (b1.Count < needed / 2) return null;
            return new TrimSample(b1.Average(), b2.Count >= b1.Count / 2 && b2.Count > 0 ? b2.Average() : null);
        }

        var idle = await Phase("Let the engine idle. Don't touch the pedal.", 500, 1100, 30, 80, 0, 0.5);
        if (idle is null) return TestReport.Stop("No steady idle reading", "The fuel trims or a steady idle couldn't be read. Make sure the engine idles normally and try again.");
        var load = await Phase("Now hold the engine at 2,500 rpm with the pedal, steady.", 2200, 2900, 25, 240, 0.5, 0.5);
        if (load is null) return TestReport.Stop("2,500 rpm wasn't held long enough", "Hold the engine steadily between 2,200 and 2,900 rpm until the test says it's done.");
        progress?.Report(new("Done. Let the engine idle.", "", 1));

        var values = new List<KeyValuePair<string, double>> { new("Bank 1 at idle", idle.Bank1), new("Bank 1 at 2,500 rpm", load.Bank1) };
        if (idle.Bank2 is { } i2 && load.Bank2 is { } l2) { values.Add(new("Bank 2 at idle", i2)); values.Add(new("Bank 2 at 2,500 rpm", l2)); }
        return new TestReport(GuidedAnalysis.FuelTrims(idle, load), values, "%");
    }

    public static async Task<TestReport> VanosAsync(ISignalSource signals, IProgress<TestProgress>? progress, CancellationToken ct, double speed = 1)
    {
        if (await Ready(signals, progress, ct) is { } stop) return stop;
        string[] wanted = ["rpm", "vanos_in_target", "vanos_in_actual", "vanos_ex_target", "vanos_ex_actual"];
        var intake = new List<(double, double)>(); var exhaust = new List<(double, double)>();
        const int ticks = 60;
        for (var tick = 0; tick < ticks; tick++)
        {
            var v = await signals.ReadAsync(wanted, ct);
            if (v.TryGetValue("vanos_in_target", out var it) && v.TryGetValue("vanos_in_actual", out var ia)) intake.Add((it, ia));
            if (v.TryGetValue("vanos_ex_target", out var et) && v.TryGetValue("vanos_ex_actual", out var ea)) exhaust.Add((et, ea));
            var instruction = tick < 16 ? "Let the engine idle." : "Blip the throttle to about 3,000 rpm and let it drop, a few times.";
            progress?.Report(new(instruction, $"{v.GetValueOrDefault("rpm"):0} rpm", (double)tick / ticks));
            await Wait(speed, ct);
        }
        if (intake.Count == 0 && exhaust.Count == 0)
            return TestReport.Stop("VANOS positions aren't available", "This engine computer didn't report camshaft target and actual positions with this connection. They need BMW's measurement values: download the engine readings in the Garage, or connect with your BMW files.");
        var findings = new List<TestFinding>();
        if (intake.Count > 0) findings.Add(GuidedAnalysis.Vanos("Intake", intake));
        if (exhaust.Count > 0) findings.Add(GuidedAnalysis.Vanos("Exhaust", exhaust));
        var values = new List<KeyValuePair<string, double>>();
        if (intake.Count > 0) values.Add(new("Intake average deviation", intake.Average(s => Math.Abs(s.Item1 - s.Item2))));
        if (exhaust.Count > 0) values.Add(new("Exhaust average deviation", exhaust.Average(s => Math.Abs(s.Item1 - s.Item2))));
        return new TestReport(findings, values, "°");
    }

    /// <summary>Reads a short "does the start chain work?" picture, then routes the stored faults. The immobiliser is never queried.</summary>
    public static async Task<TestReport> NoStartAsync(ISignalSource signals, IProgress<TestProgress>? progress, CancellationToken ct, double speed = 1,
        Func<CancellationToken, Task<IReadOnlyList<NoStartTriage.ModuleFault>>>? faultProvider = null)
    {
        double minV = 99, maxRpm = 0; var ticks = 40;
        for (var tick = 0; tick < ticks; tick++)
        {
            var v = await signals.ReadAsync(["voltage", "rpm"], ct);
            if (v.TryGetValue("voltage", out var volt) && volt > 1) minV = Math.Min(minV, volt);
            if (v.TryGetValue("rpm", out var rpm)) maxRpm = Math.Max(maxRpm, rpm);
            progress?.Report(new("Turn the key to start now (crank it) if you can, while this runs.", $"lowest voltage {(minV < 99 ? minV.ToString("0.0") : "?")} V, highest rpm {maxRpm:0}", (double)tick / ticks));
            await Wait(speed, ct);
        }

        var values = new List<KeyValuePair<string, double>>();
        if (minV < 99) values.Add(new("Lowest voltage seen", minV));
        values.Add(new("Highest engine speed seen", maxRpm));

        IReadOnlyList<NoStartTriage.ModuleFault> faults = [];
        if (faultProvider is not null)
        {
            progress?.Report(new("Reading the start-chain fault memories...", "", 1));
            try { faults = await faultProvider(ct); }
            catch (Exception e) when (e is DiagnosticException or TimeoutException or NotSupportedException) { }
        }
        bool? cranked = maxRpm > 200 ? true : maxRpm > 0 ? false : null;
        var findings = NoStartTriage.Assess(minV < 99 ? minV : null, cranked, faults).ToList();
        return new TestReport(findings, values, "");
    }

    /// <summary>Seconds the engine computer's bleed routine runs (BMW's documented cycle is about 12 minutes).</summary>
    public const int BleedSeconds = 12 * 60;

    /// <summary>
    /// Watches the engine computer's own bleed routine, which the driver starts with the accelerator (engine off,
    /// pedal to the floor for about 10 seconds). Klemme15 sends nothing to the car: the pump is driven by the engine computer.
    /// </summary>
    public static async Task<TestReport> CoolingBleedAsync(ISignalSource signals, IProgress<TestProgress>? progress, CancellationToken ct, double speed = 1)
    {
        string[] wanted = ["rpm", "pump_speed", "pump_target", "coolant", "voltage"];
        progress?.Report(new("Checking the car...", "", 0));
        var first = await signals.ReadAsync(wanted, ct);
        if (first.TryGetValue("rpm", out var rpm0) && rpm0 > 300)
            return TestReport.Stop("Switch the engine off first", "The bleed routine runs with the engine off and the ignition on. Switch the engine off, turn the ignition back on (without starting) and run this again.");
        if (first.TryGetValue("coolant", out var hot) && hot > 60)
            return TestReport.Stop("Let the engine cool down first", $"Coolant is {hot:0} °C. Bleed a cool engine: the cap must never be opened hot, and the routine is meant for a cold system.");
        var canSeePump = first.ContainsKey("pump_speed") || first.ContainsKey("pump_target");

        bool Running(IReadOnlyDictionary<string, double> v) =>
            v.TryGetValue("pump_speed", out var s) && s > 5 || v.TryGetValue("pump_target", out var t) && t > 5;

        // Phase 1: the driver starts the routine with the pedal.
        var ticksPerSecond = 1000 / Tick.TotalMilliseconds;
        var startTicks = (int)(90 * ticksPerSecond);
        var started = false;
        var minVolt = first.TryGetValue("voltage", out var v0) ? v0 : 99;
        for (var tick = 0; tick < startTicks && !started; tick++)
        {
            var v = await signals.ReadAsync(wanted, ct);
            if (v.TryGetValue("voltage", out var volt) && volt > 1) minVolt = Math.Min(minVolt, volt);
            if (v.TryGetValue("rpm", out var rpm) && rpm > 300)
                return TestReport.Stop("The engine was started", "The bleed routine only runs with the engine off. Switch it off, ignition on, and start again.");
            started = canSeePump ? Running(v) : tick >= 15 * ticksPerSecond;
            progress?.Report(new("Press the accelerator pedal fully to the floor and hold it for 10 seconds, then let go.",
                canSeePump ? $"Waiting for the pump to start... ({(int)(tick / ticksPerSecond)} s)" : "Klemme15 can't see the pump on this connection: listen for it at the front of the engine.",
                0.05 * tick / startTicks));
            await Wait(speed, ct);
        }
        if (!started)
            return new TestReport([new(TestSeverity.Problem, "The bleed routine didn't start",
                "The pump never started. Check: engine off but ignition really on (dashboard lit), pedal held fully down for 10 seconds, no fault stored for the water pump. Switch the ignition off for a minute and try again; if it never starts, test the pump itself (Service > Test the electric water pump) and check its connector.")], []);

        // Phase 2: the routine runs on its own for about 12 minutes.
        var total = (int)(BleedSeconds * ticksPerSecond);
        var runningTicks = 0; var stoppedFor = 0; double sumSpeed = 0; var speedSamples = 0;
        double maxCoolant = first.GetValueOrDefault("coolant");
        var tickDone = 0;
        for (; tickDone < total; tickDone++)
        {
            var v = await signals.ReadAsync(wanted, ct);
            if (v.TryGetValue("voltage", out var volt) && volt > 1) minVolt = Math.Min(minVolt, volt);
            if (v.TryGetValue("coolant", out var c)) maxCoolant = Math.Max(maxCoolant, c);
            if (v.TryGetValue("pump_speed", out var ps)) { sumSpeed += ps; speedSamples++; }
            var on = !canSeePump || Running(v);
            if (on) { runningTicks++; stoppedFor = 0; } else stoppedFor++;
            if (canSeePump && stoppedFor > 30 * ticksPerSecond) break; // the routine ended by itself
            var left = (int)((total - tickDone) / ticksPerSecond);
            progress?.Report(new("Leave the ignition on. Top up the expansion tank if the level drops; don't start the engine.",
                $"{left / 60}:{left % 60:00} left" + (v.TryGetValue("pump_speed", out var sp) ? $" · pump {sp:0}" : "") + (v.TryGetValue("voltage", out var vv) ? $" · {vv:0.0} V" : ""),
                0.05 + 0.95 * tickDone / total));
            await Wait(speed, ct);
        }
        progress?.Report(new("Done. Check the coolant level.", "", 1));

        var minutes = runningTicks / ticksPerSecond / 60;
        var findings = new List<TestFinding>();
        if (!canSeePump)
            findings.Add(new(TestSeverity.Unknown, "The 12-minute cycle is over",
                "Klemme15 couldn't read the pump speed on this connection (download the engine readings in the Garage to see it next time). If you heard the pump run the whole time, the cycle worked."));
        else if (minutes >= 10)
            findings.Add(new(TestSeverity.Ok, $"The pump ran the bleed cycle ({minutes:0} minutes)", "The engine computer completed its bleed routine."));
        else
            findings.Add(new(TestSeverity.Watch, $"The pump only ran {minutes:0.0} minutes",
                "The routine ended early. Usually the ignition went off, the voltage dropped or the routine was interrupted. Wait 5 minutes with the ignition off, top up, and run it again."));
        if (minVolt < 12.0)
            findings.Add(new(TestSeverity.Watch, $"The battery dropped to {minVolt:0.0} V", "Twelve minutes with the ignition on drains the battery. Connect a charger before repeating the cycle."));
        findings.Add(new(TestSeverity.Unknown, "Finish the job",
            "Top up the expansion tank to the MAX mark. If you still hear gurgling behind the dashboard, or the level kept dropping, wait 5 minutes with the ignition off and run the cycle again. Then start the engine, let it warm up with the heater on, and check for leaks and the level once it has cooled."));
        var values = new List<KeyValuePair<string, double>> { new("Minutes the pump ran", Math.Round(minutes, 1)) };
        if (speedSamples > 0) values.Add(new("Average pump speed", Math.Round(sumSpeed / speedSamples, 0)));
        if (minVolt < 99) values.Add(new("Lowest battery voltage", Math.Round(minVolt, 1)));
        return new TestReport(findings, values);
    }

    public static async Task<TestReport> RoughnessAsync(ISignalSource signals, IProgress<TestProgress>? progress, CancellationToken ct, double speed = 1)
    {
        if (await Ready(signals, progress, ct) is { } stop) return stop;
        var names = Enumerable.Range(1, 8).Select(n => $"rough{n}").ToArray();
        var sums = new double[8]; var counts = new int[8];
        const int ticks = 40;
        for (var tick = 0; tick < ticks; tick++)
        {
            var v = await signals.ReadAsync(["rpm", .. names], ct);
            for (var i = 0; i < 8; i++)
                if (v.TryGetValue(names[i], out var d)) { sums[i] += d; counts[i]++; }
            progress?.Report(new("Let the engine idle. Don't touch the pedal.", $"{v.GetValueOrDefault("rpm"):0} rpm", (double)tick / ticks));
            await Wait(speed, ct);
        }
        var cylinders = Enumerable.Range(0, 8).Where(i => counts[i] > 0).ToList();
        if (cylinders.Count < 3)
            return TestReport.Stop("Per-cylinder values aren't available", "This engine computer didn't report per-cylinder roughness with this connection. It needs BMW's measurement values: download the engine readings in the Garage, or connect with your BMW files.");
        // Cylinders the engine doesn't have report nothing (or always zero) and are left out.
        var averages = cylinders.Select(i => sums[i] / counts[i]).ToList();
        var values = cylinders.Select((c, k) => new KeyValuePair<string, double>($"Cylinder {c + 1}", averages[k])).ToList();
        return new TestReport(GuidedAnalysis.Roughness(averages), values);
    }
}
