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
        sb.AppendLine($"# Klemme15 scan {scan.Started:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine($"- Vehicle: {scan.Chassis ?? "unknown"}  VIN: {scan.Vin ?? "not read"}");
        sb.AppendLine($"- Backend: {scan.Backend}  Duration: {scan.Elapsed.TotalSeconds:0.0}s{(scan.Cancelled ? " (cancelled)" : "")}");
        sb.AppendLine($"- Modules answering: {scan.RespondingEcus}/{scan.Ecus.Count}  Faults: {scan.TotalFaults}{(scan.InfoEntries > 0 ? $"  Info memory: {scan.InfoEntries}" : "")}{(scan.BatteryVoltage is { } v ? $"  Battery: {v:0.0} V" : "")}");
        sb.AppendLine();
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
                sb.AppendLine($"- **{f.Code}** ({(f.IsShadow ? "Shadow memory" : f.IsInfo ? "Info memory" : f.State.ToString())}, {verdict.Label.ToLowerInvariant()}) {f.DisplayDescription}");
                sb.AppendLine($"  - {verdict.Advice}");
                if (f.OfficialText is { } bmw && bmw != f.Description) sb.AppendLine($"  - BMW text: {bmw}");
                if (f.ModuleVariant is { } mod) sb.AppendLine($"  - Module {mod}{(f.VariantConfirmed ? "" : " (most likely)")}");
                if (f.Hint is { } tip) sb.AppendLine($"  - Tip: {tip}");
            }
        }
        return sb.ToString();
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

        var all = scan.Ecus.SelectMany(e => e.Faults.Select(f => (Ecu: e.Ecu, Fault: f, Verdict: FaultTriage.Assess(f)))).ToList();
        void Section(string heading, FaultPriority priority, bool details)
        {
            var items = all.Where(x => x.Verdict.Priority == priority).ToList();
            if (items.Count == 0) return;
            sb.AppendLine($"**{heading}**");
            sb.AppendLine();
            foreach (var (ecu, f, _) in items)
            {
                var state = f.IsShadow ? "shadow" : f.IsInfo ? "info" : f.State switch
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
        if (all.Count == 0) sb.AppendLine("No faults stored.");
        else if (background > 0) sb.AppendLine($"_Plus {background} background entr{(background == 1 ? "y" : "ies")} (info/shadow memory or one-off events) left out._");
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

    public static string ToHtml(VehicleScanResult scan)
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
.brand{display:flex;align-items:center;gap:10px;margin-bottom:18px;font-weight:600;font-size:17px}.brand svg{width:32px;height:32px;flex:none}
@media (prefers-color-scheme:light){.brand svg rect:first-child{stroke:var(--line);stroke-width:8}}
</style></head><body><main>
""");
        sb.Append($"<div class=\"brand\">{Logo.Svg}<span>Klemme15</span></div>");
        sb.Append($"<h1>Scan report: {H(scan.Chassis ?? "Vehicle")}</h1>");
        sb.Append($"<div class=\"muted\">{scan.Started:yyyy-MM-dd HH:mm} &middot; VIN {H(scan.Vin ?? "not read")} &middot; {H(scan.Backend)}</div>");
        sb.Append("<div class=\"stats\">");
        sb.Append($"<div class=\"stat\"><b>{scan.RespondingEcus}/{scan.Ecus.Count}</b>modules answering</div>");
        sb.Append($"<div class=\"stat\"><b>{scan.TotalFaults}</b>faults</div>");
        sb.Append($"<div class=\"stat\"><b>{scan.Elapsed.TotalSeconds:0.0}s</b>scan time</div>");
        if (scan.BatteryVoltage is { } v) sb.Append($"<div class=\"stat\"><b>{v:0.0} V</b>battery</div>");
        sb.Append("</div>");
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
                var state = (f.IsShadow ? "Shadow memory" : f.IsInfo ? "Info memory" : f.State.ToString()) + $"<br><b>{H(FaultTriage.Assess(f).Label)}</b>";
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
        sb.Append("<p class=\"muted\">Generated by Klemme15. Fault descriptions come from the community database and may be incomplete.</p>");
        sb.Append("</main></body></html>");
        return sb.ToString();
    }
}
