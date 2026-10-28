using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenHood.Core;

namespace OpenHood.Data;

/// <summary>Where on the car drawing a fault points, and what to check there.</summary>
public sealed record FaultLocation(SchematicPoint Position, string Title, bool IsComponent);

/// <summary>
/// Loads the built-in community database, then layers any JSON files found in a user folder on top,
/// so people can add or fix entries without rebuilding the app.
/// </summary>
public sealed class VehicleDatabase : IFaultCodeDatabase
{
    private static readonly string[] Files = ["modules.json", "obd-codes.json", "bmw-codes.json", "components.json", "vehicles.json"];

    private readonly Dictionary<string, EcuDefinition> _modules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Text, string? Hint)> _obd = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _bmw = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CarComponent> _components = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Regex Match, string Component)> _rules = [];
    private readonly List<VehicleFamily> _vehicles = [];
    private readonly List<string> _warnings = [];

    public IReadOnlyList<VehicleFamily> Vehicles => _vehicles;
    public IReadOnlyCollection<EcuDefinition> Modules => _modules.Values;
    public IReadOnlyCollection<CarComponent> Components => _components.Values;
    public IReadOnlyList<string> LoadWarnings => _warnings;

    /// <summary>Default per-user override folder, e.g. %APPDATA%/OpenHood/db.</summary>
    public static string UserFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenHood", "db");

    public static VehicleDatabase Load(string? overrideFolder = null)
    {
        var db = new VehicleDatabase();
        foreach (var name in Files)
        {
            db.LoadEmbedded(name);
            var path = Path.Combine(overrideFolder ?? UserFolder, name);
            if (File.Exists(path))
            {
                try { db.Apply(name, File.ReadAllText(path)); }
                catch (Exception e) { db._warnings.Add($"{path}: {e.Message}"); }
            }
        }
        return db;
    }

    public VehicleFamily? FindVehicle(string id) =>
        _vehicles.FirstOrDefault(v => v.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public VehicleModel? FindModel(string id) =>
        _vehicles.SelectMany(v => v.Models).FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public VehicleFamily? FamilyOf(VehicleModel model) => FindVehicle(model.FamilyId);

    public CarComponent? FindComponent(string id) => _components.GetValueOrDefault(id);

    public string? DescribeObd(string code)
    {
        if (_obd.TryGetValue(code, out var entry))
            return entry.Hint is null ? entry.Text : $"{entry.Text}. Tip: {entry.Hint}";
        return ObdCodes.Fallback(code);
    }

    public string? DescribeBmw(string ecuId, string code) =>
        _bmw.TryGetValue(ecuId, out var codes) && codes.TryGetValue(code, out var text) ? text : null;

    /// <summary>
    /// Where to point on the drawing for a fault: the component the code implicates when we know it,
    /// otherwise the module that reported it.
    /// </summary>
    public FaultLocation? Locate(Fault fault, EcuDefinition ecu, VehicleModel? demoModel = null)
    {
        string? componentId = null;
        if (demoModel is not null)
            componentId = demoModel.DemoFaults.FirstOrDefault(d => d.ModuleId == ecu.Id && d.Code.Equals(fault.Code, StringComparison.OrdinalIgnoreCase))?.Component;
        if (componentId is null && fault.Source == FaultSource.Obd2)
        {
            foreach (var (match, component) in _rules)
            {
                var m = match.Match(fault.Code);
                if (m.Success) { componentId = m.Result(component); break; }
            }
        }
        if (componentId is not null && _components.TryGetValue(componentId, out var c))
            return new FaultLocation(c.Position, c.Name, true);
        return ecu.Position is { } p ? new FaultLocation(p, ecu.Name, false) : null;
    }

    /// <summary>Fault descriptions for a demo car: the real database plus the demo's own made-up BMW codes.</summary>
    public IFaultCodeDatabase ForDemo(VehicleModel model) => new DemoCodes(this, model);

    private sealed class DemoCodes(VehicleDatabase inner, VehicleModel model) : IFaultCodeDatabase
    {
        public string? DescribeObd(string code) => inner.DescribeObd(code);

        public string? DescribeBmw(string ecuId, string code) =>
            model.DemoFaults.FirstOrDefault(d => d.ModuleId == ecuId && d.Code.Equals(code, StringComparison.OrdinalIgnoreCase))?.Text
            ?? inner.DescribeBmw(ecuId, code);
    }

    // ------------------------------------------------------------------ loading

    private void LoadEmbedded(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"OpenHood.Data.{name}")
            ?? throw new InvalidOperationException($"Built-in database file {name} is missing from the build");
        using var reader = new StreamReader(stream);
        Apply(name, reader.ReadToEnd());
    }

    private void Apply(string name, string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        switch (name)
        {
            case "modules.json":
                foreach (var m in root.GetProperty("modules").EnumerateArray())
                {
                    var def = ParseModule(m);
                    _modules[def.Id] = def;
                }
                break;
            case "obd-codes.json":
                foreach (var c in root.GetProperty("codes").EnumerateObject())
                    _obd[c.Name] = (c.Value.GetProperty("text").GetString()!, c.Value.TryGetProperty("hint", out var h) ? h.GetString() : null);
                break;
            case "bmw-codes.json":
                foreach (var module in root.GetProperty("modules").EnumerateObject())
                {
                    if (!_bmw.TryGetValue(module.Name, out var map))
                        _bmw[module.Name] = map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var c in module.Value.EnumerateObject())
                        map[c.Name] = c.Value.ValueKind == JsonValueKind.String ? c.Value.GetString()! : c.Value.GetProperty("text").GetString()!;
                }
                break;
            case "components.json":
                if (root.TryGetProperty("components", out var comps))
                    foreach (var c in comps.EnumerateObject())
                        _components[c.Name] = new CarComponent(c.Name, c.Value.GetProperty("name").GetString()!, ParsePoint(c.Value.GetProperty("pos")));
                if (root.TryGetProperty("rules", out var rules))
                {
                    _rules.Clear();
                    foreach (var r in rules.EnumerateArray())
                        _rules.Add((new Regex(r.GetProperty("match").GetString()!, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
                                    r.GetProperty("component").GetString()!));
                }
                break;
            case "vehicles.json":
                foreach (var v in root.GetProperty("vehicles").EnumerateArray())
                    AddFamily(v);
                break;
        }
    }

    private void AddFamily(JsonElement v)
    {
        var id = v.GetProperty("id").GetString()!;
        var modules = new List<EcuDefinition>();
        foreach (var mid in v.GetProperty("modules").EnumerateArray().Select(x => x.GetString()!))
        {
            if (_modules.TryGetValue(mid, out var def)) modules.Add(def);
            else _warnings.Add($"vehicles.json: {id} lists unknown module {mid}");
        }

        var models = new List<VehicleModel>();
        if (v.TryGetProperty("models", out var ms))
        {
            foreach (var m in ms.EnumerateArray())
            {
                if (m.ValueKind != JsonValueKind.Object) continue;
                var model = ParseModel(id, m);
                foreach (var missing in model.ModuleIds.Where(x => !_modules.ContainsKey(x)))
                    _warnings.Add($"vehicles.json: model {model.Id} lists unknown module {missing}");
                foreach (var f in model.DemoFaults)
                {
                    if (!model.ModuleIds.Contains(f.ModuleId)) _warnings.Add($"vehicles.json: model {model.Id} has a demo fault in a module it doesn't fit ({f.ModuleId})");
                    if (f.Component is { } c && !_components.ContainsKey(c)) _warnings.Add($"vehicles.json: model {model.Id} demo fault points at unknown component {c}");
                }
                models.Add(model);
            }
        }

        _vehicles.RemoveAll(x => x.Id == id);
        _vehicles.Add(new VehicleFamily(
            id,
            v.GetProperty("name").GetString()!,
            v.TryGetProperty("years", out var years) ? years.GetString() ?? "" : "",
            v.GetProperty("cable").GetString()!,
            modules,
            models));
    }

    private static VehicleModel ParseModel(string familyId, JsonElement m)
    {
        string Str(string p) => m.TryGetProperty(p, out var x) ? x.GetString() ?? "" : "";
        var demo = m.TryGetProperty("demo", out var d) ? d : default;
        var faults = new List<DemoFault>();
        if (demo.ValueKind == JsonValueKind.Object && demo.TryGetProperty("faults", out var fs))
        {
            foreach (var f in fs.EnumerateArray())
            {
                faults.Add(new DemoFault
                {
                    ModuleId = f.GetProperty("module").GetString()!,
                    Code = f.GetProperty("code").GetString()!,
                    Present = f.TryGetProperty("present", out var p) && p.ValueKind == JsonValueKind.True,
                    Text = f.TryGetProperty("text", out var t) ? t.GetString() : null,
                    Component = f.TryGetProperty("component", out var c) ? c.GetString() : null,
                });
            }
        }
        return new VehicleModel
        {
            Id = Str("id"),
            FamilyId = familyId,
            Name = Str("name"),
            Body = Str("body"),
            Years = Str("years"),
            Engine = Str("engine"),
            Drive = Str("drive"),
            ModuleIds = m.TryGetProperty("modules", out var mods) ? mods.EnumerateArray().Select(x => x.GetString()!).ToList() : [],
            DemoFaults = faults,
            IdleRpm = demo.ValueKind == JsonValueKind.Object && demo.TryGetProperty("idle", out var idle) ? idle.GetInt32() : 700,
            Turbo = demo.ValueKind == JsonValueKind.Object && demo.TryGetProperty("turbo", out var turbo) && turbo.ValueKind == JsonValueKind.True,
        };
    }

    private static SchematicPoint ParsePoint(JsonElement e)
    {
        var a = e.EnumerateArray().Select(x => x.GetDouble()).ToArray();
        return new SchematicPoint(a[0], a[1]);
    }

    private static EcuDefinition ParseModule(JsonElement m)
    {
        string? Str(string p) => m.TryGetProperty(p, out var v) ? v.GetString() : null;
        bool Bool(string p) => m.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.True;
        var id = Str("id") ?? throw new FormatException("module without id");
        return new EcuDefinition
        {
            Id = id,
            Name = Str("name") ?? id,
            Address = byte.Parse(Str("address") ?? throw new FormatException($"{id}: no address"), NumberStyles.HexNumber),
            Protocol = Enum.Parse<BusProtocol>(Str("protocol") ?? "Obd2", ignoreCase: true),
            Category = Str("category") ?? "Other",
            Sgbd = m.TryGetProperty("sgbd", out var s) ? s.EnumerateArray().Select(x => x.GetString()!).ToList() : [],
            Chassis = [id.Split('.')[0]],
            IsEngine = Bool("engine"),
            Verified = Bool("verified"),
            Position = m.TryGetProperty("pos", out var pos) ? ParsePoint(pos) : null,
        };
    }
}
