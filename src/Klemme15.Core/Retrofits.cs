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

using System.Text.RegularExpressions;

namespace Klemme15.Core;

/// <summary>What a common swap or retrofit usually needs (db/retrofits.json).</summary>
public sealed record RetrofitGuide
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }

    /// <summary>Modules whose part number changing, or a module appearing, suggests this retrofit.</summary>
    public IReadOnlyList<string> Modules { get; init; } = [];
    public IReadOnlyList<string> Needs { get; init; } = [];

    /// <summary>What Klemme15 itself can do for it.</summary>
    public IReadOnlyList<string> Klemme15 { get; init; } = [];

    /// <summary>Dash messages that mean it isn't finished yet.</summary>
    public IReadOnlyList<int> WatchMessages { get; init; } = [];

    /// <summary>Fault-text words that mean it isn't finished yet ("*word*" also matches inside longer words).</summary>
    public IReadOnlyList<string> WatchWords { get; init; } = [];
    public IReadOnlyList<string> Sources { get; init; } = [];
}

/// <summary>Something the retrofit checker noticed about the car.</summary>
public sealed record RetrofitFinding(string Title, string Detail, string? GuideId);

/// <summary>Whether a retrofit looks finished: what's still showing on the car, read from the last quick test.</summary>
public sealed record RetrofitCheck(bool Clean, IReadOnlyList<string> Remaining);

/// <summary>
/// Read-only retrofit help. Notices swapped or added modules (part number changes between scans, modules the model
/// doesn't list), explains what that kind of change usually needs, and checks the car afterwards. It never writes the
/// vehicle order, flashes or touches mileage: for those steps it names the tool that does them.
/// </summary>
public static class RetrofitAdvisor
{
    private static string Short(EcuDefinition e) => e.Id.Contains('.') ? e.Id[(e.Id.LastIndexOf('.') + 1)..] : e.Id;

    public static IReadOnlyList<RetrofitFinding> Detect(VehicleScanResult scan, VehicleModel? model, IReadOnlyList<HealthSnapshot> history,
        IReadOnlyList<RetrofitGuide> guides)
    {
        var findings = new List<RetrofitFinding>();
        string? GuideFor(string module) => guides.FirstOrDefault(g => g.Modules.Contains(module, StringComparer.OrdinalIgnoreCase))?.Id;
        var answered = scan.Ecus.Where(e => e.Status is EcuScanStatus.Ok or EcuScanStatus.Faults).ToList();

        // 1. A module whose part number changed since an earlier scan of this car: replaced or swapped.
        var now = answered.Where(e => e.Identity?.PartNumber is { Length: > 0 }).GroupBy(e => Short(e.Ecu)).ToDictionary(g => g.Key, g => g.First().Identity!.PartNumber!);
        foreach (var (module, part) in now)
        {
            var before = history.Where(h => h.Parts.TryGetValue(module, out var p) && p != part && h.Time < scan.Started).OrderBy(h => h.Time).LastOrDefault();
            if (before is null) continue;
            // Shown for 30 days after the change was first seen, so it's still there while the job is being finished.
            var firstSeen = history.Where(h => h.Time > before.Time && h.Parts.TryGetValue(module, out var p2) && p2 == part).Select(h => h.Time).DefaultIfEmpty(scan.Started).Min();
            if (scan.Started - firstSeen > TimeSpan.FromDays(30)) continue;
            var name = answered.First(e => Short(e.Ecu) == module).Ecu.Name;
            findings.Add(new($"{name} was replaced", $"Part number {before.Parts[module]} at the scan on {before.Time.LocalDateTime:d MMM yyyy}, now {part}. A new or used module usually has to be coded to the car before it behaves.", GuideFor(module)));
        }

        // 2. Modules answering that this model doesn't list: added, or the model chosen in the Garage is a different spec.
        if (model is { ModuleIds.Count: > 0 })
            foreach (var e in answered.Where(e => !model.ModuleIds.Contains(e.Ecu.Id)))
                findings.Add(new($"{e.Ecu.Name} is fitted", $"The {model.Name} doesn't usually have it. Either it was retrofitted, or your car has an option the model list doesn't show.", GuideFor(Short(e.Ecu))));

        // 3. The footwell module's generation, which decides how careful coding must be.
        if (answered.FirstOrDefault(e => Short(e.Ecu) == "FRM") is { } frm && FrmGuard.Assess(frm.Identity) is { Generation: not FrmGeneration.None } a)
            findings.Add(new(a.Label, a.Advice, null));
        return findings;
    }

    /// <summary>Checks the last quick test for anything that says this retrofit isn't finished.</summary>
    public static RetrofitCheck Check(RetrofitGuide guide, VehicleScanResult scan)
    {
        var remaining = new List<string>();
        foreach (var m in scan.CheckControl.Where(m => m.Active && guide.WatchMessages.Contains(m.Id)))
            remaining.Add($"Dash message {m.Code}: {m.Title}");
        foreach (var e in scan.Ecus.Where(e => e.Faults.Count > 0 && (guide.Modules.Count == 0 || guide.Modules.Contains(Short(e.Ecu), StringComparer.OrdinalIgnoreCase))))
            foreach (var f in e.Faults.Where(f => FaultTriage.Assess(f).Priority != FaultPriority.Background))
            {
                var text = $"{f.Description} {f.OfficialText}".ToLowerInvariant();
                if (guide.WatchWords.Any(w => Matches(w, text))) remaining.Add($"{e.Ecu.Name} {f.Code}: {f.DisplayDescription}");
            }
        return new(remaining.Count == 0, remaining.Distinct().ToList());
    }

    private static bool Matches(string word, string text)
    {
        var core = Regex.Escape(word.Trim('*').ToLowerInvariant());
        var pattern = (word.StartsWith('*') ? "" : @"(?<![\p{L}\d])") + core + (word.EndsWith('*') ? "" : @"(?![\p{L}\d])");
        return Regex.IsMatch(text, pattern);
    }
}
