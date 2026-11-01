using Klemme15.Core;
using Klemme15.Data;

namespace Klemme15.App.ViewModels;

/// <summary>The one live connection to the car, shared by every page.</summary>
public sealed class SessionState : ViewModelBase
{
    private IDiagnosticBackend? _backend;
    private VehicleFamily? _vehicle;
    private VehicleModel? _model;
    private string _status = "Not connected";
    private string _connection = "Not connected";
    private VehicleScanResult? _lastScan;
    private bool _isDemo;

    public SessionState(VehicleDatabase database, string? historyFolder = null)
    {
        Database = database;
        History = new HealthHistoryStore(historyFolder ?? DefaultHistoryFolder);
    }

    public static string DefaultHistoryFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Klemme15", "history");

    public VehicleDatabase Database { get; }

    /// <summary>Health snapshots of every real car scanned on this computer (never uploaded anywhere).</summary>
    public HealthHistoryStore History { get; }

    /// <summary>Raised after a snapshot is saved or a car is forgotten.</summary>
    public event Action? HistoryChanged;

    /// <summary>Saves a snapshot of a finished quick test on a real car. Demo cars and scans without a VIN are skipped.</summary>
    public HealthSnapshot? RecordHealth(VehicleScanResult scan, IReadOnlyDictionary<string, double> engine)
    {
        if (_isDemo || HealthSnapshot.From(scan, _model?.Id, _model?.Name, engine) is not { } snapshot) return null;
        try { History.Add(snapshot); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        HistoryChanged?.Invoke();
        return snapshot;
    }

    /// <summary>Attaches what the health history says about this car (the demo car gets a made-up history).</summary>
    public VehicleScanResult WithHealth(VehicleScanResult scan)
    {
        IReadOnlyList<HealthSnapshot> history;
        try
        {
            history = _isDemo ? Klemme15.App.Services.DemoHistory.For(scan)
                : scan.Vin is { Length: 17 } vin ? History.Load(HealthSnapshot.KeyFor(vin)) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { history = []; }
        IReadOnlyList<TestRecord> tests = [];
        try { if (!_isDemo && scan.Vin is { Length: 17 } v) tests = History.LoadTests(HealthSnapshot.KeyFor(v)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var catalog = Database.CheckControl;
        return scan with { Health = HealthSummary.From(history, id => CheckControlDescriber.Describe(id, catalog, active: true).Title, madeUp: _isDemo, tests: tests) };
    }

    /// <summary>Saves a finished guided test to the car's health history (real cars with a quick test only), then refreshes the trends.</summary>
    public bool RecordTest(GuidedTestInfo test, TestReport report)
    {
        if (_isDemo || _lastScan?.Vin is not { Length: 17 } vin) return false;
        try { History.AddTest(HealthSnapshot.KeyFor(vin), TestRecord.From(test, report, DateTimeOffset.Now)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        PublishScan(WithHealth(_lastScan));
        HistoryChanged?.Invoke();
        return true;
    }

    public void ForgetCar(string carKey)
    {
        try { History.Forget(carKey); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        HistoryChanged?.Invoke();
    }
    public TrafficLog Log { get; } = new();

    public IDiagnosticBackend? Backend
    {
        get => _backend;
        private set
        {
            if (Set(ref _backend, value))
            {
                Raise(nameof(IsConnected));
                Raise(nameof(SupportsJobs));
                _demoReadings = null;
                Raise(nameof(Readings));
                Raise(nameof(Measurements));
            }
        }
    }

    public VehicleFamily? Vehicle { get => _vehicle; private set => Set(ref _vehicle, value); }
    public VehicleModel? Model { get => _model; private set => Set(ref _model, value); }
    public bool IsConnected => _backend is not null;
    public bool IsDemo { get => _isDemo; private set => Set(ref _isDemo, value); }
    public bool SupportsJobs => _backend is IJobRunner;

    /// <summary>Module readings (BMW's status jobs) when the connection can run them, or the demo's made-up ones.</summary>
    public IModuleReadings? Readings =>
        _backend as IModuleReadings
        ?? (_isDemo && _backend is not null ? _demoReadings ??= new Klemme15.Protocols.Simulation.DemoReadings(_model)
        : _backend is IRawRequests raw && Database.Measurements is { } tables
            ? _demoReadings ??= new Klemme15.Protocols.NativeReadings(raw, tables, VariantForReadings, Log)
            : null);
    private IModuleReadings? _demoReadings;
    private EcuIdentity? _engineIdentity;

    /// <summary>BMW measurement values by name (for guided tests): from the user's files, the demo, or the downloaded tables.</summary>
    public IMeasurementReader? Measurements => _backend as IMeasurementReader ?? Readings as IMeasurementReader;

    /// <summary>The module version to read native values for: from its part number when the quick test read it, else the engine.</summary>
    private string? VariantForReadings(EcuDefinition ecu)
    {
        var identity = _lastScan?.Ecus.FirstOrDefault(e => e.Ecu.Id == ecu.Id)?.Identity
                       ?? (ecu.IsEngine ? _engineIdentity : null);
        return Klemme15.Data.OfficialLookup.ResolveVariant(Database.OfficialSources, ecu, identity, _model?.EngineCode,
            v => Database.Measurements?.Has(v) == true);
    }

    /// <summary>
    /// The module version a service function would run on: the name the module reported (EDIABAS mode), else the
    /// most likely version from its part number and the engine, limited to versions the function knows.
    /// </summary>
    public string? ServiceVariant(EcuDefinition ecu, ServiceFunction function)
    {
        if (_backend is IModuleReadings readings && _backend is IJobRunner && readings.VariantOf(ecu) is { } reported) return reported;
        var identity = _lastScan?.Ecus.FirstOrDefault(e => e.Ecu.Id == ecu.Id)?.Identity;
        if (identity?.Variant is { } v) return v;
        return OfficialLookup.ResolveVariant(Database.OfficialSources, ecu, identity, _model?.EngineCode,
            c => function.Implementations.Any(i => i.Variants.Contains(c.ToLowerInvariant())));
    }

    public string Status { get => _status; set => Set(ref _status, value); }

    /// <summary>Short connection label for the header, e.g. "Demo car" or "Cable on COM3".</summary>
    public string Connection { get => _connection; private set => Set(ref _connection, value); }

    private double? _voltage;

    /// <summary>The battery voltage last measured on the car (null until measured, or in demo mode).</summary>
    public double? Voltage
    {
        get => _voltage;
        set { if (Set(ref _voltage, value)) Raise(nameof(VoltageText)); }
    }

    /// <summary>"12.4 V", or empty when unknown.</summary>
    public string VoltageText => _voltage is { } v ? $"{v:0.0} V" : "";

    /// <summary>Fault texts for this session (demo cars add their own made-up BMW codes).</summary>
    public IFaultCodeDatabase Codes => _isDemo && _model is not null ? Database.ForDemo(_model) : Database.ForEngine(_model?.EngineCode);

    /// <summary>Latest quick test, shared with the fault and report pages.</summary>
    public VehicleScanResult? LastScan { get => _lastScan; private set => Set(ref _lastScan, value); }

    public event Action? ScanUpdated;

    public void PublishScan(VehicleScanResult? scan)
    {
        LastScan = scan;
        ScanUpdated?.Invoke();
    }

    public FaultLocation? Locate(Fault fault, EcuDefinition ecu) => Database.Locate(fault, ecu, _isDemo ? _model : null);

    public async Task ConnectAsync(ConnectionSettings settings, VehicleFamily vehicle, VehicleModel? model, CancellationToken ct)
    {
        await DisconnectAsync();
        Status = "Connecting...";
        var backend = BackendFactory.Create(settings, vehicle, Log, model);
        try
        {
            await backend.ConnectAsync(ct);
        }
        catch
        {
            await backend.DisposeAsync();
            Status = "Not connected";
            throw;
        }
        Vehicle = vehicle;
        Model = model;
        IsDemo = settings.Demo;
        Backend = backend;
        // The engine computer's part number pins down its exact version (MSV70 vs MSV80 on an N52), which the
        // readings and guided tests need before any quick test has run.
        _engineIdentity = null;
        if (!settings.Demo && vehicle.Modules.FirstOrDefault(m => m.IsEngine) is { } engine)
        {
            try { _engineIdentity = await backend.IdentifyAsync(engine, ct); }
            catch (Exception e) when (e is DiagnosticException or TimeoutException) { }
        }
        Connection = settings.Demo ? "Demo car" : Klemme15.Protocols.Transport.NetworkTransport.IsNetworkAddress(settings.Port) ? $"Network cable {settings.Port![(settings.Port!.IndexOf("://") + 3)..]}" : $"Cable on {settings.Port}";
        Status = $"{vehicle.Id} {model?.Name} via {backend.Name}".Replace("  ", " ");
    }

    public async Task DisconnectAsync()
    {
        var b = _backend;
        var wasCar = b is not null && !IsDemo;
        Backend = null;
        Voltage = null;
        PublishScan(null);
        Connection = "Not connected";
        if (b is not null)
        {
            try { await b.DisposeAsync(); } catch (Exception) { /* cable may already be gone */ }
        }
        // While a tool is connected the car stays awake and draws 10-20 A with the engine off; a reminder costs nothing.
        Status = wasCar ? "Disconnected. Switch the ignition off (Start/Stop) so the car can go to sleep." : "Not connected";
    }
}
