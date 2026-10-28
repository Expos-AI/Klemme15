using OpenHood.Core;
using OpenHood.Data;

namespace OpenHood.App.ViewModels;

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

    public SessionState(VehicleDatabase database) => Database = database;

    public VehicleDatabase Database { get; }
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
            }
        }
    }

    public VehicleFamily? Vehicle { get => _vehicle; private set => Set(ref _vehicle, value); }
    public VehicleModel? Model { get => _model; private set => Set(ref _model, value); }
    public bool IsConnected => _backend is not null;
    public bool IsDemo { get => _isDemo; private set => Set(ref _isDemo, value); }
    public bool SupportsJobs => _backend is IJobRunner;
    public string Status { get => _status; set => Set(ref _status, value); }

    /// <summary>Short connection label for the header, e.g. "Demo car" or "Cable on COM3".</summary>
    public string Connection { get => _connection; private set => Set(ref _connection, value); }

    /// <summary>Fault texts for this session (demo cars add their own made-up BMW codes).</summary>
    public IFaultCodeDatabase Codes => _isDemo && _model is not null ? Database.ForDemo(_model) : Database;

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
        Connection = settings.Demo ? "Demo car" : $"Cable on {settings.Port}";
        Status = $"{vehicle.Id} {model?.Name} via {backend.Name}".Replace("  ", " ");
    }

    public async Task DisconnectAsync()
    {
        var b = _backend;
        Backend = null;
        PublishScan(null);
        Connection = "Not connected";
        if (b is not null)
        {
            try { await b.DisposeAsync(); } catch (Exception) { /* cable may already be gone */ }
        }
        Status = "Not connected";
    }
}
