using OpenHood.Core;
using OpenHood.Data;

namespace OpenHood.App.ViewModels;

/// <summary>The one live connection to the car, shared by every page.</summary>
public sealed class SessionState : ViewModelBase
{
    private IDiagnosticBackend? _backend;
    private VehicleFamily? _vehicle;
    private string _status = "Not connected";
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
    public bool IsConnected => _backend is not null;
    public bool IsDemo { get => _isDemo; private set => Set(ref _isDemo, value); }
    public bool SupportsJobs => _backend is IJobRunner;
    public string Status { get => _status; set => Set(ref _status, value); }

    /// <summary>Latest quick test, shared with the fault and report pages.</summary>
    public VehicleScanResult? LastScan { get => _lastScan; set => Set(ref _lastScan, value); }

    public event Action? ScanUpdated;

    public void PublishScan(VehicleScanResult scan)
    {
        LastScan = scan;
        ScanUpdated?.Invoke();
    }

    public async Task ConnectAsync(ConnectionSettings settings, VehicleFamily vehicle, CancellationToken ct)
    {
        await DisconnectAsync();
        Status = "Connecting...";
        var backend = BackendFactory.Create(settings, vehicle, Log);
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
        IsDemo = settings.Demo;
        Backend = backend;
        Status = $"{vehicle.Id} via {backend.Name}{(settings.Demo ? " (demo car)" : "")}";
    }

    public async Task DisconnectAsync()
    {
        var b = _backend;
        Backend = null;
        LastScan = null;
        ScanUpdated?.Invoke();
        if (b is not null)
        {
            try { await b.DisposeAsync(); } catch (Exception) { /* cable may already be gone */ }
        }
        Status = "Not connected";
    }
}
