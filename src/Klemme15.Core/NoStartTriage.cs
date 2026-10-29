namespace Klemme15.Core;

/// <summary>What a no-start points at, roughly in the order worth checking.</summary>
public enum NoStartCause { Battery, Starter, ElectricSteeringLock, Sensor, Fuel, Ignition, Immobiliser, Other }

/// <summary>
/// Turns what Klemme15 can see over the cable — battery voltage, whether the engine cranked, and the faults the start-chain
/// modules logged — into a routed answer for a car that won't start. It only reads; it never touches keys or the
/// immobiliser. An immobiliser conclusion is a signpost to a locksmith or dealer, not something Klemme15 acts on.
/// </summary>
public static class NoStartTriage
{
    /// <summary>A fault from one of the start-chain modules, tagged with that module's short name (DME, CAS, ELV...).</summary>
    public readonly record struct ModuleFault(string Module, Fault Fault);

    // Words in a fault's text (English or BMW German) that place it in a bucket. Checked in this order; first match wins.
    private static readonly (NoStartCause Cause, string[] Words)[] Buckets =
    [
        (NoStartCause.ElectricSteeringLock, ["steering lock", "steering-lock", "lenkungsverriegel", "lenkradverriegel", "elv ", "elv-", "elektrische lenk"]),
        (NoStartCause.Immobiliser, ["immobil", "wegfahrsperre", "ews", "drive-away", "engine start enable", "startfreigabe", "freigabe motor", "rolling code", "isn", "individualisierung"]),
        (NoStartCause.Starter, ["starter", "anlasser", "terminal 50", "kl.50", "kl 50", "klemme 50", "start relay", "starter relay", "startrelais", "crank relay"]),
        (NoStartCause.Sensor, ["crankshaft", "kurbelwelle", "camshaft", "nockenwelle", "crank sensor", "cam sensor", "drehzahlgeber", "ckp", "cmp", "position sensor"]),
        (NoStartCause.Fuel, ["fuel pump", "kraftstoffpumpe", "ekps", "rail pressure", "raildruck", "niederdruck", "hochdruck", "low pressure", "high pressure", "fuel supply", "kraftstoffversorg", "injector", "injektor", "einspritz"]),
        (NoStartCause.Ignition, ["ignition coil", "zündspule", "zuendspule", "ignition", "zündung", "zuendung", "misfire", "zündaussetzer"]),
    ];

    public static NoStartCause Classify(Fault fault)
    {
        var text = $"{fault.Description} {fault.OfficialText} {fault.Code}".ToLowerInvariant();
        foreach (var (cause, words) in Buckets)
            if (words.Any(text.Contains)) return cause;
        return NoStartCause.Other;
    }

    /// <summary>
    /// The verdict, most important first. <paramref name="minVoltage"/> null means voltage couldn't be read;
    /// <paramref name="cranked"/> whether the engine was seen turning (rpm above a cranking threshold).
    /// </summary>
    public static IReadOnlyList<TestFinding> Assess(double? minVoltage, bool? cranked, IReadOnlyList<ModuleFault> faults)
    {
        var findings = new List<TestFinding>();

        // 1. Power first: a weak battery causes most no-starts and fakes faults everywhere else.
        if (minVoltage is null)
            findings.Add(new(TestSeverity.Unknown, "Battery voltage couldn't be read",
                "Measure the battery with a meter: below about 12.2 V at rest, or dropping under 9-10 V while cranking, is the first thing to fix. Everything below is less reliable until the battery is known good."));
        else if (minVoltage < 11.5)
            findings.Add(new(TestSeverity.Problem, $"Battery voltage is low ({minVoltage:0.0} V)",
                "This alone can stop a car cranking or firing, and it throws confusing faults in other modules. Charge or replace the battery and clear the faults before chasing anything else."));
        else
            findings.Add(new(TestSeverity.Ok, $"Battery voltage looks healthy ({minVoltage:0.0} V)",
                "If it didn't sag while cranking, the battery and its main connections are probably fine."));

        var active = faults.Where(f => f.Fault.State == FaultState.Present || !f.Fault.IsInfo).ToList();
        var byCause = active.GroupBy(f => Classify(f.Fault)).ToDictionary(g => g.Key, g => g.ToList());
        TestFinding? For(NoStartCause c) => byCause.TryGetValue(c, out var fs)
            ? new(Severity(c), Headline(c, fs.Count), $"{Detail(c)} Codes: {string.Join(", ", fs.Select(f => $"{f.Module} {f.Fault.Code}").Distinct().Take(6))}.")
            : null;

        // 2. Did it crank? Distinguishes "no crank" causes from "cranks but won't fire" causes.
        var crankBlock = For(NoStartCause.ElectricSteeringLock) ?? For(NoStartCause.Immobiliser) ?? For(NoStartCause.Starter);
        if (cranked == false)
        {
            if (crankBlock is not null) findings.Add(crankBlock);
            else findings.Add(new(TestSeverity.Watch, "No cranking was seen, and no module blamed it",
                "Either you didn't crank during the test, or the engine isn't turning and no module logged why. Check the battery (above), the starter and its wiring, and — on automatics — that it's in P or N. If it cranks elsewhere, run this again while cranking."));
        }
        else if (cranked == true)
        {
            findings.Add(new(TestSeverity.Ok, "The engine is turning over",
                "The starter and its circuit work, so this is a spark/fuel/air problem, not a starter or steering-lock one."));
            foreach (var c in new[] { NoStartCause.Sensor, NoStartCause.Fuel, NoStartCause.Ignition })
                if (For(c) is { } f) findings.Add(f);
            // An immobiliser fault with the engine cranking still matters (it cuts fuel/spark), so surface it.
            if (For(NoStartCause.Immobiliser) is { } imm) findings.Add(imm);
        }
        else
        {
            // Cranking unknown: surface whatever the modules complained about, start-blockers first.
            foreach (var c in new[] { NoStartCause.ElectricSteeringLock, NoStartCause.Immobiliser, NoStartCause.Starter, NoStartCause.Sensor, NoStartCause.Fuel, NoStartCause.Ignition })
                if (For(c) is { } f) findings.Add(f);
        }

        if (For(NoStartCause.Other) is { } other && active.Count > 0 && !findings.Any(f => f.Severity == TestSeverity.Problem))
            findings.Add(other);

        if (active.Count == 0)
            findings.Add(new(TestSeverity.Unknown, "No start-chain faults were stored",
                "None of the engine, key or steering-lock modules logged a fault for the failed start. If it cranks but won't fire with a good battery, check fuel pressure and spark. If nothing happens at all, suspect the battery, starter or a main fuse."));

        return findings;
    }

    private static TestSeverity Severity(NoStartCause c) => c switch
    {
        NoStartCause.Immobiliser or NoStartCause.ElectricSteeringLock => TestSeverity.Watch,
        NoStartCause.Other => TestSeverity.Watch,
        _ => TestSeverity.Problem,
    };

    private static string Headline(NoStartCause c, int n) => c switch
    {
        NoStartCause.ElectricSteeringLock => "The electric steering lock reported a fault",
        NoStartCause.Immobiliser => "An immobiliser / start-enable fault is stored",
        NoStartCause.Starter => "A starter-circuit fault is stored",
        NoStartCause.Sensor => "A crank/cam sensor fault is stored",
        NoStartCause.Fuel => "A fuel-supply fault is stored",
        NoStartCause.Ignition => "An ignition fault is stored",
        _ => $"{n} other start-related fault(s) are stored",
    };

    private static string Detail(NoStartCause c) => c switch
    {
        NoStartCause.ElectricSteeringLock =>
            "On early cars with a key slot the steering lock can fail to release and block starting. Charge the battery first (a weak battery is the usual trigger), then Service > Steering lock: clear the fault counters. If it keeps coming back the lock itself is failing and needs replacing — a job for a workshop.",
        NoStartCause.Immobiliser =>
            "The key module isn't releasing the engine to start. Klemme15 does not touch keys or the immobiliser. Rule out the simple causes first — a flat or failing battery, and whether the key is detected at all — then take it to a dealer or an auto locksmith: re-syncing or key work there is deliberately outside this tool.",
        NoStartCause.Starter =>
            "The starter circuit is implicated. Check the battery and its grounds, the starter and its wiring, and the start relay. On automatics, confirm it's in P or N and the selector is seen.",
        NoStartCause.Sensor =>
            "The engine computer can't see the engine turning properly, so it won't fire. The crankshaft (and sometimes camshaft) position sensor or its wiring is the usual cause.",
        NoStartCause.Fuel =>
            "The engine is short of fuel. Check the fuel pump and its fuse/relay, then fuel pressure. Service > Test the fuel pump runs the pump so you can hear it.",
        NoStartCause.Ignition =>
            "Spark is implicated. Check the coils and their wiring; a single coil rarely stops a start, so look for a common feed or a crank-signal problem too.",
        _ => "These were logged around the failed start and may be related.",
    };
}
