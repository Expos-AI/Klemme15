using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Klemme15.Core;

namespace Klemme15.Data;

/// <summary>Where on the car drawing a fault points, and what to check there.</summary>
public sealed record FaultLocation(SchematicPoint Position, string Title, bool IsComponent);

/// <summary>
/// Loads the built-in community database, then layers any JSON files found in a user folder on top,
/// so people can add or fix entries without rebuilding the app.
/// </summary>
public sealed class VehicleDatabase : IFaultCodeDatabase, IFaultCodeDetails, IFaultDescriber, ICheckControlCatalog
{
    private readonly List<IOfficialFaultSource> _official = [];

    /// <summary>Official fault dictionaries the user has made available (their BMW files, a downloaded database), checked in order.</summary>
    public IReadOnlyList<IOfficialFaultSource> OfficialSources => _official;

    private GuideLibrary? _guides;

    /// <summary>Measurement tables from the optional full database download, used for native module readings.</summary>
    public IMeasurementTables? Measurements { get; set; }

    /// <summary>Klemme15's troubleshooting guides (db/guides.json).</summary>
    public GuideLibrary Guides => _guides ??= GuideLibrary.Load();

    private ServiceLibrary? _service;

    /// <summary>Klemme15's service functions (db/service.json).</summary>
    public ServiceLibrary Service => _service ??= ServiceLibrary.Load();

    private Klemme15.Core.Coding.VerifiedCodingLibrary? _verifiedCoding;

    /// <summary>Where this database reads user overrides from (set when it was loaded).</summary>
    public string OverrideFolder { get; private set; } = UserFolder;

    /// <summary>
    /// Community-verified coding maps (the embedded db/coding-verified.json plus any the user dropped in their override
    /// folder, whose entries are appended). Lets the comfort toggles work on a matching module without NCS data.
    /// </summary>
    public Klemme15.Core.Coding.VerifiedCodingLibrary VerifiedCoding => _verifiedCoding ??= LoadVerifiedCoding();

    private Klemme15.Core.Coding.VerifiedCodingLibrary LoadVerifiedCoding()
    {
        var jsons = new List<string>();
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Klemme15.Data.coding-verified.json"))
            if (stream is not null) { using var reader = new StreamReader(stream); jsons.Add(reader.ReadToEnd()); }
        var userPath = Path.Combine(OverrideFolder, "coding-verified.json");
        if (File.Exists(userPath)) { try { jsons.Add(File.ReadAllText(userPath)); } catch (IOException) { } }
        var entries = jsons.SelectMany(j => Klemme15.Core.Coding.VerifiedCodingLibrary.Load(j).Entries).ToList();
        return Klemme15.Core.Coding.VerifiedCodingLibrary.FromEntries(entries);
    }

    /// <summary>Replaces the official sources (e.g. after the user downloads a database or picks their BMW folder).</summary>
    public void SetOfficialSources(IEnumerable<IOfficialFaultSource> sources)
    {
        _official.Clear();
        _official.AddRange(sources);
        CheckControl.SetOfficialSources(_official.OfType<ICheckControlTextSource>());
    }

    private CheckControlCatalog? _checkControl;

    /// <summary>The Check Control (dash message) list: db/check-control.json plus the user's override.</summary>
    public CheckControlCatalog CheckControl
    {
        get
        {
            if (_checkControl is null)
            {
                _checkControl = CheckControlCatalog.Load(OverrideFolder);
                _checkControl.SetOfficialSources(_official.OfType<ICheckControlTextSource>());
            }
            return _checkControl;
        }
    }

    public CheckControlInfo? LookupCheckControl(int id) => CheckControl.LookupCheckControl(id);
    public (string Text, string Source)? OfficialCheckControlText(int id) => CheckControl.OfficialCheckControlText(id);

    private static readonly string[] Files = ["modules.json", "obd-generic.json", "obd-codes.json", "bmw-codes.json", "components.json", "vehicles.json"];

    private readonly Dictionary<string, EcuDefinition> _modules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Text, string? Hint)> _obd = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<BmwCodeEntry>> _bmw = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>One researched meaning of a BMW code; <see cref="Engines"/> limits it to certain engines.</summary>
    private sealed record BmwCodeEntry(string Text, string? Hint, string? Component, string? Confidence, IReadOnlyList<string> Sources, IReadOnlyList<string>? Engines, bool Harmless = false);
    private readonly Dictionary<string, CarComponent> _components = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Regex Match, string Component)> _rules = [];
    private readonly List<VehicleFamily> _vehicles = [];
    private readonly List<string> _warnings = [];

    public IReadOnlyList<VehicleFamily> Vehicles => _vehicles;
    public IReadOnlyCollection<EcuDefinition> Modules => _modules.Values;
    public IReadOnlyCollection<CarComponent> Components => _components.Values;
    public IReadOnlyList<string> LoadWarnings => _warnings;

    /// <summary>Default per-user override folder, e.g. %APPDATA%/Klemme15/db.</summary>
    public static string UserFolder
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
            var current = Path.Combine(appData, "Klemme15", "db");
            var legacy = Path.Combine(appData, "OpenHood", "db");   // before the rename
            return !Directory.Exists(current) && Directory.Exists(legacy) ? legacy : current;
        }
    }

    public static VehicleDatabase Load(string? overrideFolder = null)
    {
        var db = new VehicleDatabase { OverrideFolder = overrideFolder ?? UserFolder };
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

    /// <summary>Addresses worth probing for modules this family's database doesn't list (skips functional/tester addresses).</summary>
    public static IReadOnlyList<byte> UnlistedAddresses(VehicleFamily family) =>
        Enumerable.Range(0, 0xF0).Select(i => (byte)i)
            .Where(a => a != 0xDF && family.Modules.All(m => m.Address != a))
            .ToList();

    public string? DescribeObd(string code)
    {
        if (_obd.TryGetValue(code, out var entry))
            return entry.Hint is null ? entry.Text : $"{entry.Text}. Tip: {entry.Hint}";
        return ObdCodes.Fallback(code);
    }

    public string? DescribeBmw(string ecuId, string code) => LookupBmw(ecuId, code)?.Text;

    public CodeInfo? LookupBmw(string ecuId, string code) => LookupBmw(ecuId, code, null);

    /// <summary>
    /// Looks up a BMW code, preferring the meaning recorded for this engine. If the only meaning on file
    /// is for a different engine, it is returned with a warning, because engine computers reuse numbers.
    /// </summary>
    public CodeInfo? LookupBmw(string ecuId, string code, string? engine)
    {
        if (!_bmw.TryGetValue($"{ecuId}/{code}", out var list) || list.Count == 0) return null;
        static bool Matches(BmwCodeEntry e, string eng) => e.Engines!.Any(x => eng.StartsWith(x, StringComparison.OrdinalIgnoreCase) || x.StartsWith(eng, StringComparison.OrdinalIgnoreCase));
        var pick = engine is not null ? list.FirstOrDefault(e => e.Engines is not null && Matches(e, engine)) : null;
        pick ??= list.FirstOrDefault(e => e.Engines is null);
        var text = pick?.Text;
        if (pick is null)
        {
            pick = list[0];
            var engines = string.Join("/", pick.Engines!);
            text = engine is null
                ? $"{pick.Text} (meaning recorded for {engines} engines)"
                : $"{pick.Text} (meaning recorded for {engines}; may differ on your {engine})";
        }
        return new CodeInfo(text!, pick.Hint, pick.Confidence, pick.Sources, pick.Component, pick.Harmless);
    }

    public Fault Describe(Fault fault, EcuDefinition ecu, EcuIdentity? identity) => Describe(fault, ecu, identity, null);

    /// <summary>
    /// Fills in what a fault means. Order of preference for the headline text: BMW's own English, our researched
    /// community meaning, BMW's German put through the glossary, BMW's German as is. BMW's original wording, the
    /// module variant and community tips are always attached when known, so nothing is hidden.
    /// </summary>
    public Fault Describe(Fault fault, EcuDefinition ecu, EcuIdentity? identity, string? engine, string? fallbackText = null)
    {
        if (fault.Source == FaultSource.Obd2)
        {
            if (fault.Description is not null) return fault;
            var obd = DescribeObd(fault.Code);
            return fault with { Description = obd ?? fallbackText, DescriptionOrigin = obd is null ? null : "obd" };
        }

        var community = LookupBmw(ecu.Id, fault.Code, engine);
        var match = OfficialLookup.Find(_official, ecu, identity, engine, fault.Code);

        // EDIABAS mode already read BMW's text live from the car's own SGBD.
        string? officialText = fault.Confidence == "official" && fault.Description is not null ? fault.Description : null;
        string? officialEnglish = null;
        if (match is not null)
        {
            officialText ??= OfficialLookup.Clean(match.Entry.Text, fault.Code);
            if (match.Entry.English is { } en) officialEnglish = GermanGlossary.Polish(OfficialLookup.Clean(en, fault.Code));
        }

        string? description; string? origin; string? confidence;
        if (officialEnglish is not null) { description = officialEnglish; origin = "bmw-en"; confidence = "official"; }
        else if (community is not null) { description = community.Text; origin = "community"; confidence = community.Confidence; }
        else if (officialText is not null && GermanGlossary.Translate(officialText) is { } approx) { description = approx; origin = "bmw-de-glossary"; confidence = "official"; }
        else if (officialText is not null) { description = officialText; origin = "bmw-de"; confidence = "official"; }
        else { description = fault.Description ?? fallbackText; origin = description is null ? null : fault.DescriptionOrigin; confidence = fault.Confidence; }

        string? generalHint = null;
        if (description is null)
        {
            // Never leave a code without a meaning: the number range and module give a likely one, with next steps.
            (description, generalHint) = FaultFallback.For(fault, ecu, _official.Count > 0);
            origin = FaultFallback.Origin;
            confidence = null;
        }

        return fault with
        {
            Description = description,
            DescriptionOrigin = origin,
            Confidence = confidence,
            Hint = fault.Hint ?? community?.Hint ?? generalHint,
            UsuallyHarmless = fault.UsuallyHarmless || community?.UsuallyHarmless == true,
            Sources = community?.Sources is { Count: > 0 } src ? src : fault.Sources,
            OfficialText = officialText,
            ModuleVariant = match?.Variant ?? (fault.Confidence == "official" ? identity?.Variant : null),
            VariantConfirmed = match?.VariantConfirmed ?? (fault.Confidence == "official" && identity?.Variant is not null),
            OfficialSource = match?.SourceName ?? (officialText is not null ? "your BMW files (EDIABAS)" : null),
            Alternatives = match?.Alternatives ?? [],
        };
    }

    /// <summary>The same database, with BMW codes read for a specific engine (e.g. "N52").</summary>
    public IFaultCodeDatabase ForEngine(string? engine) => engine is null ? this : new EngineCodes(this, engine);

    private sealed class EngineCodes(VehicleDatabase inner, string engine) : IFaultCodeDatabase, IFaultCodeDetails, IFaultDescriber, ICheckControlCatalog
    {
        public CheckControlInfo? LookupCheckControl(int id) => inner.LookupCheckControl(id);
        public (string Text, string Source)? OfficialCheckControlText(int id) => inner.OfficialCheckControlText(id);
        public Fault Describe(Fault fault, EcuDefinition ecu, EcuIdentity? identity) => inner.Describe(fault, ecu, identity, engine);
        public string? DescribeObd(string code) => inner.DescribeObd(code);
        public string? DescribeBmw(string ecuId, string code) => LookupBmw(ecuId, code)?.Text;
        public CodeInfo? LookupBmw(string ecuId, string code) => inner.LookupBmw(ecuId, code, engine);
    }

    /// <summary>
    /// Where to point on the drawing for a fault: the component the code implicates when we know it,
    /// otherwise the module that reported it.
    /// </summary>
    public FaultLocation? Locate(Fault fault, EcuDefinition ecu, VehicleModel? demoModel = null)
    {
        string? componentId = null;
        if (demoModel is not null)
            componentId = demoModel.DemoFaults.FirstOrDefault(d => d.ModuleId == ecu.Id && d.Code.Equals(fault.Code, StringComparison.OrdinalIgnoreCase))?.Component;
        if (componentId is null && fault.Source == FaultSource.Bmw && _bmw.TryGetValue($"{ecu.Id}/{fault.Code}", out var known))
            componentId = known.Select(k => k.Component).FirstOrDefault(c => c is not null);
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
        if (ecu.Position is { } p) return new FaultLocation(p, ecu.Name, false);
        // Modules inside another one (e.g. navigation inside the head unit) point at their host.
        if (ecu.PartOf is { } host && _modules.TryGetValue(host, out var parent) && parent.Position is { } pp)
            return new FaultLocation(pp, $"{parent.Name} ({ecu.Name})", false);
        return null;
    }

    /// <summary>Fault descriptions for a demo car: the real database plus the demo's own made-up BMW codes.</summary>
    public IFaultCodeDatabase ForDemo(VehicleModel model) => new DemoCodes(this, model);

    private sealed class DemoCodes(VehicleDatabase inner, VehicleModel model) : IFaultCodeDatabase, IFaultCodeDetails, IFaultDescriber, ICheckControlCatalog
    {
        public CheckControlInfo? LookupCheckControl(int id) => inner.LookupCheckControl(id);
        public (string Text, string Source)? OfficialCheckControlText(int id) => inner.OfficialCheckControlText(id);
        public Fault Describe(Fault fault, EcuDefinition ecu, EcuIdentity? identity) =>
            inner.Describe(fault, ecu, identity, model.EngineCode,
                model.DemoFaults.FirstOrDefault(d => d.ModuleId == ecu.Id && d.Code.Equals(fault.Code, StringComparison.OrdinalIgnoreCase))?.Text);

        public string? DescribeObd(string code) => inner.DescribeObd(code);

        public string? DescribeBmw(string ecuId, string code) => LookupBmw(ecuId, code)?.Text;

        public CodeInfo? LookupBmw(string ecuId, string code) =>
            model.DemoFaults.FirstOrDefault(d => d.ModuleId == ecuId && d.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) is { Text: { } t }
                ? new CodeInfo(t)
                : inner.LookupBmw(ecuId, code, model.EngineCode);
    }

    // ------------------------------------------------------------------ loading

    private void LoadEmbedded(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Klemme15.Data.{name}")
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
            case "obd-generic.json":
                foreach (var c in root.GetProperty("codes").EnumerateObject())
                    _obd.TryAdd(c.Name, (c.Value.GetString()!, null));
                break;
            case "obd-codes.json":
                foreach (var c in root.GetProperty("codes").EnumerateObject())
                    _obd[c.Name] = (c.Value.GetProperty("text").GetString()!, c.Value.TryGetProperty("hint", out var h) ? h.GetString() : null);
                break;
            case "bmw-codes.json":
                foreach (var module in root.GetProperty("modules").EnumerateObject())
                    foreach (var c in module.Value.EnumerateObject())
                    {
                        var key = $"{module.Name}/{c.Name}";
                        var list = new List<BmwCodeEntry>();
                        if (c.Value.ValueKind == JsonValueKind.Array)
                            list.AddRange(c.Value.EnumerateArray().Select(ParseCode));
                        else
                            list.Add(ParseCode(c.Value));
                        _bmw[key] = list;   // later files (user overrides) replace earlier entries
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
            DemoCheckControl = demo.ValueKind == JsonValueKind.Object && demo.TryGetProperty("checkControl", out var cc) && cc.ValueKind == JsonValueKind.Array
                ? cc.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out _)).Select(x => x.GetInt32()).ToList()
                : [],
            IdleRpm = demo.ValueKind == JsonValueKind.Object && demo.TryGetProperty("idle", out var idle) ? idle.GetInt32() : 700,
            Turbo = demo.ValueKind == JsonValueKind.Object && demo.TryGetProperty("turbo", out var turbo) && turbo.ValueKind == JsonValueKind.True,
        };
    }

    private static BmwCodeEntry ParseCode(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String) return new BmwCodeEntry(e.GetString()!, null, null, null, [], null);
        string? Str(string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        List<string>? List(string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(x => x.GetString()!).ToList() : null;
        return new BmwCodeEntry(Str("text") ?? "", Str("hint"), Str("component"), Str("confidence"), List("sources") ?? [], List("engines"),
            e.TryGetProperty("usually_harmless", out var h) && h.ValueKind == JsonValueKind.True);
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
            Variants = m.TryGetProperty("variants", out var vs) ? vs.EnumerateArray().Select(x => x.GetString()!).ToList() : [],
            Chassis = [id.Split('.')[0]],
            IsEngine = Bool("engine"),
            Verified = Bool("verified"),
            Position = m.TryGetProperty("pos", out var pos) ? ParsePoint(pos) : null,
            Optional = Bool("optional"),
            PartOf = Str("partOf"),
            Note = Str("note"),
        };
    }
}
