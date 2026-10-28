using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Klemme15.Core;

namespace Klemme15.Telemetry;

public sealed record TelemetryOptions(int HttpPort = 8080, string? MqttHost = null, int MqttPort = 1883,
    string MqttTopic = "klemme15", string? MqttUser = null, string? MqttPass = null, double IntervalSeconds = 1);

/// <summary>
/// Headless telemetry: polls the car's live values on a timer and serves them as JSON over HTTP (for Grafana, a web
/// dashboard or a phone) and optionally publishes each value to an MQTT broker. Read-only; it never writes to the car.
/// Meant to run on a Raspberry Pi left plugged into the OBD port: <c>klemme15 serve</c>.
/// </summary>
public sealed class TelemetryServer(IDiagnosticBackend backend, EcuDefinition engine, IReadOnlyList<LiveParameter> parameters,
    TelemetryOptions options, TrafficLog? log = null)
{
    private readonly object _gate = new();
    private IReadOnlyList<LiveValue> _latest = [];
    private DateTimeOffset _at;
    private long _reads, _errors;
    private string? _lastError;

    public async Task RunAsync(CancellationToken ct)
    {
        await using var mqtt = options.MqttHost is { } host && host.Length > 0 ? new MqttPublisher(host!, options.MqttPort, "klemme15", options.MqttUser, options.MqttPass) : null;
        if (mqtt is not null)
        {
            await mqtt.ConnectAsync(ct);
            log?.Note($"Telemetry: connected to MQTT broker {options.MqttHost}:{options.MqttPort}");
        }
        using var http = new HttpListener();
        http.Prefixes.Add($"http://+:{options.HttpPort}/");
        try { http.Start(); }
        catch (HttpListenerException) when (OperatingSystem.IsWindows())
        {
            http.Prefixes.Clear();
            http.Prefixes.Add($"http://localhost:{options.HttpPort}/");
            http.Start();
        }
        log?.Note($"Telemetry: serving http://localhost:{options.HttpPort}/ (endpoints: /live /live.json /faults /health /)");

        var serving = ServeHttpAsync(http, ct);
        var pinging = PingAsync(mqtt, ct);
        try
        {
            var interval = TimeSpan.FromSeconds(Math.Max(0.2, options.IntervalSeconds));
            while (!ct.IsCancellationRequested)
            {
                var start = DateTimeOffset.UtcNow;
                try
                {
                    var values = await backend.ReadLiveAsync(engine, parameters, ct);
                    lock (_gate) { _latest = values; _at = DateTimeOffset.UtcNow; _reads++; _lastError = null; }
                    if (mqtt is not null)
                        foreach (var v in values.Where(v => v.Value is not null))
                            await mqtt.PublishAsync($"{options.MqttTopic}/{v.Parameter.Id}", v.Value!.Value.ToString(CultureInfo.InvariantCulture), ct: ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception e) when (e is DiagnosticException or TimeoutException or IOException)
                {
                    lock (_gate) { _errors++; _lastError = e.Message; }
                }
                var wait = interval - (DateTimeOffset.UtcNow - start);
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            }
        }
        catch (OperationCanceledException) { }
        http.Close();
        await Task.WhenAll(serving, pinging);
    }

    private async Task PingAsync(MqttPublisher? mqtt, CancellationToken ct)
    {
        if (mqtt is null) return;
        try { while (!ct.IsCancellationRequested) { await Task.Delay(TimeSpan.FromSeconds(30), ct); await mqtt.PingAsync(ct); } }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }

    private async Task ServeHttpAsync(HttpListener http, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await http.GetContextAsync(); }
            catch (Exception) { break; }   // listener closed
            _ = Task.Run(() => Respond(context), ct);
        }
    }

    private void Respond(HttpListenerContext context)
    {
        try
        {
            var (body, type) = context.Request.Url?.AbsolutePath switch
            {
                "/" => (IndexHtml(), "text/html; charset=utf-8"),
                "/health" => (Health(), "application/json"),
                "/faults" => (FaultsJson(), "application/json"),
                _ => (LiveJson(), "application/json"),
            };
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.ContentType = type;
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes);
        }
        catch (Exception) { /* client went away */ }
        finally { try { context.Response.Close(); } catch { /* ignore */ } }
    }

    private string LiveJson()
    {
        IReadOnlyList<LiveValue> values; DateTimeOffset at;
        lock (_gate) { values = _latest; at = _at; }
        var sb = new StringBuilder("{\"time\":\"").Append(at.ToString("o")).Append("\",\"values\":{");
        for (var i = 0; i < values.Count; i++)
        {
            var v = values[i];
            sb.Append(i > 0 ? "," : "").Append('"').Append(v.Parameter.Id).Append("\":{\"value\":")
              .Append(v.Value?.ToString(CultureInfo.InvariantCulture) ?? "null")
              .Append(",\"unit\":").Append(JsonSerializer.Serialize(v.Parameter.Unit))
              .Append(",\"name\":").Append(JsonSerializer.Serialize(v.Parameter.Name)).Append('}');
        }
        return sb.Append("}}").ToString();
    }

    private string Health()
    {
        lock (_gate)
            return JsonSerializer.Serialize(new { ok = _lastError is null, reads = _reads, errors = _errors, lastError = _lastError, lastUpdate = _at.ToString("o") });
    }

    private string FaultsJson()
    {
        try
        {
            var faults = backend.ReadFaultsAsync(engine).GetAwaiter().GetResult();
            return JsonSerializer.Serialize(faults.Select(f => new { code = f.Code, state = f.State.ToString(), f.Description, priority = FaultTriage.Assess(f).Priority.ToString() }));
        }
        catch (Exception e) { return JsonSerializer.Serialize(new { error = e.Message }); }
    }

    private string IndexHtml()
    {
        var rows = string.Join("", parameters.Select(p => $"<tr><td>{p.Name}</td><td id=\"{p.Id}\">-</td><td>{p.Unit}</td></tr>"));
        const string css = "body{font-family:system-ui,sans-serif;margin:2rem;background:#121a23;color:#dce3ea}"
            + "h1{font-size:1.2rem}table{border-collapse:collapse;width:100%;max-width:480px}"
            + "td{padding:.4rem .6rem;border-bottom:1px solid #2a3746}td:nth-child(2){text-align:right;font-variant-numeric:tabular-nums;font-weight:600}"
            + "td:nth-child(3){color:#8796a5;width:4rem}";
        const string js = "async function tick(){try{const r=await fetch('/live.json');const d=await r.json();"
            + "for(const k in d.values){const e=document.getElementById(k);if(e)e.textContent=(d.values[k].value==null?'-':d.values[k].value);}}catch(e){}}"
            + "setInterval(tick,1000);tick();";
        return "<!doctype html><html><head><meta charset=\"utf-8\"><title>Klemme15</title>"
            + "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><style>" + css + "</style></head>"
            + "<body><h1>Klemme15 live data</h1><table>" + rows + "</table><script>" + js + "</script></body></html>";
    }
}
