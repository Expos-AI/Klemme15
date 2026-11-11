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
        Journal = new OperationJournal(Path.Combine(Path.GetDirectoryName(historyFolder ?? DefaultHistoryFolder)!, "running-job.json"));
        _interrupted = Journal.CutOff();   // a job still recorded as running at start-up was cut off
    }

    // ---------------------------------------------------------------- jobs that change the car

    /// <summary>Remembers a job that changes the car while it runs, so a cut-off is noticed even after a reboot.</summary>
    public OperationJournal Journal { get; }
    private RunningOperation? _interrupted;

    /// <summary>A job that was cut off (now, or the last time Klemme15 ran), until the user dismisses it.</summary>
    public RunningOperation? Interrupted
    {
        get => _interrupted;
        private set { if (Set(ref _interrupted, value)) { Raise(nameof(HasInterrupted)); Raise(nameof(InterruptedAdvice)); Raise(nameof(InterruptedHeadline)); } }
    }

    public bool HasInterrupted => _interrupted is not null;
    public string InterruptedAdvice => _interrupted is { } op ? OperationJournal.Advice(op) : "";
    public string InterruptedHeadline => _interrupted is { } op ? OperationJournal.Headline(op) : "";

    /// <summary>Starts a job that changes the car: written to disk, and the computer kept awake, until it's ended.</summary>
    public IDisposable BeginOperation(RunningOperation op)
    {
        if (!_isDemo) Journal.Begin(op);
        OnUiThread(() => { _jobs++; Raise(nameof(JobRunning)); });
        var done = new Release(() => OnUiThread(() => { _jobs = Math.Max(0, _jobs - 1); Raise(nameof(JobRunning)); }));
        return new Both(new Both(Klemme15.App.Services.SleepGuard.Hold(op.Title), ClaimBus(op.Title)), done);
    }

    private int _jobs;

    /// <summary>
    /// A job that changes or drives something in the car is running (service function, coding write, a test that drives
    /// a part). Disconnecting or switching cars now would cut it off, so the Garage waits.
    /// </summary>
    public bool JobRunning => _jobs > 0;

    // ---------------------------------------------------------------- who is using the cable

    private string? _busyWith;

    /// <summary>
    /// What is using the cable right now (a test, a service function, a coding write, live data), so the Ghost Hunter
    /// doesn't start a second conversation with the car alongside it. Null when nothing is.
    /// </summary>
    /// <summary>Redraws everything built from the last quick test (after the display units change).</summary>
    public void RefreshViews() => ScanUpdated?.Invoke();

    public string? BusyWith { get => _busyWith; private set => Set(ref _busyWith, value); }

    public IDisposable ClaimBus(string what)
    {
        OnUiThread(() => BusyWith = what);
        return new Release(() => OnUiThread(() => { if (BusyWith == what) BusyWith = null; }));
    }

    private static void OnUiThread(Action a)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) a(); else Avalonia.Threading.Dispatcher.UIThread.Post(a);
    }

    private sealed class Release(Action release) : IDisposable
    {
        private int _done;
        public void Dispose() { if (Interlocked.Exchange(ref _done, 1) == 0) release(); }
    }

    private sealed class Both(IDisposable a, IDisposable b) : IDisposable
    {
        public void Dispose() { a.Dispose(); b.Dispose(); }
    }

    /// <summary>The job ended normally (finished, failed with an answer from the car, or stopped by the user).</summary>
    public void EndOperation() => Journal.End();

    /// <summary>The job was cut off: the cable or the car stopped answering. Keeps the journal and shows what to do.</summary>
    public void OperationCutOff() => Interrupted = Journal.CutOff();

    public void DismissInterrupted()
    {
        Journal.Dismiss();
        Interrupted = null;
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
        if (_isDemo || IsSnapshot || HealthSnapshot.From(scan, _model?.Id, _model?.Name, engine) is not { } snapshot) return null;
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
                : HealthSnapshot.CarKeyFor(scan) is { } key ? History.Load(key) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { history = []; }
        IReadOnlyList<TestRecord> tests = [];
        try { if (!_isDemo && HealthSnapshot.CarKeyFor(scan) is { } k) tests = History.LoadTests(k); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var catalog = Database.CheckControl;
        return scan with { Health = HealthSummary.From(history, id => CheckControlDescriber.Describe(id, catalog, active: true).Title, madeUp: _isDemo, tests: tests) };
    }

    /// <summary>Saves a finished guided test to the car's health history (real cars with a quick test only), then refreshes the trends.</summary>
    public bool RecordTest(GuidedTestInfo test, TestReport report)
    {
        if (report.Verdict != TestSeverity.Unknown || report.Traces.Count > 0)
        {
            TestResults.Add(new SnapshotTest(test.Id, test.Title, DateTimeOffset.Now, report));
            if (TestResults.Count > 40) TestResults.RemoveAt(0);
        }
        if (_isDemo || _lastScan is null || HealthSnapshot.CarKeyFor(_lastScan) is not { } key) return false;
        try { History.AddTest(key, TestRecord.From(test, report, DateTimeOffset.Now)); }
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

    private CarSnapshot? _snapshot;

    /// <summary>The snapshot being viewed, when one is open instead of a car.</summary>
    public CarSnapshot? Snapshot { get => _snapshot; private set { if (Set(ref _snapshot, value)) { Raise(nameof(IsSnapshot)); Raise(nameof(SnapshotBanner)); } } }
    public bool IsSnapshot => _snapshot is not null;

    public string SnapshotBanner => _snapshot is { } s
        ? $"Viewing a snapshot from {s.Manifest.Created.LocalDateTime:d MMM yyyy HH:mm} · {s.Manifest.ModelName ?? s.Manifest.Chassis ?? "BMW"} · nothing is connected, nothing can be changed"
        : "";

    private bool _isWatching;

    /// <summary>The Ghost Hunter is reading the car in the background: other work waits, because there's one diagnostic bus.</summary>
    public bool IsWatching { get => _isWatching; set => Set(ref _isWatching, value); }

    /// <summary>The last minute of live data (filled while live data or a test runs), for snapshots.</summary>
    public LiveBuffer LiveBuffer { get; } = new();

    /// <summary>Guided tests finished in this session, for snapshots.</summary>
    public List<SnapshotTest> TestResults { get; } = [];

    /// <summary>The latest coding read per comfort module in this session, for snapshots (view only).</summary>
    public Dictionary<string, SnapshotCoding> CodingReads { get; } = [];

    /// <summary>Raised after a snapshot was opened, so pages can load what it carries (the Analyse page its live data).</summary>
    public event Action<CarSnapshot>? SnapshotOpened;

    /// <summary>Opens a snapshot in place of a car: every page shows the car as it was; nothing can be written.</summary>
    public async Task OpenSnapshotAsync(CarSnapshot snapshot)
    {
        await DisconnectAsync();
        var family = (snapshot.Manifest.FamilyId is { } f ? Database.FindVehicle(f) : null) ?? Database.Vehicles.FirstOrDefault()
                     ?? throw new DiagnosticException("No vehicle families are loaded.");
        // Modules the snapshot knows but this version's database doesn't still show, with the snapshot's own definition.
        Vehicle = family;
        Model = family.Models.FirstOrDefault(m => m.Id == snapshot.Manifest.ModelId);
        IsDemo = false;
        Snapshot = snapshot;
        Backend = new SnapshotBackend(snapshot);
        Connection = $"Snapshot {snapshot.Manifest.Created.LocalDateTime:d MMM yyyy}";
        Status = SnapshotBanner;
        PublishScan(snapshot.Scan);
        SnapshotOpened?.Invoke(snapshot);
    }

    /// <summary>Builds a snapshot of the car now: the last quick test, live data, tests and (if asked) coding and the traffic log.</summary>
    public CarSnapshot BuildSnapshot(string? note, bool includeLive, bool includeTests, bool includeCoding, bool includeLog, string appVersion)
    {
        var scan = _lastScan ?? throw new DiagnosticException("Run a quick test first: the snapshot is built around it.");
        return new CarSnapshot
        {
            Manifest = new SnapshotManifest
            {
                App = appVersion,
                Created = DateTimeOffset.Now,
                FamilyId = _vehicle?.Id,
                ModelId = _model?.Id,
                ModelName = _model?.Name,
                Chassis = scan.Chassis ?? _vehicle?.Id,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            },
            Scan = scan,
            LiveCsv = includeLive ? LiveBuffer.ToCsv() : "",
            Tests = includeTests ? TestResults.TakeLast(20).ToList() : [],
            Coding = includeCoding ? CodingReads.Values.ToList() : [],
            TrafficLog = includeLog ? Log.Export() : null,
        };
    }
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
    /// <summary>
    /// A K-line car (E46, E39, E53, E38, E85, E83, E36) on the native connection: the engine computer gives the standard OBD-II values
    /// only. BMW's own values (VANOS, per-cylinder roughness, module readings) need EDIABAS mode with your BMW files.
    /// </summary>
    public bool ObdValuesOnly => !IsDemo && Vehicle?.Cable == "KLine" && Backend is not IMeasurementReader;

    public IMeasurementReader? Measurements => _backend as IMeasurementReader ?? Readings as IMeasurementReader;

    /// <summary>Engine values (rpm, coolant, voltage...) from the connected car, or null when there's no engine computer.</summary>
    public ISignalSource? EngineSignalSource()
    {
        if (Backend is not { } backend || Vehicle?.Modules.FirstOrDefault(m => m.IsEngine) is not { } engine) return null;
        return IsDemo && Measurements is { } demo
            ? new Klemme15.Protocols.Simulation.DemoSignals(demo, engine)
            : new EngineSignals(backend, engine, Measurements);
    }

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
        if (backend is IConnectionWatch watch)
            watch.ConnectionLost += message => OnUiThread(async () =>
            {
                if (!ReferenceEquals(_backend, backend)) return;   // already disconnected, or another car by now
                Log.Note("The cable stopped responding; disconnecting");
                await DisconnectAsync();
                Status = "The cable stopped responding, so Klemme15 disconnected. Check the plug at both ends, then connect again in the Garage.";
            });
        // The engine computer's part number pins down its exact version (MSV70 vs MSV80 on an N52), which the
        // readings and guided tests need before any quick test has run.
        _engineIdentity = null;
        if (!settings.Demo && vehicle.Modules.FirstOrDefault(m => m.IsEngine) is { } engine)
        {
            try { _engineIdentity = await backend.IdentifyAsync(engine, ct); }
            catch (Exception e) when (e is DiagnosticException or TimeoutException) { }
        }
        // A test that drove the water pump was cut off: send the switch-off again, once, and only to the same car model.
        if (!settings.Demo && _interrupted is { StopAction: "pump" } cut && cut.Car == $"{vehicle.Id} {model?.Name}".Trim()
            && vehicle.Modules.FirstOrDefault(m => m.IsEngine) is { } eng && Klemme15.App.Services.JobPumpActuator.For(this, eng) is { } pump)
        {
            try
            {
                await pump.StopAsync(ct);
                Log.Note("Recovery: sent the water pump switch-off again after an interrupted test");
                var done = cut with { StopAction = null };
                Journal.Begin(done);
                Interrupted = done;
            }
            catch (Exception e) when (e is DiagnosticException or TimeoutException or NotSupportedException or OperationCanceledException) { }
        }
        Connection = settings.Demo ? "Demo car" : Klemme15.Protocols.Transport.NetworkTransport.IsNetworkAddress(settings.Port) ? $"Network cable {settings.Port![(settings.Port!.IndexOf("://") + 3)..]}" : $"Cable on {settings.Port}";
        Status = settings.Demo ? $"{vehicle.Id} {model?.Name} demo car: nothing is sent to a real car".Replace("  ", " ")
            : $"{vehicle.Id} {model?.Name} via {backend.Name}".Replace("  ", " ");
    }

    public async Task DisconnectAsync()
    {
        var b = _backend;
        var wasCar = b is not null && !IsDemo && !IsSnapshot;
        Snapshot = null;
        LiveBuffer.Clear();
        TestResults.Clear();
        CodingReads.Clear();
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
