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
                sb.AppendLine($"- **{f.Code}** ({f.State}) {f.DisplayDescription}");
        }
        return sb.ToString();
    }

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
                var state = f.IsInfo ? "Info memory" : f.State.ToString();
                var hint = f.Hint is null ? "" : $"<br><span class=\"muted\">Tip: {H(f.Hint)}</span>";
                var origin = f.Confidence switch
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
