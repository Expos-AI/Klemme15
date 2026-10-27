using System.Collections.ObjectModel;
using Avalonia.Media;
using OpenHood.App.Services;
using OpenHood.Core;
using OpenHood.Data;
using OpenHood.Serial;

namespace OpenHood.App.ViewModels;

public sealed class ConnectViewModel : PageViewModel
{
    private readonly SessionState _session;
    private readonly IDialogService _dialogs;
    private readonly AppSettings _settings;
    private readonly Action _onConnected;
    private VehicleFamily? _vehicle;
    private PortInfo? _port;
    private bool _useDemo;
    private bool _useEdiabas;
    private string? _ediabasPath;
    private bool _trustDsr;
    private string? _error;
    private CancellationTokenSource? _cts;

    public ConnectViewModel(SessionState session, IDialogService dialogs, AppSettings settings, Action onConnected)
    {
        _session = session;
        _dialogs = dialogs;
        _settings = settings;
        _onConnected = onConnected;
        Vehicles = session.Database.Vehicles;
        _vehicle = session.Database.FindVehicle(settings.VehicleId) ?? Vehicles.FirstOrDefault();
        _useDemo = settings.UseDemo;
        _useEdiabas = settings.UseEdiabas && EdiabasAvailable;
        _ediabasPath = settings.EdiabasPath;
        _trustDsr = settings.TrustDsr;

        ConnectCommand = new AsyncCommand(ConnectAsync, () => !_session.IsConnected && _vehicle is not null, e => Error = e.Message);
        DisconnectCommand = new AsyncCommand(_session.DisconnectAsync, () => _session.IsConnected, e => Error = e.Message);
        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        BrowseEdiabasCommand = new AsyncCommand(BrowseEdiabasAsync);
        CancelCommand = new RelayCommand(() => _cts?.Cancel());

        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionState.IsConnected) or nameof(SessionState.Backend))
            {
                ConnectCommand.RaiseCanExecuteChanged();
                DisconnectCommand.RaiseCanExecuteChanged();
                Raise(nameof(IsConnected));
                Raise(nameof(Ignition));
                Raise(nameof(IgnitionBrush));
            }
        };
        RefreshPorts();
    }

    public override string Title => "Connect";
    public override Geometry Icon => Icons.Connect;

    public IReadOnlyList<VehicleFamily> Vehicles { get; }
    public ObservableCollection<PortInfo> Ports { get; } = [];
    public SessionState Session => _session;
    public bool IsConnected => _session.IsConnected;
    public bool EdiabasAvailable => BackendFactory.EdiabasAvailable;
    public string EdiabasNote => EdiabasAvailable
        ? "Uses the SGBD files from your own EDIABAS/INPA install for full module coverage and BMW fault texts."
        : "EDIABAS mode is part of the Windows build. This build uses OpenHood's own protocol stack.";

    public VehicleFamily? Vehicle
    {
        get => _vehicle;
        set
        {
            if (!Set(ref _vehicle, value)) return;
            Raise(nameof(CableHint));
            ConnectCommand.RaiseCanExecuteChanged();
        }
    }

    public PortInfo? Port { get => _port; set => Set(ref _port, value); }
    public bool UseDemo { get => _useDemo; set => Set(ref _useDemo, value); }
    public bool UseEdiabas { get => _useEdiabas; set => Set(ref _useEdiabas, value); }
    public string? EdiabasPath { get => _ediabasPath; set => Set(ref _ediabasPath, value); }
    public bool TrustDsr { get => _trustDsr; set => Set(ref _trustDsr, value); }

    public string? Error
    {
        get => _error;
        set { if (Set(ref _error, value)) Raise(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);

    public string CableHint => _vehicle?.Cable.Equals("KLine", StringComparison.OrdinalIgnoreCase) == true
        ? "Set the cable's switch to K-line (pins 7+8 bridged). Ignition on (key position 2)."
        : "Set the cable's switch to D-CAN. Ignition on (key position 2), or engine running for live data.";

    public string Ignition => _session.Backend?.IgnitionOn switch
    {
        true => "Ignition on",
        false => "Ignition off",
        null => _session.IsConnected ? "Ignition: not reported by cable" : "",
    };

    public IBrush IgnitionBrush => _session.Backend?.IgnitionOn == false ? Brushes.IndianRed : Brushes.SeaGreen;

    public AsyncCommand ConnectCommand { get; }
    public AsyncCommand DisconnectCommand { get; }
    public RelayCommand RefreshPortsCommand { get; }
    public AsyncCommand BrowseEdiabasCommand { get; }
    public RelayCommand CancelCommand { get; }

    private void RefreshPorts()
    {
        Ports.Clear();
        try
        {
            foreach (var p in PortDiscovery.List()) Ports.Add(p);
        }
        catch (Exception e)
        {
            Error = $"Could not list serial ports: {e.Message}";
        }
        Port = Ports.FirstOrDefault(p => p.Name == _settings.Port) ?? Ports.FirstOrDefault(p => p.LikelyKDCan) ?? Ports.FirstOrDefault();
    }

    private async Task BrowseEdiabasAsync()
    {
        var folder = await _dialogs.PickFolderAsync("Choose your EDIABAS Ecu folder (contains .prg files)");
        if (folder is not null) EdiabasPath = folder;
    }

    private async Task ConnectAsync()
    {
        Error = null;
        if (_vehicle is null) return;
        if (!_useDemo && _port is null)
        {
            Error = "No cable port selected. Plug in the K+DCAN cable and press Refresh, or tick Demo car.";
            return;
        }
        var settings = new ConnectionSettings
        {
            VehicleId = _vehicle.Id,
            Port = _port?.Name,
            Demo = _useDemo,
            EdiabasPath = _useEdiabas ? _ediabasPath : null,
            TrustDsr = _trustDsr,
            LiveFavorites = _settings.LiveFavorites.Select(f => f.ToParameter()).ToList(),
        };

        _cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await _session.ConnectAsync(settings, _vehicle, _cts.Token);
        }
        catch (DiagnosticException e)
        {
            Error = e.Message;
            return;
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }

        _settings.VehicleId = _vehicle.Id;
        _settings.Port = _port?.Name;
        _settings.UseDemo = _useDemo;
        _settings.UseEdiabas = _useEdiabas;
        _settings.EdiabasPath = _ediabasPath;
        _settings.TrustDsr = _trustDsr;
        _settings.Save();
        _onConnected();
    }
}
