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

using System.Globalization;
using System.Net;
using System.Text;

namespace Klemme15.Core;

public enum DiffKind { New, Gone, Changed, Same }

/// <summary>One difference between two snapshots.</summary>
/// <param name="Area">"Fault", "Module", "Dash message" or "Coding".</param>
/// <param name="Where">The module's name, or empty for the car as a whole.</param>
public sealed record DiffItem(DiffKind Kind, string Area, string Where, string Title, string Detail = "");

/// <summary>Everything that differs between two snapshots, and the one-line verdict.</summary>
public sealed record SnapshotComparison(string BeforeLabel, string AfterLabel, IReadOnlyList<DiffItem> Items, IReadOnlyList<string> Notes)
{
    public IEnumerable<DiffItem> Of(DiffKind kind) => Items.Where(i => i.Kind == kind);
    public int Unchanged => Items.Count(i => i.Kind == DiffKind.Same);

    /// <summary>"2 faults gone, 1 new, 3 unchanged." in plain words.</summary>
    public string Headline
    {
        get
        {
            var gone = Items.Count(i => i is { Kind: DiffKind.Gone, Area: "Fault" });
            var added = Items.Count(i => i is { Kind: DiffKind.New, Area: "Fault" });
            var same = Items.Count(i => i is { Kind: DiffKind.Same or DiffKind.Changed, Area: "Fault" });
            if (gone + added + same == 0) return "No faults in either snapshot.";
            var parts = new List<string>();
            void Add(int n, string what) { if (n > 0) parts.Add($"{(parts.Count == 0 ? Count(n) : n.ToString(CultureInfo.InvariantCulture))} {what}"); }
            Add(gone, "gone");
            Add(added, "new");
            Add(same, "still there");
            return string.Join(", ", parts) + ".";
            static string Count(int n) => n == 1 ? "1 fault" : $"{n} faults";
        }
    }
}

/// <summary>
/// Compares two snapshots of a car: before and after a repair, last month against today, or this car against a
/// healthy one of the same model. It reads the two files and nothing else; no car needs to be connected.
/// </summary>
public static class SnapshotDiff
{
    public static SnapshotComparison Compare(CarSnapshot before, CarSnapshot after)
    {
        var items = new List<DiffItem>();
        var notes = new List<string>();
        if (before.Manifest.ModelId is { } a && after.Manifest.ModelId is { } b && a != b)
            notes.Add($"These are different models ({before.Manifest.ModelName ?? a} and {after.Manifest.ModelName ?? b}), so module and coding differences are expected.");
        if (after.Manifest.Created < before.Manifest.Created)
            notes.Add("The second snapshot is older than the first. \"New\" means: in the second file but not the first.");

        var ecusBefore = before.Scan.Ecus.ToDictionary(e => e.Ecu.Id);
        var ecusAfter = after.Scan.Ecus.ToDictionary(e => e.Ecu.Id);
        foreach (var id in ecusBefore.Keys.Union(ecusAfter.Keys))
        {
            ecusBefore.TryGetValue(id, out var x);
            ecusAfter.TryGetValue(id, out var y);
            var name = (y ?? x)!.Ecu.Name;
            bool was = Answered(x), now = Answered(y);

            // Modules: one that stopped answering, started answering, or was swapped for another part.
            if (was && !now) items.Add(new(DiffKind.Gone, "Module", name, "No longer answers", "It answered in the first snapshot. Check its fuse, power and connector, or whether the ignition was on."));
            else if (!was && now) items.Add(new(DiffKind.New, "Module", name, "Answers now", "It didn't answer in the first snapshot."));
            else if (was && now && x!.Identity?.PartNumber is { Length: > 0 } p1 && y!.Identity?.PartNumber is { Length: > 0 } p2 && p1 != p2)
                items.Add(new(DiffKind.Changed, "Module", name, "A different part", $"Part number {p1} before, {p2} now: the module was replaced or reprogrammed."));

            // Faults, matched by code within the module. Info and shadow entries are background records: left out.
            var f1 = Faults(x).GroupBy(f => f.Code).ToDictionary(g => g.Key, g => g.First());
            var f2 = Faults(y).GroupBy(f => f.Code).ToDictionary(g => g.Key, g => g.First());
            foreach (var code in f1.Keys.Union(f2.Keys).OrderBy(c => c, StringComparer.Ordinal))
            {
                f1.TryGetValue(code, out var o);
                f2.TryGetValue(code, out var n);
                var text = (n ?? o)!.DisplayDescription ?? "No description for this code yet";
                if (o is null) items.Add(new(DiffKind.New, "Fault", name, $"{code}  {text}", n!.State == FaultState.Present ? "Active now." : "Stored."));
                else if (n is null) items.Add(new(DiffKind.Gone, "Fault", name, $"{code}  {text}",
                    now ? "" : "The module doesn't answer in the second snapshot, so this isn't proof the fault is fixed."));
                else if (o.State != n.State) items.Add(new(DiffKind.Changed, "Fault", name, $"{code}  {text}", $"{Label(o.State)} before, {Label(n.State).ToLowerInvariant()} now."));
                else items.Add(new(DiffKind.Same, "Fault", name, $"{code}  {text}", Label(n.State) + "."));
            }
        }

        // Dash messages showing at the time of each snapshot.
        var cc1 = before.Scan.CheckControl.Where(c => c.Active).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        var cc2 = after.Scan.CheckControl.Where(c => c.Active).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var id in cc1.Keys.Union(cc2.Keys).OrderBy(i => i))
        {
            var message = cc2.TryGetValue(id, out var m2) ? m2 : cc1[id];
            var kind = !cc1.ContainsKey(id) ? DiffKind.New : !cc2.ContainsKey(id) ? DiffKind.Gone : DiffKind.Same;
            items.Add(new(kind, "Dash message", "", $"{message.Code}  {message.Title}"));
        }

        // Coding, where both snapshots carry it for the same module: how many bytes differ, never the bytes themselves.
        foreach (var c2 in after.Coding)
        {
            if (before.Coding.FirstOrDefault(c => c.Module == c2.Module) is not { } c1) continue;
            var changed = 0;
            var blocks = 0;
            foreach (var b2 in c2.Blocks)
            {
                var hex1 = c1.Blocks.FirstOrDefault(x => x.Id == b2.Id)?.Hex ?? "";
                var bytes = DifferentBytes(hex1, b2.Hex);
                if (bytes > 0) { changed += bytes; blocks++; }
            }
            items.Add(changed == 0
                ? new(DiffKind.Same, "Coding", c2.Module, "Coding is identical")
                : new(DiffKind.Changed, "Coding", c2.Module, "Coding differs", $"{changed} byte{(changed == 1 ? "" : "s")} in {blocks} block{(blocks == 1 ? "" : "s")}. Open both snapshots on the Coding page to see which options."));
        }
        if (before.Coding.Count == 0 || after.Coding.Count == 0)
            notes.Add("Coding wasn't compared: it's only in a snapshot when its owner ticked it.");

        return new(LabelOf(before), LabelOf(after), items, notes);
    }

    private static bool Answered(EcuScanResult? e) => e?.Status is EcuScanStatus.Ok or EcuScanStatus.Faults;
    private static IEnumerable<Fault> Faults(EcuScanResult? e) => e is null ? [] : e.Faults.Where(f => !f.IsInfo && !f.IsShadow);
    private static string Label(FaultState s) => s switch { FaultState.Present => "Active", FaultState.Intermittent => "Intermittent", _ => "Stored" };

    private static string LabelOf(CarSnapshot s) =>
        $"{s.Manifest.Chassis ?? s.Manifest.ModelName ?? "Snapshot"}, {s.Manifest.Created.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture)}";

    /// <summary>How many bytes differ between two hex strings (a length difference counts every extra byte).</summary>
    public static int DifferentBytes(string hexA, string hexB)
    {
        string a = hexA.Replace(" ", ""), b = hexB.Replace(" ", "");
        var n = 0;
        var shared = Math.Min(a.Length, b.Length) / 2;
        for (var i = 0; i < shared; i++)
            if (!a.AsSpan(i * 2, 2).Equals(b.AsSpan(i * 2, 2), StringComparison.OrdinalIgnoreCase)) n++;
        return n + Math.Abs(a.Length - b.Length) / 2;
    }

    // ---------------------------------------------------------------- output

    public static string ToText(SnapshotComparison c)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Before: {c.BeforeLabel}");
        sb.AppendLine($"After:  {c.AfterLabel}");
        sb.AppendLine();
        sb.AppendLine(c.Headline);
        foreach (var note in c.Notes) sb.AppendLine("Note: " + note);
        foreach (var (kind, title) in Sections)
        {
            var list = c.Of(kind).ToList();
            if (list.Count == 0) continue;
            sb.AppendLine();
            sb.AppendLine(title);
            foreach (var i in list)
            {
                sb.AppendLine($"  [{i.Area}] {(i.Where.Length > 0 ? i.Where + ": " : "")}{i.Title}");
                if (i.Detail.Length > 0) sb.AppendLine($"      {i.Detail}");
            }
        }
        return sb.ToString();
    }

    private static readonly (DiffKind Kind, string Title)[] Sections =
        [(DiffKind.New, "New"), (DiffKind.Gone, "Gone"), (DiffKind.Changed, "Changed"), (DiffKind.Same, "Unchanged")];

    /// <summary>A page that stands on its own: no scripts, nothing loaded from the internet.</summary>
    public static string ToHtml(SnapshotComparison c)
    {
        static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();
        sb.Append("""
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Klemme15 snapshot comparison</title>
<style>
:root{--bg:#f7f7f5;--card:#fff;--ink:#1b1d21;--muted:#666b73;--line:#e3e3df;--ok:#1f8a4c;--warn:#b86e00;--bad:#c0362c}
@media (prefers-color-scheme:dark){:root{--bg:#141518;--card:#1d1f23;--ink:#ececec;--muted:#9aa0a8;--line:#2d3036}}
body{margin:0;background:var(--bg);color:var(--ink);font:16px/1.5 system-ui,-apple-system,"Segoe UI",sans-serif}
main{max-width:860px;margin:0 auto;padding:24px 16px 48px}
h1{font-size:26px;margin:0 0 4px}h2{font-size:19px;margin:28px 0 8px}
.muted{color:var(--muted);font-size:14px}.card{background:var(--card);border:1px solid var(--line);border-left-width:4px;border-radius:8px;padding:10px 14px;margin:0 0 8px}
.new{border-left-color:var(--bad)}.gone{border-left-color:var(--ok)}.changed{border-left-color:var(--warn)}.same{border-left-color:var(--line)}
.tag{font-size:12px;font-weight:600;color:var(--muted);text-transform:uppercase;letter-spacing:.04em}
.head{font-size:19px;font-weight:600;margin:14px 0}
</style></head><body><main>
<h1>Snapshot comparison</h1>
""");
        sb.Append($"<div class=\"muted\">Before: {H(c.BeforeLabel)}<br>After: {H(c.AfterLabel)}</div>");
        sb.Append($"<div class=\"head\">{H(c.Headline)}</div>");
        foreach (var note in c.Notes) sb.Append($"<p class=\"muted\">{H(note)}</p>");
        foreach (var (kind, title) in Sections)
        {
            var list = c.Of(kind).ToList();
            if (list.Count == 0) continue;
            sb.Append($"<h2>{title} ({list.Count})</h2>");
            foreach (var i in list)
            {
                sb.Append($"<div class=\"card {kind.ToString().ToLowerInvariant()}\"><div class=\"tag\">{H(i.Area)}{(i.Where.Length > 0 ? " · " + H(i.Where) : "")}</div><div>{H(i.Title)}</div>");
                if (i.Detail.Length > 0) sb.Append($"<div class=\"muted\">{H(i.Detail)}</div>");
                sb.Append("</div>");
            }
        }
        sb.Append("<p class=\"muted\">Made with Klemme15 from two snapshot files. A fault that is gone was not in the second snapshot's fault memory; it can return. Not affiliated with BMW AG.</p></main></body></html>");
        return sb.ToString();
    }
}
