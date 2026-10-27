using System.Globalization;
using System.Reflection;
using System.Text.Json;
using OpenHood.Core;

namespace OpenHood.Data;

public sealed record VehicleFamily(string Id, string Name, IReadOnlyList<string> Models, string Cable, IReadOnlyList<EcuDefinition> Modules);

/// <summary>
/// Loads the built-in community database, then layers any JSON files found in a user folder on top,
/// so people can add or fix entries without rebuilding the app.
/// </summary>
public sealed class VehicleDatabase : IFaultCodeDatabase
{
    private readonly Dictionary<string, EcuDefinition> _modules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Text, string? Hint)> _obd = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _bmw = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<VehicleFamily> _vehicles = [];

    public IReadOnlyList<VehicleFamily> Vehicles => _vehicles;
    public IReadOnlyCollection<EcuDefinition> Modules => _modules.Values;
    public IReadOnlyList<string> LoadWarnings => _warnings;
    private readonly List<string> _warnings = [];

    /// <summary>Default per-user override folder, e.g. %APPDATA%/OpenHood/db.</summary>
    public static string UserFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenHood", "db");

    public static VehicleDatabase Load(string? overrideFolder = null)
    {
        var db = new VehicleDatabase();
        foreach (var name in new[] { "modules.json", "obd-codes.json", "bmw-codes.json", "vehicles.json" })
        {
            db.LoadEmbedded(name);
            var folder = overrideFolder ?? UserFolder;
            var path = Path.Combine(folder, name);
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

    public string? DescribeObd(string code)
    {
        if (_obd.TryGetValue(code, out var entry))
            return entry.Hint is null ? entry.Text : $"{entry.Text}. Tip: {entry.Hint}";
        return ObdCodes.Fallback(code);
    }

    public string? DescribeBmw(string ecuId, string code) =>
        _bmw.TryGetValue(ecuId, out var codes) && codes.TryGetValue(code, out var text) ? text : null;

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
            case "vehicles.json":
                foreach (var v in root.GetProperty("vehicles").EnumerateArray())
                {
                    var id = v.GetProperty("id").GetString()!;
                    var modules = new List<EcuDefinition>();
                    foreach (var mid in v.GetProperty("modules").EnumerateArray().Select(x => x.GetString()!))
                    {
                        if (_modules.TryGetValue(mid, out var def)) modules.Add(def);
                        else _warnings.Add($"vehicles.json: {id} lists unknown module {mid}");
                    }
                    _vehicles.RemoveAll(x => x.Id == id);
                    _vehicles.Add(new VehicleFamily(
                        id,
                        v.GetProperty("name").GetString()!,
                        v.TryGetProperty("models", out var models) ? models.EnumerateArray().Select(x => x.GetString()!).ToList() : [],
                        v.GetProperty("cable").GetString()!,
                        modules));
                }
                break;
        }
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
        };
    }
}
