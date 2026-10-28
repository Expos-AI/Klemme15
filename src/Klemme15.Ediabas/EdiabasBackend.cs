using System.Globalization;
using EdiabasLib;
using Klemme15.Core;

namespace Klemme15.Ediabas;

public sealed record EdiabasOptions
{
    /// <summary>Folder holding the user's own SGBD files (*.prg, *.grp), e.g. C:\EDIABAS\Ecu.</summary>
    public required string EcuPath { get; init; }

    /// <summary>Cable port, e.g. "COM3".</summary>
    public required string ComPort { get; init; }

    /// <summary>SGBDs to ask for the VIN (usually the engine's), tried in order.</summary>
    public IReadOnlyList<string> VinSgbds { get; init; } = [];

    /// <summary>User-chosen live values, Source = "ediabas:SGBD/JOB/RESULT" or "ediabas:SGBD/JOB/RESULT/ARGS".</summary>
    public IReadOnlyList<LiveParameter> LiveFavorites { get; init; } = [];

    public TrafficLog? Log { get; init; }
}

/// <summary>
/// Runs jobs from the user's own BMW SGBD files through EdiabasLib. Gives the deepest coverage
/// (every module, BMW fault texts, all status jobs) but needs those files. Runs on Windows, macOS and Linux.
/// </summary>
public sealed class EdiabasBackend(EdiabasOptions options) : IDiagnosticBackend, IJobRunner, IModuleReadings
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, string> _resolved = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _variants = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<ModuleReading>> _readingCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _resultComments = new(StringComparer.OrdinalIgnoreCase);
    private EdiabasNet? _ediabas;
    private CancellationToken _jobToken;

    public string Name => $"EDIABAS ({options.ComPort}, {options.EcuPath})";

    public BackendCapabilities Capabilities =>
        BackendCapabilities.Identify | BackendCapabilities.ReadFaults | BackendCapabilities.ClearFaults |
        BackendCapabilities.LiveData | BackendCapabilities.ReadVin | BackendCapabilities.Jobs;

    public bool? IgnitionOn => null;

    public Task ConnectAsync(CancellationToken ct = default) => Locked(() =>
    {
        if (!Directory.Exists(options.EcuPath))
            throw new DiagnosticException($"SGBD folder not found: {options.EcuPath}");
        if (!Directory.EnumerateFiles(options.EcuPath, "*.prg").Any())
            throw new DiagnosticException($"No .prg files in {options.EcuPath}. Point Klemme15 at the Ecu folder of your own EDIABAS install.");

        _ediabas?.Dispose();
        var ediabas = new EdiabasNet();
        var iface = new EdInterfaceObd();
        ediabas.EdInterfaceClass = iface;
        iface.ComPort = options.ComPort;
        ediabas.SetConfigProperty("EcuPath", options.EcuPath);
        ediabas.AbortJobFunc = () => _jobToken.IsCancellationRequested;
        _ediabas = ediabas;
        options.Log?.Note($"EDIABAS ready on {options.ComPort}, SGBDs from {options.EcuPath}");
        return true;
    }, ct);

    public async Task<string?> ReadVinAsync(CancellationToken ct = default)
    {
        foreach (var sgbd in options.VinSgbds.Where(SgbdExists))
        {
            try
            {
                var sets = await RunJobAsync(sgbd, "AIF_LESEN", "", ct);
                var vin = sets.SelectMany(s => s).FirstOrDefault(kv => kv.Key == "AIF_FG_NR").Value;
                if (!string.IsNullOrWhiteSpace(vin)) return vin.Trim();
            }
            catch (DiagnosticException) { /* try next */ }
        }
        return null;
    }

    public async Task<EcuIdentity?> IdentifyAsync(EcuDefinition ecu, CancellationToken ct = default)
    {
        var candidates = ecu.Sgbd.Where(SgbdExists).ToList();
        if (candidates.Count == 0)
            candidates.Add($"D_{ecu.Address:X4}");   // EDIABAS convention for E6x/E9x group files
        foreach (var sgbd in candidates)
        {
            try
            {
                var sets = await RunJobAsync(sgbd, "IDENT", "", ct);
                _resolved[ecu.Id] = sgbd;
                var all = Merge(sets);
                // Run later jobs against the exact variant file, so EDIABAS doesn't re-identify every time.
                if (Get(all, "VARIANTE") is { } variant && SgbdExists(variant)) _variants[ecu.Id] = variant;
                return new EcuIdentity
                {
                    Variant = Get(all, "VARIANTE"),
                    PartNumber = Get(all, "ID_BMW_NR"),
                    HardwareVersion = Get(all, "ID_HW_NR"),
                    SoftwareVersion = Get(all, "ID_SW_NR", "ID_SW_NR_MCV", "ID_SW_NR_FSV"),
                    CodingIndex = Get(all, "ID_COD_INDEX"),
                    DiagnosticIndex = Get(all, "ID_DIAG_INDEX"),
                    Supplier = Get(all, "ID_LIEF_TEXT", "ID_LIEF_NR"),
                    ProductionDate = Get(all, "ID_DATUM"),
                    Raw = all,
                };
            }
            catch (DiagnosticException e) when (IsNoResponse(e.Message))
            {
                return null;
            }
            catch (DiagnosticException)
            {
                // wrong SGBD for this car: try the next candidate
            }
        }
        return null;
    }

    /// <summary>
    /// Reads fault memory the way ISTA does on E-series cars: FS_LESEN for the list, FS_LESEN_DETAIL for each
    /// fault's details (mileage, frequency, environment conditions), then the info memory with IS_LESEN.
    /// Texts come from the car's own SGBD, matched to the module variant that answered.
    /// </summary>
    public Task<IReadOnlyList<Fault>> ReadFaultsAsync(EcuDefinition ecu, CancellationToken ct = default) =>
        Locked<IReadOnlyList<Fault>>(() =>
        {
            var sgbd = SgbdFor(ecu);
            var faults = new List<Fault>();
            faults.AddRange(ReadMemory(ecu, sgbd, "FS_LESEN", "FS_LESEN_DETAIL", isInfo: false, ct));
            try
            {
                if (JobExists("IS_LESEN"))
                    faults.AddRange(ReadMemory(ecu, sgbd, "IS_LESEN", "IS_LESEN_DETAIL", isInfo: true, ct));
            }
            catch (DiagnosticException) { /* info memory is a bonus */ }
            return faults;
        }, ct);

    private const int MaxDetailReads = 25;

    private IEnumerable<Fault> ReadMemory(EcuDefinition ecu, string sgbd, string listJob, string detailJob, bool isInfo, CancellationToken ct)
    {
        var sets = RunJobCore(sgbd, listJob, "", ct);
        var variant = sets.SelectMany(x => x).FirstOrDefault(kv => kv.Key == "VARIANTE").Value ?? sgbd;
        var withDetail = JobExists(detailJob);
        var detailsRead = 0;
        foreach (var set in sets.Where(x => x.ContainsKey("F_ORT_NR")))
        {
            ct.ThrowIfCancellationRequested();
            var hasNumber = long.TryParse(set["F_ORT_NR"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number);
            var code = hasNumber ? number.ToString("X4", CultureInfo.InvariantCulture) : set["F_ORT_NR"];

            var all = new Dictionary<string, string>(set.ToDictionary(kv => kv.Key, kv => kv.Value), StringComparer.OrdinalIgnoreCase);
            if (withDetail && hasNumber && detailsRead < MaxDetailReads)
            {
                try
                {
                    foreach (var kv in Merge(RunJobCore(sgbd, detailJob, $"0x{number:X}", ct))) all[kv.Key] = kv.Value;
                    detailsRead++;
                }
                catch (DiagnosticException) { /* details are optional */ }
            }

            yield return new Fault
            {
                EcuId = ecu.Id,
                Code = code,
                Source = FaultSource.Bmw,
                State = isInfo ? FaultState.Stored : StateFrom(all.GetValueOrDefault("F_VORHANDEN_TEXT")),
                IsInfo = isInfo,
                Description = all.TryGetValue("F_ORT_TEXT", out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null,
                Confidence = "official",
                Sources = [$"Your SGBD file for this module ({variant})"],
                Details = DetailsFrom(all),
            };
        }
    }

    private static FaultState StateFrom(string? present)
    {
        if (string.IsNullOrWhiteSpace(present)) return FaultState.Unknown;
        var p = present.ToLowerInvariant();
        if (p.Contains("nicht") || p.Contains("not ")) return FaultState.Stored;
        if (p.Contains("sporad") || p.Contains("intermit")) return FaultState.Intermittent;
        return FaultState.Present;
    }

    /// <summary>The fault details ISTA shows: mileage, frequency, fault type and environment conditions.</summary>
    private static Dictionary<string, string> DetailsFrom(Dictionary<string, string> r)
    {
        var d = new Dictionary<string, string>();
        static bool Useful(string? v) => !string.IsNullOrWhiteSpace(v) && v.Trim() is not "--" and not "-" and not "0xFFFF";
        void Add(string label, string? value, string? unit = null)
        {
            if (Useful(value)) d[label] = unit is null ? value!.Trim() : $"{value!.Trim()} {unit}";
        }

        Add("Mileage", r.GetValueOrDefault("F_UW_KM"), "km");
        Add("Occurrences", r.GetValueOrDefault("F_HFK"));
        Add("Logged", r.GetValueOrDefault("F_LZ"));
        Add("Status", r.GetValueOrDefault("F_VORHANDEN_TEXT"));
        Add("Warning lamp", r.GetValueOrDefault("F_WARNUNG_TEXT"));
        Add("OBD code", r.GetValueOrDefault("F_PCODE_STRING") ?? r.GetValueOrDefault("F_SAE_CODE_STRING"));

        var types = new List<string>();
        var typeCount = int.TryParse(r.GetValueOrDefault("F_ART_ANZ"), out var tc) ? tc : 6;
        for (var i = 1; i <= Math.Min(typeCount, 9); i++)
            if (r.GetValueOrDefault($"F_ART{i}_TEXT") is { } t && Useful(t)) types.Add(t.Trim());
        if (types.Count > 0) d["Fault type"] = string.Join("; ", types.Distinct());

        var envCount = int.TryParse(r.GetValueOrDefault("F_UW_ANZ"), out var ec) ? ec : 0;
        for (var i = 1; i <= Math.Min(envCount, 12); i++)
        {
            var name = r.GetValueOrDefault($"F_UW{i}_TEXT");
            var value = r.GetValueOrDefault($"F_UW{i}_WERT");
            if (!Useful(name) || !Useful(value)) continue;
            var unit = r.GetValueOrDefault($"F_UW{i}_EINH");
            d[name!.Trim()] = Useful(unit) && unit!.Trim() != "1" ? $"{value!.Trim()} {unit.Trim()}" : value!.Trim();
        }
        return d;
    }

    public Task ClearFaultsAsync(EcuDefinition ecu, CancellationToken ct = default) =>
        RunJobAsync(SgbdFor(ecu), "FS_LOESCHEN", "", ct);

    public IReadOnlyList<LiveParameter> GetLiveParameters(EcuDefinition ecu)
    {
        var names = new HashSet<string>(ecu.Sgbd, StringComparer.OrdinalIgnoreCase);
        if (_resolved.TryGetValue(ecu.Id, out var r)) names.Add(r);
        if (_variants.TryGetValue(ecu.Id, out var v)) names.Add(v);
        return options.LiveFavorites.Where(p => Parse(p) is { } loc && names.Contains(loc.Sgbd)).ToList();
    }

    public async Task<IReadOnlyList<LiveValue>> ReadLiveAsync(EcuDefinition ecu, IReadOnlyList<LiveParameter> parameters, CancellationToken ct = default)
    {
        var values = new List<LiveValue>();
        // Group by job so each job runs once even if several results are shown.
        foreach (var group in parameters.Select(p => (p, loc: Parse(p))).Where(x => x.loc is not null).GroupBy(x => (x.loc!.Value.Sgbd, x.loc.Value.Job, x.loc.Value.Args)))
        {
            Dictionary<string, string> results;
            try { results = Merge(await RunJobAsync(group.Key.Sgbd, group.Key.Job, group.Key.Args, ct)); }
            catch (DiagnosticException e)
            {
                values.AddRange(group.Select(x => new LiveValue(x.p, null, e.Message, DateTimeOffset.Now)));
                continue;
            }
            foreach (var (p, loc) in group)
            {
                results.TryGetValue(loc!.Value.Result, out var text);
                double? number = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
                values.Add(new LiveValue(p, number, text ?? "no result", DateTimeOffset.Now));
            }
        }
        return values;
    }

    // ------------------------------------------------------------------ IModuleReadings

    public string? VariantOf(EcuDefinition ecu) =>
        _variants.TryGetValue(ecu.Id, out var v) ? v.ToUpperInvariant() : null;

    private string ReadingSgbd(EcuDefinition ecu) => _variants.TryGetValue(ecu.Id, out var v) ? v : SgbdFor(ecu);

    /// <summary>Every STATUS_ job of the module's variant that runs without arguments, with BMW's description.</summary>
    public async Task<IReadOnlyList<ModuleReading>> ListReadingsAsync(EcuDefinition ecu, CancellationToken ct = default)
    {
        var sgbd = ReadingSgbd(ecu);
        lock (_readingCache)
            if (_readingCache.TryGetValue(sgbd, out var cached)) return cached;

        var list = new List<ModuleReading>();
        foreach (var job in await ListJobsAsync(sgbd, ct))
        {
            if (!job.StartsWith("STATUS_", StringComparison.OrdinalIgnoreCase)) continue;
            // The virtual jobs below only read the SGBD file; nothing is sent to the car.
            var args = await RunJobAsync(sgbd, "_ARGUMENTS", job, ct);
            if (args.Any(set => set.ContainsKey("ARG"))) continue;
            var comments = await RunJobAsync(sgbd, "_JOBCOMMENTS", job, ct);
            var comment = comments.SelectMany(set => set).Where(kv => kv.Key.StartsWith("JOBCOMMENT", StringComparison.OrdinalIgnoreCase))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            list.Add(new ModuleReading(job, comment));
        }
        lock (_readingCache) _readingCache[sgbd] = list;
        return list;
    }

    /// <summary>Runs one reading job and returns its values, pairing each STAT_x_WERT with its STAT_x_EINH unit.</summary>
    public async Task<IReadOnlyList<ReadingValue>> ReadAsync(EcuDefinition ecu, string job, CancellationToken ct = default)
    {
        var sgbd = ReadingSgbd(ecu);
        var comments = await ResultCommentsAsync(sgbd, job, ct);
        var sets = await RunJobAsync(sgbd, job, "", ct);
        var values = new List<ReadingValue>();
        foreach (var set in sets)
        {
            foreach (var (name, text) in set)
            {
                if (IsSystemResult(name) || ResultParts.Match(name) is { Success: true } e && e.Groups[2].Value.Equals("EINH", StringComparison.OrdinalIgnoreCase)) continue;
                // STAT_x_WERT, STAT_x_WERT1 ... pair with STAT_x_EINH1 or the shared STAT_x_EINH.
                var m = ResultParts.Match(name);
                var stem = m.Success ? m.Groups[1].Value : name;
                var index = m.Success ? m.Groups[3].Value : "";
                var unit = set.GetValueOrDefault(stem + "_EINH" + index) ?? set.GetValueOrDefault(stem + "_EINH");
                // A _TEXT result next to a _WERT one only describes it in words; keep it when it's the only one.
                if (m.Success && m.Groups[2].Value.Equals("TEXT", StringComparison.OrdinalIgnoreCase) && set.ContainsKey(stem + "_WERT" + index)) continue;
                double? number = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
                values.Add(new ReadingValue(name, text, number, string.IsNullOrWhiteSpace(unit) ? null : unit.Trim(), comments.GetValueOrDefault(name)));
            }
        }
        return values;
    }

    public string PinSource(EcuDefinition ecu, string job, string result) => $"ediabas:{ReadingSgbd(ecu)}/{job}/{result}";

    private async Task<Dictionary<string, string>> ResultCommentsAsync(string sgbd, string job, CancellationToken ct)
    {
        var key = sgbd + "/" + job;
        lock (_resultComments)
            if (_resultComments.TryGetValue(key, out var cached)) return cached;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var set in await RunJobAsync(sgbd, "_RESULTS", job, ct))
            {
                if (!set.TryGetValue("RESULT", out var result)) continue;
                var comment = set.Where(kv => kv.Key.StartsWith("RESULTCOMMENT", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                if (comment is not null) map[result] = comment;
            }
        }
        catch (DiagnosticException) { }   // comments are a nicety; readings work without them
        lock (_resultComments) _resultComments[key] = map;
        return map;
    }

    private static readonly System.Text.RegularExpressions.Regex ResultParts =
        new(@"^(.*)_(WERT|TEXT|EINH)(\d*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static bool IsSystemResult(string name) =>
        name.Equals("JOB_STATUS", StringComparison.OrdinalIgnoreCase) || name.Equals("VARIANTE", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("_", StringComparison.Ordinal) || name.Equals("SAETZE", StringComparison.OrdinalIgnoreCase)
        || name.Equals("JOBNAME", StringComparison.OrdinalIgnoreCase) || name.Equals("OBJECT", StringComparison.OrdinalIgnoreCase)
        || name.Equals("UBATTCURRENT", StringComparison.OrdinalIgnoreCase) || name.Equals("IGNITIONCURRENT", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("ID_SGBD", StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ IJobRunner

    public async Task<IReadOnlyList<string>> ListJobsAsync(string sgbd, CancellationToken ct = default)
    {
        var sets = await RunJobAsync(sgbd, "_JOBS", "", ct);
        return sets.Select(s => s.GetValueOrDefault("JOBNAME")).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> RunJobAsync(string sgbd, string job, string arguments, CancellationToken ct = default) =>
        Locked<IReadOnlyList<IReadOnlyDictionary<string, string>>>(() => RunJobCore(sgbd, job, arguments, ct), ct);

    /// <summary>True if the currently resolved SGBD has this job.</summary>
    private bool JobExists(string job)
    {
        try { return _ediabas?.IsJobExisting(job) == true; }
        catch (Exception) { return false; }
    }

    /// <summary>Runs one job. Caller must hold the lock.</summary>
    private IReadOnlyList<IReadOnlyDictionary<string, string>> RunJobCore(string sgbd, string job, string arguments, CancellationToken ct)
    {
            var ediabas = _ediabas ?? throw new DiagnosticException("Not connected");
            _jobToken = ct;
            try
            {
                options.Log?.Note($"Job {sgbd} {job} {arguments}".TrimEnd());
                ediabas.ResolveSgbdFile(sgbd);
                ediabas.ArgString = arguments ?? "";
                ediabas.ArgBinaryStd = null;
                ediabas.ResultsRequests = "";
                ediabas.ExecuteJob(job);

                // Set 0 holds system results (VARIANTE, JOBNAME, SAETZE); job results follow.
                var sets = ediabas.ResultSets ?? [];
                var converted = sets.Select((set, index) =>
                    (IReadOnlyDictionary<string, string>)set.ToDictionary(kv => kv.Key, kv => Format(kv.Value), StringComparer.OrdinalIgnoreCase)).ToList();

                var status = converted.Skip(1).Select(s => s.GetValueOrDefault("JOB_STATUS")).LastOrDefault(s => s is not null);
                if (status is not null && !status.Equals("OKAY", StringComparison.OrdinalIgnoreCase) && job != "_JOBS")
                    throw new DiagnosticException($"{sgbd} {job}: {status}");

                // Keep VARIANTE from the system set, return the job's own sets.
                var system = converted.Count > 0 ? converted[0] : new Dictionary<string, string>();
                var result = converted.Skip(1).ToList();
                if (system.TryGetValue("VARIANTE", out var variant))
                    result.Insert(0, new Dictionary<string, string> { ["VARIANTE"] = variant });
                return result;
            }
            catch (DiagnosticException) { throw; }
            catch (Exception e)
            {
                ct.ThrowIfCancellationRequested();   // an aborted job is a cancellation, not an error
                var text = EdiabasNet.GetExceptionText(e, false, false);
                options.Log?.Note($"  -> {text}");
                throw new DiagnosticException($"{sgbd} {job}: {text}", e);
            }
    }

    public async ValueTask DisposeAsync()
    {
        // Wait for a running job to finish so it never touches a disposed EdiabasNet.
        _jobToken = new CancellationToken(canceled: true);
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _ediabas?.Dispose();
            _ediabas = null;
        }
        finally { _lock.Release(); }
    }

    // ------------------------------------------------------------------ helpers

    private string SgbdFor(EcuDefinition ecu) =>
        _variants.TryGetValue(ecu.Id, out var v) ? v
        : _resolved.TryGetValue(ecu.Id, out var s) ? s
        : ecu.Sgbd.FirstOrDefault(SgbdExists) ?? $"D_{ecu.Address:X4}";

    private bool SgbdExists(string name) =>
        File.Exists(Path.Combine(options.EcuPath, name + ".prg")) || File.Exists(Path.Combine(options.EcuPath, name + ".grp"));

    private static bool IsNoResponse(string message) =>
        message.Contains("IFH-0009", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("NO RESPONSE", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> Merge(IEnumerable<IReadOnlyDictionary<string, string>> sets)
    {
        var all = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in sets)
            foreach (var kv in set)
                all.TryAdd(kv.Key, kv.Value);
        return all;
    }

    private static string? Get(Dictionary<string, string> d, params string[] keys) =>
        keys.Select(k => d.GetValueOrDefault(k)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static string Format(EdiabasNet.ResultData data) => data.OpData switch
    {
        null => "",
        string s => s,
        byte[] bytes => Convert.ToHexString(bytes),
        double d => d.ToString("0.###", CultureInfo.InvariantCulture),
        float f => f.ToString("0.###", CultureInfo.InvariantCulture),
        IFormattable n => n.ToString(null, CultureInfo.InvariantCulture),
        var other => other.ToString() ?? "",
    };

    private readonly record struct JobLocator(string Sgbd, string Job, string Result, string Args);

    private static JobLocator? Parse(LiveParameter p)
    {
        if (!p.Source.StartsWith("ediabas:", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = p.Source["ediabas:".Length..].Split('/', 4);
        return parts.Length >= 3 ? new JobLocator(parts[0], parts[1], parts[2], parts.Length == 4 ? parts[3] : "") : null;
    }

    private async Task<T> Locked<T>(Func<T> work, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try { return await Task.Run(work, ct).ConfigureAwait(false); }
        finally { _lock.Release(); }
    }

    private Task Locked(Func<bool> work, CancellationToken ct) => Locked<bool>(work, ct);
}
