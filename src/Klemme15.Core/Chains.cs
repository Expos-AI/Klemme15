using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Klemme15.Core;

// Community diagnostic chains: a mechanic's reasoning written as data. A chain can only read live values, ask the
// person something, wait for a condition, calculate, compare and conclude. It can't switch anything on or write
// anything: when a part needs driving, it points to the existing test, which keeps its own checks.

/// <summary>One step of a chain. Which fields matter depends on <see cref="Type"/>.</summary>
public sealed record ChainStep
{
    /// <summary>"require", "ask", "measure", "compute" or "suggest".</summary>
    public required string Type { get; init; }

    /// <summary>require: the signal to check; measure: (unused, see Signals).</summary>
    public string? Signal { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }

    /// <summary>require: what to tell the person when the check fails (the chain stops).</summary>
    public string? Stop { get; init; }

    /// <summary>require: when true, a signal that can't be read doesn't stop the chain.</summary>
    public bool IfAvailable { get; init; }

    /// <summary>measure / compute / ask: the name results are stored under (e.g. "idle" → idle.rpm, idle.trim1).</summary>
    public string? Id { get; init; }

    /// <summary>measure: what the person should do while it measures; ask: the question.</summary>
    public string? Say { get; init; }

    /// <summary>measure: values to average. Names are signals ("rpm", "maf"), or sums like "stft1+ltft1".</summary>
    public IReadOnlyList<string> Signals { get; init; } = [];

    /// <summary>measure: values averaged when the engine computer reports them, without holding up the measurement.</summary>
    public IReadOnlyList<string> Optional { get; init; } = [];

    /// <summary>measure: only count readings while this signal is in [Min, Max] (e.g. rpm 2,200-2,900).</summary>
    public string? While { get; init; }

    public int Samples { get; init; } = 20;
    public int TimeoutSeconds { get; init; } = 60;

    /// <summary>compute: the expression whose value is stored under Id.</summary>
    public string? Expr { get; init; }

    /// <summary>ask: the answers offered; the chosen value is stored under Id as a number (its index) and text.</summary>
    public IReadOnlyList<ChainChoice> Choices { get; init; } = [];

    /// <summary>suggest: the test to run next (by id) and why; only shown, never started by the chain.</summary>
    public string? Test { get; init; }

    /// <summary>Only run this step when the expression is true (e.g. "idle.trim1 > 8").</summary>
    public string? When { get; init; }
}

public sealed record ChainChoice(string Value, string Label);

/// <summary>A conclusion the chain can reach. The first ones whose condition holds are reported.</summary>
public sealed record ChainVerdict
{
    public required string When { get; init; }
    public TestSeverity Severity { get; init; } = TestSeverity.Watch;
    public required string Title { get; init; }

    /// <summary>The explanation. {name} or {name:0.0} inserts a measured or computed value.</summary>
    public required string Text { get; init; }
}

/// <summary>A whole chain, as contributors write it in db/chains.json.</summary>
public sealed record DiagnosticChain
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public string Requirements { get; init; } = "";
    public string Duration { get; init; } = "";

    /// <summary>Fault codes (BMW or OBD-II) this chain is offered for.</summary>
    public IReadOnlyList<string> Faults { get; init; } = [];
    public IReadOnlyList<string> Sources { get; init; } = [];
    public required IReadOnlyList<ChainStep> Steps { get; init; }
    public required IReadOnlyList<ChainVerdict> Verdicts { get; init; }

    /// <summary>Shown when no verdict's condition holds.</summary>
    public ChainVerdict? Otherwise { get; init; }

    /// <summary>Only the first matching verdicts up to this many are reported (most specific first).</summary>
    public int MaxVerdicts { get; init; } = 2;

    public GuidedTestInfo Info => new(Id, Title, Summary, Requirements, Duration);

    /// <summary>Problems a contributor should fix before a chain ships (empty when it's fine).</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        var known = new HashSet<string>(StringComparer.Ordinal) { "displacement" };
        foreach (var (s, i) in Steps.Select((s, i) => (s, i + 1)))
        {
            void Expr(string? e, string what)
            {
                if (e is null) return;
                try { ChainExpression.Parse(e).CheckNames(known); }
                catch (ChainExpressionException x) { problems.Add($"step {i} {what}: {x.Message}"); }
            }
            Expr(s.When, "when");
            switch (s.Type)
            {
                case "require" when s.Signal is null: problems.Add($"step {i}: require needs a signal"); break;
                case "measure" when s.Id is null || s.Signals.Count == 0: problems.Add($"step {i}: measure needs an id and signals"); break;
                case "measure":
                    foreach (var sig in s.Signals.Concat(s.Optional)) { known.Add($"{s.Id}.{Name(sig)}"); known.Add($"{s.Id}.{Name(sig)}.min"); known.Add($"{s.Id}.{Name(sig)}.max"); }
                    known.Add($"{s.Id}.count");
                    break;
                case "compute" when s.Id is null || s.Expr is null: problems.Add($"step {i}: compute needs an id and expr"); break;
                case "compute": Expr(s.Expr, "expr"); known.Add(s.Id); break;
                case "ask" when s.Id is null || s.Choices.Count < 2: problems.Add($"step {i}: ask needs an id and at least two choices"); break;
                case "ask": known.Add(s.Id); foreach (var c in s.Choices) known.Add($"{s.Id}.{c.Value}"); break;
                case "require": if (s.Signal is { } sig2) known.Add(Name(sig2)); break;
                case "suggest": break;
                default: problems.Add($"step {i}: unknown type '{s.Type}'"); break;
            }
        }
        foreach (var v in Verdicts.Append(Otherwise).OfType<ChainVerdict>())
            try { ChainExpression.Parse(v.When).CheckNames(known); }
            catch (ChainExpressionException x) { problems.Add($"verdict '{v.Title}': {x.Message}"); }
        return problems;
    }

    /// <summary>"stft1+ltft1" → "stft1_ltft1": the name a measured sum is stored under.</summary>
    public static string Name(string signal) => signal.Replace("+", "_").Replace(" ", "");
}

/// <summary>Asks the person a question while a chain runs (the Tests page shows buttons; the command line a prompt).</summary>
public interface IChainPrompt
{
    Task<string> AskAsync(string question, IReadOnlyList<ChainChoice> choices, CancellationToken ct);
}

public static class ChainRunner
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(500);

    /// <param name="displacement">Engine size in litres, for chains that compare airflow with engine size (0 when unknown).</param>
    public static async Task<TestReport> RunAsync(DiagnosticChain chain, ISignalSource signals, IChainPrompt? prompt, IProgress<TestProgress>? progress,
        CancellationToken ct, double speed = 1, double displacement = 0)
    {
        var vars = new Dictionary<string, double>(StringComparer.Ordinal) { ["displacement"] = displacement };
        var graph = new TraceRecorder();
        var clock = 0;
        var findings = new List<TestFinding>();
        var values = new List<KeyValuePair<string, double>>();
        var total = Math.Max(1, chain.Steps.Count);

        for (var i = 0; i < chain.Steps.Count; i++)
        {
            var step = chain.Steps[i];
            var start = (double)i / total;
            if (step.When is { } when && !ChainExpression.Parse(when).IsTrue(vars)) continue;
            switch (step.Type)
            {
                case "require":
                {
                    progress?.Report(new("Checking the car...", "", start, graph.Snapshot()));
                    var read = await signals.ReadAsync([step.Signal!], ct);
                    if (!read.TryGetValue(step.Signal!, out var v))
                    {
                        if (step.IfAvailable) break;
                        return Stopped($"{step.Signal} can't be read", "This chain needs that value from the engine computer. Download the engine readings in the Garage, or connect with your BMW files, and run it again.");
                    }
                    vars[DiagnosticChain.Name(step.Signal!)] = v;
                    if (step.Min is { } min && v < min || step.Max is { } max && v > max)
                        return Stopped(step.Stop ?? $"{step.Signal} is out of range", $"{Label(step.Signal!)} is {v:0.#}. {step.Stop}");
                    break;
                }
                case "ask":
                {
                    if (prompt is null) return Stopped("This chain asks questions", "Run it on the Tests page, where it can ask you.");
                    progress?.Report(new(step.Say ?? "", "Waiting for your answer...", start, graph.Snapshot()));
                    var answer = await prompt.AskAsync(step.Say ?? "", step.Choices, ct);
                    var index = step.Choices.ToList().FindIndex(c => c.Value == answer);
                    vars[step.Id!] = index;
                    foreach (var (c, k) in step.Choices.Select((c, k) => (c, k))) vars[$"{step.Id}.{c.Value}"] = k == index ? 1 : 0;
                    break;
                }
                case "measure":
                {
                    var all = step.Signals.Concat(step.Optional).ToList();
                    var required = step.Signals.Count;
                    var gate = step.While ?? "rpm";
                    var needed = all.SelectMany(x => x.Split('+')).Select(x => x.Trim()).Append(gate).Distinct().ToList();
                    var sums = new double[all.Count];
                    var counts = new int[all.Count];
                    var mins = Enumerable.Repeat(double.MaxValue, all.Count).ToArray();
                    var maxs = Enumerable.Repeat(double.MinValue, all.Count).ToArray();
                    var count = 0;
                    var ticks = (int)(step.TimeoutSeconds * 1000 / Tick.TotalMilliseconds);
                    for (var t = 0; t < ticks && count < step.Samples; t++)
                    {
                        var read = await signals.ReadAsync(needed, ct);
                        var sec = clock++ * Tick.TotalSeconds;
                        var now = all.Select(x => Value(read, x)).ToList();
                        for (var k = 0; k < all.Count; k++) if (now[k] is { } gv) graph.Add(Label(all[k]), Unit(all[k]), sec, gv);
                        var hasGate = read.TryGetValue(gate, out var g);
                        var inBand = hasGate ? (step.Min is not { } lo || g >= lo) && (step.Max is not { } hi || g <= hi) : step.While is null;
                        if (inBand && now.Take(required).All(x => x is not null))
                        {
                            for (var k = 0; k < all.Count; k++)
                                if (now[k] is { } x) { sums[k] += x; counts[k]++; mins[k] = Math.Min(mins[k], x); maxs[k] = Math.Max(maxs[k], x); }
                            count++;
                        }
                        var gateText = hasGate ? $"{Label(gate)} {g:0.#}" : "";
                        progress?.Report(new(step.Say ?? "Measuring...", inBand ? $"{gateText} · measuring ({count}/{step.Samples})".TrimStart(' ', '·') : $"{gateText} · waiting for {step.Min:0.#}-{step.Max:0.#}",
                            start + (1.0 / total) * count / step.Samples, graph.Snapshot()));
                        await Task.Delay(TimeSpan.FromMilliseconds(Tick.TotalMilliseconds / speed), ct);
                    }
                    vars[$"{step.Id}.count"] = count;
                    if (count < Math.Max(1, step.Samples / 2))
                        return Stopped("Not enough steady readings", $"{step.Say} The values needed couldn't be read steadily enough. Try again, holding it steady until the test says it's done.");
                    for (var k = 0; k < all.Count; k++)
                    {
                        if (counts[k] == 0) continue;   // an optional value the engine computer didn't report
                        var name = DiagnosticChain.Name(all[k]);
                        vars[$"{step.Id}.{name}"] = sums[k] / counts[k];
                        vars[$"{step.Id}.{name}.min"] = mins[k];
                        vars[$"{step.Id}.{name}.max"] = maxs[k];
                        values.Add(new($"{Title(step.Id!)}: {Label(all[k])}", Math.Round(sums[k] / counts[k], 2)));
                    }
                    break;
                }
                case "compute":
                    vars[step.Id!] = ChainExpression.Parse(step.Expr!).Evaluate(vars);
                    break;
                case "suggest":
                    findings.Add(new(TestSeverity.Unknown, Fill(step.Say ?? "Next step", vars), Fill(step.Test ?? "", vars)));
                    break;
            }
        }

        progress?.Report(new("Done.", "", 1, graph.Snapshot()));
        var verdicts = chain.Verdicts.Where(v => ChainExpression.Parse(v.When).IsTrue(vars)).Take(chain.MaxVerdicts).ToList();
        if (verdicts.Count == 0 && chain.Otherwise is { } other) verdicts.Add(other);
        findings.InsertRange(0, verdicts.Select(v => new TestFinding(v.Severity, Fill(v.Title, vars), Fill(v.Text, vars))));
        return new TestReport(findings, values) { Traces = graph.Snapshot() };

        TestReport Stopped(string title, string why) => TestReport.Stop(title, why) with { Traces = graph.Snapshot() };
    }

    private static string Title(string id) => id.Length == 0 ? id : char.ToUpperInvariant(id[0]) + id[1..].Replace('_', ' ');

    /// <summary>A signal, or a sum like "stft1+ltft1" (null if any part is missing).</summary>
    private static double? Value(IReadOnlyDictionary<string, double> read, string signal)
    {
        double total = 0;
        foreach (var part in signal.Split('+'))
            if (read.TryGetValue(part.Trim(), out var v)) total += v; else return null;
        return total;
    }

    private static string Label(string signal) => signal switch
    {
        "rpm" => "Engine speed", "coolant" => "Coolant", "voltage" => "Battery voltage", "maf" => "Air mass", "load" => "Engine load",
        "iat" => "Intake air", "throttle" => "Throttle", "map" => "Manifold pressure",
        "stft1+ltft1" => "Bank 1 total trim", "stft2+ltft2" => "Bank 2 total trim",
        "o2b1s1" => "Oxygen sensor B1S1", "o2b1s2" => "Oxygen sensor B1S2",
        _ => signal,
    };

    private static string Unit(string signal) => signal switch
    {
        "rpm" => "rpm", "coolant" or "iat" => "°C", "voltage" or "o2b1s1" or "o2b1s2" => "V", "maf" => "g/s", "map" => "kPa",
        _ when signal.Contains("ft") => "%", "load" or "throttle" => "%",
        _ => "",
    };

    /// <summary>"{idle.trim1:+0.0} %" → "+12.3 %".</summary>
    public static string Fill(string text, IReadOnlyDictionary<string, double> vars) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\{([A-Za-z0-9_.]+)(?::([^}]+))?\}", m =>
            vars.TryGetValue(m.Groups[1].Value, out var v) ? v.ToString(m.Groups[2].Success ? m.Groups[2].Value : "0.#", CultureInfo.InvariantCulture) : m.Value);
}

/// <summary>Loads chains from JSON (db/chains.json, or a contributor's file). Broken chains are reported, not run.</summary>
public static class ChainLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static (IReadOnlyList<DiagnosticChain> Chains, IReadOnlyList<string> Problems) Load(string json)
    {
        var problems = new List<string>();
        var chains = new List<DiagnosticChain>();
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!doc.RootElement.TryGetProperty("chains", out var list) || list.ValueKind != JsonValueKind.Array) return (chains, ["no 'chains' array"]);
        foreach (var e in list.EnumerateArray())
        {
            DiagnosticChain? chain;
            try { chain = e.Deserialize<DiagnosticChain>(Options); }
            catch (JsonException x) { problems.Add($"a chain couldn't be read: {x.Message}"); continue; }
            if (chain is null) continue;
            var issues = chain.Validate();
            if (issues.Count > 0) { problems.AddRange(issues.Select(i => $"{chain.Id}: {i}")); continue; }
            chains.Add(chain);
        }
        return (chains, problems);
    }
}

public sealed class ChainExpressionException(string message) : Exception(message);

/// <summary>
/// The arithmetic a chain may use: numbers, names of measured values, + - * /, comparisons, and/or/not, and abs, min,
/// max. Nothing else: no access to files, the car or the app. Parsed once, evaluated against the chain's values.
/// </summary>
public abstract class ChainExpression
{
    public abstract double Evaluate(IReadOnlyDictionary<string, double> vars);
    public bool IsTrue(IReadOnlyDictionary<string, double> vars) => Evaluate(vars) != 0;
    public abstract void CheckNames(IReadOnlySet<string> known);

    private static readonly Dictionary<string, ChainExpression> Cache = new();

    public static ChainExpression Parse(string text)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(text, out var cached)) return cached;
            if (text.Length > 500) throw new ChainExpressionException("expression too long");
            var p = new Parser(text);
            var e = p.Or();
            p.End();
            return Cache[text] = e;
        }
    }

    private sealed class Number(double v) : ChainExpression
    {
        public override double Evaluate(IReadOnlyDictionary<string, double> vars) => v;
        public override void CheckNames(IReadOnlySet<string> known) { }
    }

    private sealed class Variable(string name) : ChainExpression
    {
        public override double Evaluate(IReadOnlyDictionary<string, double> vars) =>
            vars.TryGetValue(name, out var v) ? v : double.NaN;   // unknown → NaN, so comparisons with it are false
        public override void CheckNames(IReadOnlySet<string> known)
        {
            if (!known.Contains(name)) throw new ChainExpressionException($"'{name}' isn't measured, computed or asked before it's used");
        }
    }

    private sealed class Unary(string op, ChainExpression a) : ChainExpression
    {
        public override double Evaluate(IReadOnlyDictionary<string, double> vars) => op == "-" ? -a.Evaluate(vars) : a.Evaluate(vars) != 0 ? 0 : 1;
        public override void CheckNames(IReadOnlySet<string> known) => a.CheckNames(known);
    }

    private sealed class Binary(string op, ChainExpression a, ChainExpression b) : ChainExpression
    {
        public override double Evaluate(IReadOnlyDictionary<string, double> vars)
        {
            var x = a.Evaluate(vars);
            if (op == "and") return x != 0 && !double.IsNaN(x) && b.Evaluate(vars) is var y1 && y1 != 0 && !double.IsNaN(y1) ? 1 : 0;
            if (op == "or") return x != 0 && !double.IsNaN(x) || b.Evaluate(vars) is var y2 && y2 != 0 && !double.IsNaN(y2) ? 1 : 0;
            var y = b.Evaluate(vars);
            return op switch
            {
                "+" => x + y, "-" => x - y, "*" => x * y, "/" => y == 0 ? double.NaN : x / y,
                "<" => x < y ? 1 : 0, "<=" => x <= y ? 1 : 0, ">" => x > y ? 1 : 0, ">=" => x >= y ? 1 : 0,
                "==" => x == y ? 1 : 0, "!=" => x != y ? 1 : 0,
                _ => double.NaN,
            };
        }
        public override void CheckNames(IReadOnlySet<string> known) { a.CheckNames(known); b.CheckNames(known); }
    }

    private sealed class Call(string name, IReadOnlyList<ChainExpression> args) : ChainExpression
    {
        public override double Evaluate(IReadOnlyDictionary<string, double> vars)
        {
            var v = args.Select(a => a.Evaluate(vars)).ToList();
            // min and max skip values that weren't measured (an optional second bank on a four-cylinder engine).
            var known = v.Where(x => !double.IsNaN(x)).ToList();
            return name switch
            {
                "abs" => Math.Abs(v[0]),
                "min" => known.Count == 0 ? double.NaN : known.Min(),
                "max" => known.Count == 0 ? double.NaN : known.Max(),
                _ => double.NaN,
            };
        }
        public override void CheckNames(IReadOnlySet<string> known) { foreach (var a in args) a.CheckNames(known); }
    }

    private sealed class Parser(string text)
    {
        private int _i;

        private void Skip() { while (_i < text.Length && char.IsWhiteSpace(text[_i])) _i++; }

        private bool Take(string token)
        {
            Skip();
            if (string.CompareOrdinal(text, _i, token, 0, token.Length) != 0) return false;
            // Words ("and", "or", "not") must end at a word boundary.
            if (char.IsLetter(token[0]) && _i + token.Length < text.Length && (char.IsLetterOrDigit(text[_i + token.Length]) || text[_i + token.Length] is '_' or '.')) return false;
            _i += token.Length;
            return true;
        }

        public void End()
        {
            Skip();
            if (_i < text.Length) throw new ChainExpressionException($"unexpected '{text[_i..]}'");
        }

        public ChainExpression Or()
        {
            var e = And();
            while (Take("or") || Take("||")) e = new Binary("or", e, And());
            return e;
        }

        private ChainExpression And()
        {
            var e = Not();
            while (Take("and") || Take("&&")) e = new Binary("and", e, Not());
            return e;
        }

        private ChainExpression Not() => Take("not") || Take("!") && !(text.Length > _i && text[_i] == '=') ? new Unary("not", Not()) : Compare();

        private ChainExpression Compare()
        {
            var e = Sum();
            foreach (var op in new[] { "<=", ">=", "==", "!=", "<", ">" })
                if (Take(op)) return new Binary(op, e, Sum());
            return e;
        }

        private ChainExpression Sum()
        {
            var e = Product();
            while (true)
            {
                if (Take("+")) e = new Binary("+", e, Product());
                else if (Take("-")) e = new Binary("-", e, Product());
                else return e;
            }
        }

        private ChainExpression Product()
        {
            var e = Atom();
            while (true)
            {
                if (Take("*")) e = new Binary("*", e, Atom());
                else if (Take("/")) e = new Binary("/", e, Atom());
                else return e;
            }
        }

        private ChainExpression Atom()
        {
            Skip();
            if (Take("-")) return new Unary("-", Atom());
            if (Take("("))
            {
                var e = Or();
                if (!Take(")")) throw new ChainExpressionException("missing ')'");
                return e;
            }
            if (_i < text.Length && (char.IsDigit(text[_i]) || text[_i] == '.'))
            {
                var start = _i;
                while (_i < text.Length && (char.IsDigit(text[_i]) || text[_i] == '.')) _i++;
                return double.TryParse(text[start.._i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                    ? new Number(v) : throw new ChainExpressionException($"bad number '{text[start.._i]}'");
            }
            if (_i < text.Length && char.IsLetter(text[_i]))
            {
                var start = _i;
                while (_i < text.Length && (char.IsLetterOrDigit(text[_i]) || text[_i] is '_' or '.')) _i++;
                var name = text[start.._i];
                if (Take("("))
                {
                    if (name is not ("abs" or "min" or "max")) throw new ChainExpressionException($"unknown function '{name}' (only abs, min, max)");
                    var args = new List<ChainExpression> { Or() };
                    while (Take(",")) args.Add(Or());
                    if (!Take(")")) throw new ChainExpressionException("missing ')'");
                    if (name == "abs" && args.Count != 1) throw new ChainExpressionException("abs takes one value");
                    return new Call(name, args);
                }
                if (name is "true") return new Number(1);
                if (name is "false") return new Number(0);
                return new Variable(name);
            }
            throw new ChainExpressionException(_i < text.Length ? $"unexpected '{text[_i]}'" : "expression ends too early");
        }
    }
}
