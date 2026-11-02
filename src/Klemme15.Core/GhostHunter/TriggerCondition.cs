using System.Globalization;
using System.Text.Json.Serialization;

namespace Klemme15.Core.GhostHunter;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TriggerOperator { Above, AtOrAbove, Below, AtOrBelow, Equal, NotEqual }

/// <summary>
/// One condition on one live value: "rpm above 4000", "voltage below 11.5 for 1 second". The value has to hold for
/// <see cref="HoldDuration"/> (measured on the readings' own timestamps) before it counts, so a single noisy reading
/// can't fire a capture.
/// </summary>
public sealed record TriggerCondition
{
    /// <summary>A live signal id, e.g. "rpm", "coolant", "voltage", "rough3", "stft1".</summary>
    public required string Parameter { get; init; }
    public required TriggerOperator Operator { get; init; }
    public required double Threshold { get; init; }
    public TimeSpan HoldDuration { get; init; } = TimeSpan.FromSeconds(1);

    public bool Test(double value) => Operator switch
    {
        TriggerOperator.Above => value > Threshold,
        TriggerOperator.AtOrAbove => value >= Threshold,
        TriggerOperator.Below => value < Threshold,
        TriggerOperator.AtOrBelow => value <= Threshold,
        TriggerOperator.Equal => Math.Abs(value - Threshold) < 1e-9,
        TriggerOperator.NotEqual => Math.Abs(value - Threshold) >= 1e-9,
        _ => false,
    };

    public static string Symbol(TriggerOperator op) => op switch
    {
        TriggerOperator.Above => ">", TriggerOperator.AtOrAbove => ">=", TriggerOperator.Below => "<",
        TriggerOperator.AtOrBelow => "<=", TriggerOperator.Equal => "=", _ => "!=",
    };

    public override string ToString() =>
        $"{Parameter} {Symbol(Operator)} {Threshold.ToString(CultureInfo.InvariantCulture)}" +
        (HoldDuration > TimeSpan.Zero ? $" for {HoldDuration.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s" : "");
}

/// <summary>
/// A named rule: all of its conditions must hold at once (AND). When it fires, the monitor keeps <see cref="Before"/>
/// of history and records <see cref="After"/> more, then writes a snapshot.
/// </summary>
public sealed record TriggerRule
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyList<TriggerCondition> Conditions { get; init; }
    public TimeSpan Before { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan After { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>After a capture, the rule rests this long, so one long event doesn't produce a burst of files.</summary>
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Captures per drive, so a stuck condition can't fill the disk.</summary>
    public int MaxCaptures { get; init; } = 5;

    public IEnumerable<string> Parameters => Conditions.Select(c => c.Parameter).Distinct();

    public string Describe() => string.Join(" and ", Conditions.Select(c => c.ToString()));

    /// <summary>Problems that stop the rule from being armed (empty when it's fine).</summary>
    public IReadOnlyList<string> Validate(IReadOnlySet<string>? available = null)
    {
        var problems = new List<string>();
        if (Conditions.Count == 0) problems.Add("Add at least one condition.");
        foreach (var c in Conditions)
        {
            if (string.IsNullOrWhiteSpace(c.Parameter)) problems.Add("A condition has no value picked.");
            else if (available is not null && !available.Contains(c.Parameter)) problems.Add($"This car doesn't report {c.Parameter} on this connection.");
            if (double.IsNaN(c.Threshold) || double.IsInfinity(c.Threshold)) problems.Add($"{c.Parameter}: the threshold isn't a number.");
            if (c.HoldDuration < TimeSpan.Zero || c.HoldDuration > TimeSpan.FromMinutes(5)) problems.Add($"{c.Parameter}: hold between 0 and 300 seconds.");
        }
        if (Before < TimeSpan.Zero || Before > TimeSpan.FromSeconds(120) || After < TimeSpan.Zero || After > TimeSpan.FromSeconds(120))
            problems.Add("Keep between 0 and 120 seconds before and after.");
        if (MaxCaptures is < 1 or > 50) problems.Add("Between 1 and 50 captures per drive.");
        return problems;
    }
}

/// <summary>Ready-made rules for the faults owners chase most. They only use values the engine computer reports.</summary>
public static class TriggerPresets
{
    private static TriggerCondition C(string p, TriggerOperator op, double t, double hold = 1) =>
        new() { Parameter = p, Operator = op, Threshold = t, HoldDuration = TimeSpan.FromSeconds(hold) };

    public static readonly IReadOnlyList<TriggerRule> All =
    [
        new() { Id = "voltage-dip", Title = "Battery voltage dip (random dash warnings)", Conditions = [C("voltage", TriggerOperator.Below, 11.5, 0.5)] },
        new() { Id = "overheat", Title = "Running hot", Conditions = [C("coolant", TriggerOperator.Above, 108, 2)] },
        new() { Id = "lean-spike", Title = "Lean spike under way (bank 1)", Conditions = [C("rpm", TriggerOperator.Above, 1500, 0), C("stft1", TriggerOperator.Above, 15, 2)] },
        new() { Id = "rough-high-rpm", Title = "Cylinder 3 rough at high rpm (misfire hunting)", Conditions = [C("rpm", TriggerOperator.Above, 4000, 0), C("rough3", TriggerOperator.Above, 2.5, 0.5)] },
        new() { Id = "charging-low", Title = "Not charging while driving", Conditions = [C("rpm", TriggerOperator.Above, 1000, 0), C("voltage", TriggerOperator.Below, 13.0, 3)] },
    ];
}
