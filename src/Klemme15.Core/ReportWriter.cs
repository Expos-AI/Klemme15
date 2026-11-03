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

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Klemme15.Core;

/// <summary>Exports a scan as a shareable report (HTML for people, JSON for tools, Markdown for forums).</summary>
public static class ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string Label(EcuScanStatus s) => s switch
    {
        EcuScanStatus.Ok => "OK",
        EcuScanStatus.NoResponse => "No answer",
        _ => s.ToString(),
    };

    private static string Info(EcuScanResult e) =>
        string.Join(" ", new[] { e.Identity?.Summary, e.Error, e.Note }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public static string ToJson(VehicleScanResult scan) => JsonSerializer.Serialize(scan, JsonOptions);

    public static string ToMarkdown(VehicleScanResult scan)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Klemme15 quick test {scan.Started:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine($"- Vehicle: {scan.Chassis ?? "unknown"}  VIN: {scan.Vin ?? "not read"}");
        sb.AppendLine($"- Backend: {scan.Backend}  Duration: {scan.Elapsed.TotalSeconds:0.0}s{(scan.Cancelled ? " (cancelled)" : "")}");
        sb.AppendLine($"- Modules answering: {scan.RespondingEcus}/{scan.Ecus.Count}  Faults: {scan.TotalFaults}{(scan.InfoEntries > 0 ? $"  Shadow / Info memory: {scan.InfoEntries}" : "")}{(scan.ActiveCheckControl > 0 ? $"  Dash messages: {scan.ActiveCheckControl}" : "")}{(scan.BatteryVoltage is { } v ? $"  Battery: {v:0.0} V" : "")}");
        sb.AppendLine();
        MarkdownCheckControl(sb, scan, forum: false);
        sb.AppendLine("| Module | Address | Status | Faults | Identification |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var e in scan.Ecus)
            sb.AppendLine($"| {e.Ecu.Name} | {e.Ecu.AddressHex} | {Label(e.Status)} | {e.Faults.Count} | {Info(e)} |");
        foreach (var e in scan.Ecus.Where(e => e.Faults.Count > 0))
        {
            sb.AppendLine();
            sb.AppendLine($"## {e.Ecu.Name}");
            foreach (var f in e.Faults)
            {
                var verdict = FaultTriage.Assess(f);
                sb.AppendLine($"- **{f.Code}** ({f.MemoryLabel}, {verdict.Label.ToLowerInvariant()}) {f.DisplayDescription}");
                sb.AppendLine($"  - {verdict.Advice}");
                if (f.OfficialText is { } bmw && bmw != f.Description) sb.AppendLine($"  - BMW text: {bmw}");
                if (f.ModuleVariant is { } mod) sb.AppendLine($"  - Module {mod}{(f.VariantConfirmed ? "" : " (most likely)")}");
                if (f.Hint is { } tip) sb.AppendLine($"  - Tip: {tip}");
            }
        }
        MarkdownInfoMemory(sb, scan, forum: false);
        MarkdownHealth(sb, scan, forum: false);
        MarkdownStory(sb, scan, forum: false);
        return sb.ToString();
    }

    private static string HealthIntro(HealthSummary h) =>
        $"Compared across {h.Scans} quick test{(h.Scans == 1 ? "" : "s")} since {h.Since:yyyy-MM-dd}" + (h.MadeUp ? " (made-up demo history)" : "") + ".";

    private static string Level(TrendLevel l) => l switch { TrendLevel.Act => "act", TrendLevel.Watch => "watch", _ => "note" };

    /// <summary>What's changing across earlier scans: drift that hasn't become a fault yet.</summary>
    private static void MarkdownHealth(StringBuilder sb, VehicleScanResult scan, bool forum)
    {
        if (scan.Health is not { Trends.Count: > 0 } h) return;
        sb.AppendLine();
        sb.AppendLine(forum ? "**Changes over time**" : "## Health over time");
        sb.AppendLine();
        sb.AppendLine(HealthIntro(h));
        sb.AppendLine();
        foreach (var t in h.Trends.Take(forum ? 5 : 20))
            sb.AppendLine($"- **{t.Title}** ({Level(t.Level)}): {(forum ? Scrub(t.Detail) : t.Detail)}");
    }

    /// <summary>The cluster's warning history, told as events (newest first).</summary>
    private static void MarkdownStory(StringBuilder sb, VehicleScanResult scan, bool forum)
    {
        if (scan.Story is not { IsEmpty: false } story) return;
        sb.AppendLine();
        sb.AppendLine(forum ? "**Warning history (from the cluster)**" : "## The car's story (warning history from the cluster)");
        sb.AppendLine();
        foreach (var e in story.Events.Reverse().Take(forum ? 5 : 50))
            sb.AppendLine($"- **{e.When}**: {e.Title}. {(forum ? "" : e.Explanation)}".TrimEnd());
    }

    private static string CcLine(CheckControlMessage m) =>
        $"`{m.Code}` {m.Title} ({(m.Severity == CcSeverity.Critical ? "red" : "yellow")}{(m.Severity == CcSeverity.Advisory ? ", advisory" : "")})";

    /// <summary>The dash (Check Control) messages: why the triangle is lit even with no fault codes.</summary>
    private static void MarkdownCheckControl(StringBuilder sb, VehicleScanResult scan, bool forum)
    {
        var active = scan.CheckControl.Where(m => m.Active).ToList();
        var history = scan.CheckControl.Where(m => !m.Active).ToList();
        if (active.Count == 0 && history.Count == 0)
        {
            if (!forum && scan.CheckControlNote is { } note) { sb.AppendLine($"_Dash messages: {note}_"); sb.AppendLine(); }
            return;
        }
        sb.AppendLine(forum ? "**Dash messages (Check Control)**" : "## Dash messages (Check Control)");
        sb.AppendLine();
        if (active.Count == 0) sb.AppendLine("- None showing now.");
        foreach (var m in active)
        {
            sb.AppendLine($"- {CcLine(m)}{(m.Description is { } d && !forum ? $": {d}" : "")}");
            if (m.OfficialText is { } bmw && bmw != m.Title) sb.AppendLine($"  - BMW: {(forum ? Scrub(bmw) : bmw)}");
            if (m.Hint is { } tip && !forum) sb.AppendLine($"  - Tip: {tip}");
        }
        if (history.Count > 0)
        {
            sb.AppendLine(forum ? "- Earlier:" : "- Shown earlier (cluster history):");
            foreach (var m in history.Take(forum ? 5 : 20))
                sb.AppendLine($"  - {CcLine(m)}{(m.MileageKm is { } km ? $" at about {km:N0} km" : "")}");
        }
        sb.AppendLine();
    }

    /// <summary>A separate list of the background entries, clearly labelled, so they aren't mistaken for faults.</summary>
    private static void MarkdownInfoMemory(StringBuilder sb, VehicleScanResult scan, bool forum)
    {
        var entries = scan.Ecus.SelectMany(e => e.Faults.Where(f => f.IsInfo || f.IsShadow).Select(f => (e.Ecu, Fault: f))).ToList();
        if (entries.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine(forum ? $"**{Fault.InfoMemoryLabel}** (logged events, not faults)" : $"## {Fault.InfoMemoryLabel}");
        if (!forum) { sb.AppendLine(); sb.AppendLine("Events modules logged in the background. They never light a lamp on their own, but they can explain one."); }
        sb.AppendLine();
        foreach (var (ecu, f) in entries.Take(forum ? 8 : 100))
            sb.AppendLine($"- `{f.Code}` {ShortName(ecu)}: {(forum ? Scrub(f.DisplayDescription) : f.DisplayDescription)}");
        if (forum && entries.Count > 8) sb.AppendLine($"- _...and {entries.Count - 8} more._");
    }

    /// <summary>
    /// A short Markdown summary for asking for help on a forum (E90Post, Reddit, Bimmerforums): car, modules that
    /// answered, what needs attention with BMW's details, battery voltage. The VIN and anything that looks like one
    /// are left out, so the post can't be traced to the car.
    /// </summary>
    public static string ToForumPost(VehicleScanResult scan, string? carName = null)
    {
        var sb = new StringBuilder();
        var title = carName ?? scan.Chassis ?? "BMW";
        sb.AppendLine($"**{Scrub(title)}: Klemme15 quick test**");
        sb.AppendLine();
        var facts = new List<string> { $"{scan.RespondingEcus} of {scan.Ecus.Count} modules answered" };
        if (scan.BatteryVoltage is { } v) facts.Add($"battery {v:0.0} V");
        if (scan.IgnitionOn == false) facts.Add("ignition reported off");
        sb.AppendLine("- " + string.Join(", ", facts));
        var answered = scan.Ecus.Where(e => e.Status is EcuScanStatus.Ok or EcuScanStatus.Faults).ToList();
        if (answered.Count > 0)
            sb.AppendLine("- Modules: " + string.Join(", ", answered.Select(e =>
                e.Identity?.Variant is { } variant ? $"{ShortName(e.Ecu)} ({variant})" : ShortName(e.Ecu))));
        sb.AppendLine();

        MarkdownCheckControl(sb, scan, forum: true);
        var all = scan.Ecus.SelectMany(e => e.Faults.Select(f => (Ecu: e.Ecu, Fault: f, Verdict: FaultTriage.Assess(f)))).ToList();
        void Section(string heading, FaultPriority priority, bool details)
        {
            var items = all.Where(x => x.Verdict.Priority == priority).ToList();
            if (items.Count == 0) return;
            sb.AppendLine($"**{heading}**");
            sb.AppendLine();
            foreach (var (ecu, f, _) in items)
            {
                var state = f.IsShadow || f.IsInfo ? "shadow / info memory" : f.State switch
                {
                    FaultState.Present => "active now",
                    FaultState.Stored => "stored",
                    FaultState.Intermittent => "intermittent",
                    _ => "",
                };
                sb.AppendLine($"- `{f.Code}` {ShortName(ecu)}{(state.Length > 0 ? $", {state}" : "")}: {Scrub(f.DisplayDescription)}");
                if (f.OfficialText is { } bmw && bmw != f.Description) sb.AppendLine($"  - BMW: {Scrub(bmw)}");
                if (details)
                    foreach (var (k, val) in f.Details.Where(d => ForumDetail(d.Key) && !d.Value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)).Take(6))
                        sb.AppendLine($"  - {k}: {Scrub(val)}");
            }
            sb.AppendLine();
        }
        Section("Needs fixing", FaultPriority.FixNow, true);
        Section("Worth checking", FaultPriority.CheckIt, true);
        var background = all.Count(x => x.Verdict.Priority == FaultPriority.Background);
        var infoMemory = all.Count(x => x.Fault.IsInfo || x.Fault.IsShadow);
        if (all.Count == 0) sb.AppendLine(scan.ActiveCheckControl > 0 ? "No fault codes stored (the dash message above is not a fault code)." : "No faults stored.");
        else if (background - infoMemory > 0) sb.AppendLine($"_Plus {background - infoMemory} background entr{(background - infoMemory == 1 ? "y" : "ies")} (one-off events) left out._");
        MarkdownInfoMemory(sb, scan, forum: true);
        MarkdownHealth(sb, scan, forum: true);
        MarkdownStory(sb, scan, forum: true);
        sb.AppendLine();
        sb.AppendLine($"_Read with Klemme15 {scan.Started:yyyy-MM-dd} (open-source BMW diagnostics). VIN removed._");
        return sb.ToString();
    }

    private static string ShortName(EcuDefinition ecu) => ecu.Id.Contains('.') ? ecu.Id[(ecu.Id.IndexOf('.') + 1)..] : ecu.Name;

    /// <summary>Details worth posting: when, how often, and the conditions (temperatures, voltage, speed, rpm).</summary>
    private static bool ForumDetail(string key)
    {
        var k = key.ToLowerInvariant();
        return !k.Contains("vin") && !k.Contains("fahrgestell") && !k.StartsWith('_') && !k.Contains("hex") && !k.Contains("raw");
    }

    private static readonly System.Text.RegularExpressions.Regex VinPattern =
        new(@"\b[A-HJ-NPR-Z0-9]{17}\b|\b[A-HJ-NPR-Z]{2}[0-9]{5}\b", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Removes full VINs and BMW's 7-character short VIN ("PT12345") from text.</summary>
    public static string Scrub(string text) => VinPattern.Replace(text, "[VIN removed]");

    private static void HtmlCheckControl(StringBuilder sb, VehicleScanResult scan, Func<string?, string> H)
    {
        var active = scan.CheckControl.Where(m => m.Active).ToList();
        var history = scan.CheckControl.Where(m => !m.Active).ToList();
        if (active.Count == 0 && history.Count == 0)
        {
            if (scan.CheckControlNote is { } note) sb.Append($"<p class=\"muted\">Dash messages: {H(note)}</p>");
            return;
        }
        var red = CheckControlDescriber.Worst(active) == CcSeverity.Critical ? " red" : "";
        sb.Append($"<div class=\"cc{red}\"><h2><span class=\"tri{red}\"></span>Dash messages (Check Control)</h2>");
        if (active.Count > 0 && scan.TotalFaults == 0)
            sb.Append("<div class=\"muted\">These explain the warning triangle on the dash. They are messages, not fault codes, so the fault memory can be empty.</div>");
        sb.Append("<ul>");
        if (active.Count == 0) sb.Append("<li class=\"muted\">None showing now.</li>");
        foreach (var m in active)
        {
            var tri = m.Severity == CcSeverity.Critical ? "tri red" : "tri";
            sb.Append($"<li><span class=\"{tri}\"></span><b>{H(m.Title)}</b> <code>{H(m.Code)}</code> <span class=\"muted\">{H(CheckControlDescriber.SeverityLabel(m.Severity))}</span>");
            if (m.Description is { } d) sb.Append($"<br>{H(d)}");
            if (m.Hint is { } h) sb.Append($"<br><span class=\"muted\">Tip: {H(h)}</span>");
            if (m.OfficialText is { } ot) sb.Append($"<br><span class=\"muted\">BMW text: {H(ot)} ({H(m.OfficialSource)})</span>");
            sb.Append("</li>");
        }
        sb.Append("</ul>");
        if (history.Count > 0)
        {
            sb.Append("<div class=\"muted\">Shown earlier (cluster history):</div><ul>");
            foreach (var m in history.Take(20))
                sb.Append($"<li>{H(m.Title)} <code>{H(m.Code)}</code>{(m.MileageKm is { } km ? $" <span class=\"muted\">at about {km:N0} km</span>" : "")}</li>");
            sb.Append("</ul>");
        }
        sb.Append("</div>");
    }

    /// <param name="purchase">A pre-purchase check to put at the top (the buyer's report), or null.</param>
    public static string ToHtml(VehicleScanResult scan, PurchaseReport? purchase = null)
    {
        static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();
        sb.Append("""
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Klemme15 scan report</title>
<style>
:root{--bg:#f7f7f5;--card:#fff;--ink:#1b1d21;--muted:#666b73;--line:#e3e3df;--ok:#1f8a4c;--warn:#b86e00;--bad:#c0362c}
@media (prefers-color-scheme:dark){:root{--bg:#141518;--card:#1d1f23;--ink:#ececec;--muted:#9aa0a8;--line:#2d3036}}
body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.5 system-ui,-apple-system,Segoe UI,sans-serif}
main{max-width:960px;margin:0 auto;padding:24px 16px}
h1{font-size:22px;margin:0 0 4px}.muted{color:var(--muted)}
.stats{display:flex;gap:12px;flex-wrap:wrap;margin:16px 0}
.stat{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:10px 14px;min-width:120px}
.stat b{display:block;font-size:22px}
table{width:100%;border-collapse:collapse;background:var(--card);border:1px solid var(--line);border-radius:10px;overflow:hidden}
th,td{text-align:left;padding:8px 10px;border-bottom:1px solid var(--line);vertical-align:top}
th{font-size:12px;text-transform:uppercase;letter-spacing:.04em;color:var(--muted)}
.Ok{color:var(--ok)}.Faults{color:var(--warn)}.Error{color:var(--bad)}.NoResponse,.Skipped{color:var(--muted)}
code{font:13px ui-monospace,Consolas,monospace}
section{margin-top:24px}
.table-wrap{overflow-x:auto}
.cc{background:var(--card);border:1px solid var(--line);border-left:6px solid var(--warn);border-radius:10px;padding:12px 16px;margin:16px 0}
.cc.red{border-left-color:var(--bad)}.cc h2{margin:0 0 6px;font-size:17px}.cc ul{margin:6px 0;padding-left:20px}
.tri{display:inline-block;width:0;height:0;border-left:8px solid transparent;border-right:8px solid transparent;border-bottom:14px solid var(--warn);vertical-align:-2px;margin-right:6px}
.tri.red{border-bottom-color:var(--bad)}
.bad{color:var(--bad);font-weight:600}.warn{color:var(--warn);font-weight:600}
.brand{display:flex;align-items:center;gap:10px;margin-bottom:18px;font-weight:600;font-size:17px}.brand svg{width:32px;height:32px;flex:none}
@media (prefers-color-scheme:light){.brand svg rect:first-child{stroke:var(--line);stroke-width:8}}
</style></head><body><main>
""");
        sb.Append($"<div class=\"brand\">{Logo.Svg}<span>Klemme15</span></div>");
        sb.Append($"<h1>Quick test report: {H(scan.Chassis ?? "Vehicle")}</h1>");
        sb.Append($"<div class=\"muted\">{scan.Started:yyyy-MM-dd HH:mm} &middot; VIN {H(scan.Vin ?? "not read")} &middot; {H(scan.Backend)}</div>");
        sb.Append("<div class=\"stats\">");
        sb.Append($"<div class=\"stat\"><b>{scan.RespondingEcus}/{scan.Ecus.Count}</b>modules answering</div>");
        sb.Append($"<div class=\"stat\"><b>{scan.TotalFaults}</b>faults</div>");
        sb.Append($"<div class=\"stat\"><b>{scan.Elapsed.TotalSeconds:0.0}s</b>duration</div>");
        if (scan.BatteryVoltage is { } v) sb.Append($"<div class=\"stat\"><b>{v:0.0} V</b>battery</div>");
        if (scan.ActiveCheckControl > 0) sb.Append($"<div class=\"stat\"><b>{scan.ActiveCheckControl}</b>dash messages</div>");
        sb.Append("</div>");
        if (purchase is not null)
        {
            sb.Append($"<section><h2>Pre-purchase check: {H(purchase.Headline)}</h2><div class=\"table-wrap\"><table><tr><th></th><th>Finding</th></tr>");
            foreach (var i in purchase.Items)
            {
                var tag = i.Level switch { CheckLevel.Warn => "<span class=\"bad\">Check</span>", CheckLevel.Ask => "<span class=\"warn\">Ask</span>", CheckLevel.Good => "<span class=\"Ok\">Good</span>", _ => "Note" };
                sb.Append($"<tr><td>{tag}</td><td><b>{H(i.Title)}</b><br><span class=\"muted\">{H(i.Detail)}</span></td></tr>");
            }
            sb.Append("</table></div>");
            if (purchase.NotChecked.Count > 0)
                sb.Append("<p class=\"muted\">Not covered: " + string.Join(" ", purchase.NotChecked.Select(H)) + "</p>");
            sb.Append("<p class=\"muted\">A pre-purchase check reads what the car's modules remember. It is not a substitute for an inspection by a workshop.</p></section>");
        }
        HtmlCheckControl(sb, scan, H);
        sb.Append("<div class=\"table-wrap\"><table><tr><th>Module</th><th>Address</th><th>Status</th><th>Faults</th><th>Identification</th></tr>");
        foreach (var e in scan.Ecus)
            sb.Append($"<tr><td>{H(e.Ecu.Name)}</td><td><code>{e.Ecu.AddressHex}</code></td><td class=\"{e.Status}\">{Label(e.Status)}</td><td>{e.Faults.Count}</td><td>{H(Info(e))}</td></tr>");
        sb.Append("</table></div>");
        foreach (var e in scan.Ecus.Where(e => e.Faults.Count > 0))
        {
            sb.Append($"<section><h2>{H(e.Ecu.Name)}</h2><div class=\"table-wrap\"><table><tr><th>Code</th><th>State</th><th>Description</th><th>Details</th></tr>");
            foreach (var f in e.Faults)
            {
                var details = string.Join(", ", f.Details.Select(kv => $"{kv.Key}: {kv.Value}"));
                var state = H(f.MemoryLabel) + $"<br><b>{H(FaultTriage.Assess(f).Label)}</b>";
                var hint = $"<br><span class=\"muted\">{H(FaultTriage.Assess(f).Advice)}</span>"
                           + (f.Hint is null ? "" : $"<br><span class=\"muted\">Tip: {H(f.Hint)}</span>");
                var bmwText = f.OfficialText is { } ot && ot != f.Description ? $"<br><span class=\"muted\">BMW text: {H(ot)}</span>" : "";
                var variant = f.ModuleVariant is { } mv ? $"<br><span class=\"muted\">Module {H(mv)}{(f.VariantConfirmed ? "" : " (most likely)")}</span>" : "";
                hint = bmwText + variant + hint;
                var origin = f.DescriptionOrigin switch
                {
                    "bmw-en" => $"<br><span class=\"muted\">BMW's own text ({H(f.OfficialSource)})</span>",
                    "bmw-de-glossary" => $"<br><span class=\"muted\">BMW's German text, translated with Klemme15's glossary ({H(f.OfficialSource)})</span>",
                    "bmw-de" => $"<br><span class=\"muted\">BMW's German text ({H(f.OfficialSource)})</span>",
                    "klemme15-generic" => "<br><span class=\"muted\">General meaning from the code range and module (no specific text on file)</span>",
                    _ => null,
                } ?? f.Confidence switch
                {
                    "official" => "<br><span class=\"muted\">Official text from the module's SGBD</span>",
                    "high" or "medium" => $"<br><span class=\"muted\">Community meaning ({f.Confidence} confidence): " +
                        string.Join(", ", f.Sources.Take(3).Select(u => $"<a href=\"{H(u)}\">{H(Uri.TryCreate(u, UriKind.Absolute, out var x) ? x.Host : u)}</a>")) + "</span>",
                    _ => "",
                };
                sb.Append($"<tr><td><code>{H(f.Code)}</code></td><td>{state}</td><td>{H(f.DisplayDescription)}{hint}{origin}</td><td class=\"muted\">{H(details)}</td></tr>");
            }
            sb.Append("</table></div></section>");
        }
        if (scan.Health is { Trends.Count: > 0 } health)
        {
            sb.Append($"<section><h2>Health over time</h2><p class=\"muted\">{H(HealthIntro(health))} Slow changes show up here before they become faults.</p><div class=\"table-wrap\"><table><tr><th>Level</th><th>Finding</th></tr>");
            foreach (var t in health.Trends)
                sb.Append($"<tr><td>{(t.Level == TrendLevel.Act ? "<span class=\"bad\">Act</span>" : t.Level == TrendLevel.Watch ? "<span class=\"warn\">Watch</span>" : "Note")}</td><td><b>{H(t.Title)}</b><br><span class=\"muted\">{H(t.Detail)}</span></td></tr>");
            sb.Append("</table></div></section>");
        }
        if (scan.Story is { IsEmpty: false } story)
        {
            sb.Append("<section><h2>The car's story</h2><p class=\"muted\">Every dash message the instrument cluster still remembers, grouped into what happened. Newest first.</p><div class=\"table-wrap\"><table><tr><th>When</th><th>What happened</th></tr>");
            foreach (var e in story.Events.Reverse())
                sb.Append($"<tr><td><code>{H(e.When)}</code></td><td><b>{H(e.Title)}</b><br><span class=\"muted\">{H(e.Explanation)}</span><br><span class=\"muted\">{H(string.Join(" ", e.Messages.Select(m => m.Code)))}</span></td></tr>");
            sb.Append("</table></div></section>");
        }
        if (scan.InfoEntries > 0)
            sb.Append($"<p class=\"muted\">Entries marked <b>{Fault.InfoMemoryLabel}</b> are events modules logged in the background; they never light a lamp on their own.</p>");
        sb.Append("<p class=\"muted\">Generated by Klemme15. Fault descriptions come from the community database and may be incomplete.</p>");
        sb.Append("</main></body></html>");
        return sb.ToString();
    }
}
