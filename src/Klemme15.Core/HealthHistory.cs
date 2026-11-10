// Klemme15: open-source BMW diagnostics
// Copyright (C) 2026 Kindric Castro
//
// This program is free software: you can redistribute it and/or modify it under the terms of the GNU General
// Public License as published by the Free Software Foundation, either version 3 of the License, or (at your
// option) any later version. This program is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
// General Public License for more details. You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Klemme15.Core;

/// <summary>
/// A small summary of one scan of a real car, kept on the user's own computer so Klemme15 can show how the car changes
/// over time. It holds no VIN: cars are told apart by a one-way hash of it.
/// </summary>
public sealed record HealthSnapshot
{
    public required DateTimeOffset Time { get; init; }
    public required string CarKey { get; init; }
    public string? ModelId { get; init; }
    public string? ModelName { get; init; }
    public string? Chassis { get; init; }

    /// <summary>Approximate mileage, from the cluster's warning history (the newest entry's mileage).</summary>
    public long? Km { get; init; }

    public double? Voltage { get; init; }
    public double? Rpm { get; init; }
    public double? Coolant { get; init; }

    /// <summary>Long-term fuel trims in percent, when the engine computer reported them with the engine running.</summary>
    public double? Ltft1 { get; init; }
    public double? Ltft2 { get; init; }

    public int FixNow { get; init; }
    public int CheckIt { get; init; }
    public int Background { get; init; }

    /// <summary>"E9x.FZD:A670" for every fault that isn't background.</summary>
    public IReadOnlyList<string> Faults { get; init; } = [];

    /// <summary>Check Control IDs showing at the time.</summary>
    public IReadOnlyList<int> DashMessages { get; init; } = [];

    /// <summary>Each module's part number ("FRM" → "7004041"), so a swapped module is noticed at the next scan.</summary>
    public IReadOnlyDictionary<string, string> Parts { get; init; } = new Dictionary<string, string>();

    public bool EngineRunning => Rpm is > 400 || Rpm is null && Voltage is > 13.3;

    /// <summary>A stable, one-way id for a car (no VIN stored): the first 12 hex digits of SHA-256 of the VIN.</summary>
    public static string KeyFor(string vin) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(vin.Trim().ToUpperInvariant())))[..12].ToLowerInvariant();

    /// <summary>
    /// The car's history key. From the VIN when the car reports a full one; otherwise (many E46-era engine computers
    /// don't give the VIN over OBD) from the identification of its engine computer and cluster, which is fixed for a
    /// car until one of them is replaced. Null when neither is available. Never stores the VIN or identification.
    /// </summary>
    public static string? CarKeyFor(VehicleScanResult scan)
    {
        if (scan.Vin is { Length: 17 } vin) return KeyFor(vin);
        static string Short(string id) => id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id;
        var answered = scan.Ecus.Where(e => e.Identity is not null).ToList();
        // The engine computer and the cluster first: the two modules least often swapped.
        // Part numbers, not the raw replies: they read the same with or without BMW files, so the key doesn't change
        // when you switch connection mode.
        var core = answered.Where(e => (e.Ecu.IsEngine || Short(e.Ecu.Id) is "IKE" or "KOMBI") && e.Identity!.PartNumber is { Length: > 0 })
            .Select(e => $"{Short(e.Ecu.Id)}={e.Identity!.PartNumber}").Order(StringComparer.Ordinal).ToList();
        if (core.Count < 2)
            core = answered.Where(e => e.Identity!.PartNumber is { Length: > 0 })
                .Select(e => $"{Short(e.Ecu.Id)}={e.Identity!.PartNumber}").Order(StringComparer.Ordinal).ToList();
        if (core.Count < 2) return null;
        // A partial VIN (some engine computers give only the last digits) tells same-spec cars apart.
        var partial = scan.Vin is { Length: >= 7 } p ? p.Trim().ToUpperInvariant() : "";
        var text = $"{scan.Chassis}|{partial}|{string.Join("|", core)}";
        return "m" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..11].ToLowerInvariant();
    }

    public static HealthSnapshot? From(VehicleScanResult scan, string? modelId, string? modelName, IReadOnlyDictionary<string, double> engine)
    {
        if (scan.Cancelled || CarKeyFor(scan) is not { } key) return null;
        double? Get(string k) => engine.TryGetValue(k, out var v) ? Math.Round(v, 2) : null;
        var all = scan.Ecus.SelectMany(e => e.Faults.Select(f => (e.Ecu, Fault: f, Verdict: FaultTriage.Assess(f)))).ToList();
        return new HealthSnapshot
        {
            Time = scan.Started,
            CarKey = key,
            ModelId = modelId,
            ModelName = modelName,
            Chassis = scan.Chassis,
            Km = scan.Story?.LastKm,
            Voltage = Get("voltage") ?? scan.BatteryVoltage,
            Rpm = Get("rpm"),
            Coolant = Get("coolant"),
            Ltft1 = Get("ltft1"),
            Ltft2 = Get("ltft2"),
            FixNow = all.Count(x => x.Verdict.Priority == FaultPriority.FixNow),
            CheckIt = all.Count(x => x.Verdict.Priority == FaultPriority.CheckIt),
            Background = all.Count(x => x.Verdict.Priority == FaultPriority.Background),
            Faults = all.Where(x => x.Verdict.Priority != FaultPriority.Background).Select(x => $"{x.Ecu.Id}:{x.Fault.Code}").Distinct().ToList(),
            DashMessages = scan.CheckControl.Where(m => m.Active).Select(m => m.Id).ToList(),
            Parts = scan.Ecus.Where(e => e.Identity?.PartNumber is { Length: > 0 })
                .GroupBy(e => e.Ecu.Id.Contains('.') ? e.Ecu.Id[(e.Ecu.Id.LastIndexOf('.') + 1)..] : e.Ecu.Id)
                .ToDictionary(g => g.Key, g => g.First().Identity!.PartNumber!),
        };
    }
}

/// <summary>One finished guided test, kept with the car's health history so a part wearing out shows as a trend.</summary>
public sealed record TestRecord
{
    public required DateTimeOffset Time { get; init; }
    public required string TestId { get; init; }
    public required string Title { get; init; }
    public required TestSeverity Verdict { get; init; }
    public string Summary { get; init; } = "";
    public IReadOnlyDictionary<string, double> Values { get; init; } = new Dictionary<string, double>();

    public static TestRecord From(GuidedTestInfo test, TestReport report, DateTimeOffset time) => new()
    {
        Time = time,
        TestId = test.Id,
        Title = test.Title,
        Verdict = report.Verdict,
        Summary = report.Findings.FirstOrDefault(f => f.Severity == report.Verdict)?.Title ?? "",
        Values = report.Values.GroupBy(v => v.Key).ToDictionary(g => g.Key, g => Math.Round(g.First().Value, 2)),
    };
}

public enum TrendLevel { Info, Watch, Act }

/// <summary>What the health history says, attached to a scan so pages and reports can show it.</summary>
public sealed record HealthSummary(int Scans, DateTimeOffset Since, IReadOnlyList<HealthTrend> Trends, bool MadeUp = false)
{
    public static HealthSummary? From(IReadOnlyList<HealthSnapshot> history, Func<int, string>? ccName = null, bool madeUp = false,
        IReadOnlyList<TestRecord>? tests = null) =>
        history.Count == 0 ? null : new(history.Count, history[0].Time,
            HealthTrends.Analyze(history, ccName).Concat(HealthTrends.AnalyzeTests(tests ?? [])).OrderByDescending(t => t.Level).ToList(), madeUp);
}

/// <summary>Something Klemme15 noticed by comparing scans over time.</summary>
public sealed record HealthTrend(TrendLevel Level, string Title, string Detail);

/// <summary>
/// Reads a car's snapshots (oldest first) and points out what's changing before it becomes a fault: a battery that
/// rests lower each time, charging that's off, fuel trims creeping, faults that keep coming back after being cleared.
/// Every finding says how many scans it rests on; nothing is concluded from a single reading except clear limits.
/// </summary>
public static class HealthTrends
{
    public static IReadOnlyList<HealthTrend> Analyze(IReadOnlyList<HealthSnapshot> snapshots, Func<int, string>? ccName = null)
    {
        var list = new List<HealthTrend>();
        if (snapshots.Count == 0) return list;
        var latest = snapshots[^1];

        // Battery at rest (engine off).
        var resting = snapshots.Where(s => !s.EngineRunning && s.Voltage is not null).ToList();
        if (resting.Count > 0 && resting[^1].Voltage is { } rest && resting[^1] == latest)
        {
            if (rest < 12.2)
                list.Add(new(TrendLevel.Act, "Battery low at rest",
                    $"{rest:0.0} V with the engine off at the last quick test. A charged battery rests around 12.6 V. Charge it and have it tested if it stays low."));
        }
        if (resting.Count >= 3)
        {
            var first = resting.Take(2).Average(s => s.Voltage!.Value);
            var last = resting.TakeLast(2).Average(s => s.Voltage!.Value);
            if (first - last >= 0.2)
                list.Add(new(TrendLevel.Watch, "Battery resting voltage is falling",
                    $"From about {first:0.00} V to {last:0.00} V over {resting.Count} engine-off quick tests. A battery that rests lower each time is ageing or being drained while parked."));
        }

        // Charging (engine running).
        var running = snapshots.Where(s => s.EngineRunning && s.Voltage is not null).ToList();
        if (running.Count > 0 && running[^1] == latest && latest.Voltage is { } charge)
        {
            if (charge < 13.4)
                list.Add(new(TrendLevel.Act, "Charging voltage low", $"{charge:0.0} V with the engine running. Normal is about 13.8–14.8 V: check the alternator and battery cables."));
            else if (charge > 15.0)
                list.Add(new(TrendLevel.Act, "Charging voltage high", $"{charge:0.0} V with the engine running, above the normal 13.8–14.8 V. Have the alternator's regulator checked."));
        }

        // Long-term fuel trims (engine running, warm).
        var trims = snapshots.Where(s => s.EngineRunning && (s.Ltft1 is not null || s.Ltft2 is not null)).ToList();
        if (trims.Count > 0 && trims[^1] == latest)
        {
            var worst = new[] { latest.Ltft1, latest.Ltft2 }.OfType<double>().MaxBy(Math.Abs);
            if (Math.Abs(worst) >= 10)
                list.Add(new(TrendLevel.Act, worst > 0 ? "Engine running lean" : "Engine running rich",
                    $"Long-term fuel trim at {worst:+0.0;-0.0} %. Beyond ±10 % the engine computer is correcting a lot: {(worst > 0 ? "look for a vacuum leak (crankcase vent valve, intake boot, oil filler cap), a dirty air-mass meter or weak fuel delivery" : "look for a leaking injector, high fuel pressure or a faulty oxygen sensor")}. The fuel trim test on the Tests page narrows it down."));
        }
        if (trims.Count >= 3)
        {
            static double Avg(HealthSnapshot s) => new[] { s.Ltft1, s.Ltft2 }.OfType<double>().Average();
            var first = trims.Take(2).Average(Avg);
            var last = trims.TakeLast(2).Average(Avg);
            var days = (trims[^1].Time - trims[0].Time).TotalDays;
            if (Math.Abs(last - first) >= 4 && days >= 7)
                list.Add(new(TrendLevel.Watch, last > first ? "Fuel trims creeping leaner" : "Fuel trims creeping richer",
                    $"Long-term trims moved from {first:+0.0;-0.0} % to {last:+0.0;-0.0} % over {days:0} days. {(last > first ? "A slowly growing vacuum leak is the usual cause, well before a lean fault code appears." : "Worth checking injectors and the oxygen sensors before a fault code appears.")}"));
        }

        // Faults that keep coming back: in the latest scan and in scans on two or more earlier days.
        foreach (var f in latest.Faults)
        {
            var days = snapshots.Take(snapshots.Count - 1).Where(s => s.Faults.Contains(f)).Select(s => s.Time.Date).Distinct().Count();
            if (days >= 2)
                list.Add(new(TrendLevel.Watch, $"{Short(f)} keeps coming back", $"Present in this quick test and on {days} earlier days. A fault that returns after clearing is real: fix the cause rather than clearing it again."));
        }

        // Dash messages that won't go away: like faults, on two or more earlier days. Several quick tests in one
        // sitting all see the same dash, so they prove nothing.
        foreach (var id in latest.DashMessages)
        {
            var days = snapshots.Take(snapshots.Count - 1).Where(s => s.DashMessages.Contains(id)).Select(s => s.Time.Date).Distinct().Count();
            if (days >= 2)
                list.Add(new(TrendLevel.Info, $"{ccName?.Invoke(id) ?? $"CC {id}"} keeps showing", $"On the dash in this quick test and on {days} earlier days."));
        }

        // New since the previous scan.
        if (snapshots.Count >= 2)
        {
            var fresh = latest.Faults.Except(snapshots[^2].Faults).ToList();
            if (fresh.Count > 0)
                list.Add(new(TrendLevel.Info, fresh.Count == 1 ? "New since the last quick test" : $"{fresh.Count} new since the last quick test", string.Join(", ", fresh.Select(Short))));
            // Gone since the previous scan: the proof a repair (or a clear) held.
            var gone = snapshots[^2].Faults.Except(latest.Faults).ToList();
            if (gone.Count > 0)
                list.Add(new(TrendLevel.Info, gone.Count == 1 ? "Gone since the last quick test" : $"{gone.Count} gone since the last quick test",
                    $"{string.Join(", ", gone.Select(Short))}. Stored on {snapshots[^2].Time.LocalDateTime:d MMM yyyy}, not there now. If you cleared {(gone.Count == 1 ? "it" : "them")}, drive a few days and test again to be sure."));
        }

        return list.OrderByDescending(t => t.Level).ToList();
    }

    /// <summary>What the guided tests say over time: the latest result of each test, and whether it got worse.</summary>
    public static IReadOnlyList<HealthTrend> AnalyzeTests(IReadOnlyList<TestRecord> records)
    {
        static int Rank(TestSeverity s) => s switch { TestSeverity.Ok => 0, TestSeverity.Watch => 1, TestSeverity.Problem => 2, _ => -1 };
        var list = new List<HealthTrend>();
        foreach (var g in records.Where(r => r.Verdict != TestSeverity.Unknown).OrderBy(r => r.Time).GroupBy(r => r.TestId))
        {
            var runs = g.ToList();
            var last = runs[^1];
            var best = runs.Take(runs.Count - 1).Select(r => Rank(r.Verdict)).DefaultIfEmpty(-1).Min();
            var when = $"{last.Time.LocalDateTime:d MMM yyyy}";
            if (best >= 0 && Rank(last.Verdict) > best)
                list.Add(new(last.Verdict == TestSeverity.Problem ? TrendLevel.Act : TrendLevel.Watch, $"{last.Title}: getting worse",
                    $"Better in an earlier run; on {when}: {last.Summary}. A part that tests worse each time is wearing out."));
            else if (last.Verdict == TestSeverity.Problem)
                list.Add(new(TrendLevel.Act, $"{last.Title}: problem", $"Last run on {when}: {last.Summary}."));
            else if (last.Verdict == TestSeverity.Watch)
                list.Add(new(TrendLevel.Watch, $"{last.Title}: worth watching", $"Last run on {when}: {last.Summary}."));
        }
        return list;
    }

    private static string Short(string key) => key.Contains('.') ? key[(key.IndexOf('.') + 1)..].Replace(":", " ") : key;
}

/// <summary>Where snapshots live: one JSON-lines file per car in the user's own folder.</summary>
public sealed class HealthHistoryStore(string folder)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public string Folder { get; } = folder;

    public void Add(HealthSnapshot s)
    {
        Directory.CreateDirectory(Folder);
        File.AppendAllText(PathOf(s.CarKey), JsonSerializer.Serialize(s, Json) + "\n");
    }

    public IReadOnlyList<HealthSnapshot> Load(string carKey)
    {
        var path = PathOf(carKey);
        if (!File.Exists(path)) return [];
        var list = new List<HealthSnapshot>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try { if (JsonSerializer.Deserialize<HealthSnapshot>(line, Json) is { } s) list.Add(s); }
            catch (JsonException) { /* skip a damaged line */ }
        }
        return list.OrderBy(s => s.Time).ToList();
    }

    /// <summary>Every car with at least one snapshot: its key and newest snapshot, newest car first.</summary>
    public IReadOnlyList<HealthSnapshot> Cars()
    {
        if (!Directory.Exists(Folder)) return [];
        return Directory.EnumerateFiles(Folder, "*.jsonl")
            .Where(f => !f.EndsWith(".tests.jsonl", StringComparison.OrdinalIgnoreCase))
            .Select(f => Load(Path.GetFileNameWithoutExtension(f)).LastOrDefault())
            .OfType<HealthSnapshot>()
            .OrderByDescending(s => s.Time)
            .ToList();
    }

    public void Forget(string carKey)
    {
        foreach (var path in new[] { PathOf(carKey), TestsPathOf(carKey) })
            if (File.Exists(path)) File.Delete(path);
    }

    public void AddTest(string carKey, TestRecord record)
    {
        Directory.CreateDirectory(Folder);
        File.AppendAllText(TestsPathOf(carKey), JsonSerializer.Serialize(record, Json) + "\n");
    }

    public IReadOnlyList<TestRecord> LoadTests(string carKey)
    {
        var path = TestsPathOf(carKey);
        if (!File.Exists(path)) return [];
        var list = new List<TestRecord>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try { if (JsonSerializer.Deserialize<TestRecord>(line, Json) is { } r) list.Add(r); }
            catch (JsonException) { }
        }
        return list.OrderBy(r => r.Time).ToList();
    }

    private static string Safe(string carKey) => new(carKey.Where(char.IsLetterOrDigit).ToArray());
    private string PathOf(string carKey) => Path.Combine(Folder, Safe(carKey) + ".jsonl");
    private string TestsPathOf(string carKey) => Path.Combine(Folder, Safe(carKey) + ".tests.jsonl");
}
