using System.Text.RegularExpressions;

namespace Klemme15.Core;

/// <summary>Several faults that most likely share one cause.</summary>
/// <param name="Faults">"FRM A8C5" for each fault in the group.</param>
public sealed record CauseGroup(string Title, string Detail, IReadOnlyList<string> Faults);

/// <summary>
/// Looks across every module's faults for the patterns that mean one cause, not many: a low-voltage event, several
/// modules losing contact with the same one, or faults recorded at the same mileage. Deliberately simple rules,
/// worded as "likely", so ten codes don't turn into ten repairs.
/// </summary>
public static partial class RootCause
{
    /// <summary>Faults recorded within this distance of each other happened together.</summary>
    public const long SameMomentKm = 8;

    public static IReadOnlyList<CauseGroup> Find(VehicleScanResult scan)
    {
        var all = scan.Ecus.Where(e => e.Status is EcuScanStatus.Ok or EcuScanStatus.Faults)
            .SelectMany(e => e.Faults.Where(f => f.Source == FaultSource.Bmw).Select(f => (e.Ecu, Fault: f))).ToList();
        var groups = new List<CauseGroup>();
        var used = new HashSet<Fault>();
        static string Short(EcuDefinition e) => e.Id.Contains('.') ? e.Id[(e.Id.LastIndexOf('.') + 1)..] : e.Name;
        static string Label((EcuDefinition Ecu, Fault Fault) x) => $"{Short(x.Ecu)} {x.Fault.Code}";
        static bool Several(List<(EcuDefinition Ecu, Fault Fault)> g) => g.Count >= 3 && g.Select(x => x.Ecu.Id).Distinct().Count() >= 2;
        string Modules(List<(EcuDefinition Ecu, Fault Fault)> g) => Join(g.Select(x => x.Ecu.Name).Distinct().ToList());

        // 1. One low-voltage event.
        var supply = all.Where(x => FaultTriage.IsSupply(x.Fault)).ToList();
        if (Several(supply))
        {
            var volts = scan.BatteryVoltage is { } v ? $" The battery reads {v:0.0} V now." : "";
            groups.Add(new($"{supply.Count} faults point at one low-voltage event",
                $"{Modules(supply)} all logged low or interrupted supply voltage{SameMileage(supply)}. That is usually one flat battery, a jump start or battery work, not {supply.Count} separate problems. Check the battery and charging first, then clear them and see what returns.{volts}",
                supply.Select(Label).ToList()));
            used.UnionWith(supply.Select(x => x.Fault));
        }

        // 2. Several modules lost contact, possibly with the same one.
        var bus = all.Where(x => !used.Contains(x.Fault) && FaultTriage.IsBus(x.Fault)).ToList();
        if (Several(bus))
        {
            // A module that two or more others name in their fault text is the one to look at.
            var named = scan.Ecus.Select(e => e.Ecu).Where(e => Short(e).Length >= 3)
                .Select(e => (Ecu: e, By: bus.Where(x => x.Ecu.Id != e.Id && Regex.IsMatch($"{x.Fault.Description} {x.Fault.OfficialText}", $@"\b{Regex.Escape(Short(e))}\b", RegexOptions.IgnoreCase)).ToList()))
                .Where(n => n.By.Select(x => x.Ecu.Id).Distinct().Count() >= 2)
                .OrderByDescending(n => n.By.Count).FirstOrDefault();
            if (named.Ecu is { } target)
            {
                var answered = scan.Ecus.FirstOrDefault(e => e.Ecu.Id == target.Id)?.Status is EcuScanStatus.Ok or EcuScanStatus.Faults;
                groups.Add(new($"{named.By.Select(x => x.Ecu.Id).Distinct().Count()} modules lost contact with {target.Name}",
                    $"{Modules(named.By)} each report missing messages from {Short(target)}{SameMileage(named.By)}. Look at {Short(target)} first (its fuse, ground and connector) rather than at each module that complains. " +
                    (answered ? $"{Short(target)} answers now, so the fault was likely temporary: a voltage dip or a loose connector." : $"{Short(target)} didn't answer the quick test, which fits."),
                    named.By.Select(Label).ToList()));
                used.UnionWith(named.By.Select(x => x.Fault));
                bus = bus.Where(x => !used.Contains(x.Fault)).ToList();
            }
            if (Several(bus))
            {
                groups.Add(new($"{bus.Count} communication faults in {bus.Select(x => x.Ecu.Id).Distinct().Count()} modules",
                    $"{Modules(bus)} logged missing messages or bus faults{SameMileage(bus)}. These are usually stored together when the battery was disconnected or ran low, or a module was asleep. Clear them and investigate only what returns.",
                    bus.Select(Label).ToList()));
                used.UnionWith(bus.Select(x => x.Fault));
            }
        }

        // 3. Whatever is left that was recorded at the same mileage.
        var dated = all.Where(x => !used.Contains(x.Fault)).Select(x => (x.Ecu, x.Fault, Km: Km(x.Fault))).Where(x => x.Km is not null)
            .OrderBy(x => x.Km).ToList();
        for (var i = 0; i < dated.Count;)
        {
            var j = i;
            while (j + 1 < dated.Count && dated[j + 1].Km - dated[j].Km <= SameMomentKm) j++;
            var cluster = dated.GetRange(i, j - i + 1).Select(x => (x.Ecu, x.Fault)).ToList();
            if (Several(cluster))
                groups.Add(new($"{cluster.Count} faults were recorded at about {DisplayUnits.Distance(dated[i].Km!.Value)}",
                    $"{Modules(cluster)} stored these within a few kilometres of each other, so they most likely happened together. Look for one cause they share (a voltage dip, one sensor several modules use, water in a connector) before treating them separately.",
                    cluster.Select(Label).ToList()));
            i = j + 1;
        }
        return groups;
    }

    private static long? Km(Fault f) => f.Details.TryGetValue("Mileage", out var t) ? MileageReader.Parse(t) : null;

    /// <summary>", all at about 98,112 km" when every fault in the group carries (nearly) the same mileage.</summary>
    private static string SameMileage(List<(EcuDefinition Ecu, Fault Fault)> g)
    {
        var km = g.Select(x => Km(x.Fault)).ToList();
        return km.All(k => k is not null) && km.Max() - km.Min() <= SameMomentKm ? $", all at about {DisplayUnits.Distance(km.Min()!.Value)}" : "";
    }

    private static string Join(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        <= 4 => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
        _ => string.Join(", ", names.Take(3)) + $" and {names.Count - 3} more modules",
    };
}
