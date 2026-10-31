using System.Collections.ObjectModel;
using Avalonia.Media;
using Klemme15.App.Services;
using Klemme15.Core;
using Klemme15.Data;
using Klemme15.Serial;

namespace Klemme15.App.ViewModels;

public sealed class FamilyOption(VehicleFamily family) : ViewModelBase
{
    private bool _isSelected;
    public VehicleFamily Family { get; } = family;
    public string Name => Family.Id == "E9x" ? "E90 / E91 / E92 / E93" : Family.Id;
    public string Years => Family.Years;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public RelayCommand? SelectCommand { get; set; }
}

/// <summary>Start page: choose the car, then open its demo or connect the cable.</summary>
public sealed class GarageViewModel : PageViewModel
{
    private readonly SessionState _session;
    private readonly IDialogService _dialogs;
    private readonly AppSettings _settings;
    private readonly Action _onConnected;
    private FamilyOption _family;
    private VehicleModel? _model;
    private PortInfo? _port;
    private bool _showCable;
    private bool _useEdiabas;
    private string? _ediabasPath;
    private bool _trustDsr;
    private string? _error;
    private bool _busy;
    private string _ediabasStatus = "";
    private string _faultDataStatus = "Checking for official BMW fault texts...";
    private string? _downloadText;
    private bool _downloading;
    private StartupInfo? _startup;

    public GarageViewModel(SessionState session, IDialogService dialogs, AppSettings settings, Action onConnected, StartupInfo? startup = null)
    {
        _startup = startup;
        _session = session;
        _dialogs = dialogs;
        _settings = settings;
        _onConnected = onConnected;

        Families = session.Database.Vehicles.Select(f => new FamilyOption(f)).ToList();
        foreach (var f in Families) f.SelectCommand = new RelayCommand(() => SelectFamily(f));
        _family = Families.FirstOrDefault(f => f.Family.Id == settings.VehicleId) ?? Families[0];
        _family.IsSelected = true;
        _model = _family.Family.Models.FirstOrDefault(m => m.Id == settings.ModelId) ?? _family.Family.Models.FirstOrDefault();
        _useEdiabas = settings.UseEdiabas && EdiabasAvailable;
        _ediabasPath = settings.EdiabasPath;
        _trustDsr = settings.TrustDsr;

        OpenDemoCommand = new AsyncCommand(() => ConnectAsync(demo: true), () => !_busy && _model is not null, e => Error = e.Message);
        ConnectCarCommand = new AsyncCommand(() => ConnectAsync(demo: false), () => !_busy && _model is not null && (_port is not null || UsesNetwork), e => Error = e.Message);
        ShowCableCommand = new RelayCommand(() => ShowCable = true);
        DisconnectCommand = new AsyncCommand(_session.DisconnectAsync, () => _session.IsConnected);
        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        BrowseEdiabasCommand = new AsyncCommand(BrowseEdiabasAsync);
        DownloadFaultDataCommand = new AsyncCommand(DownloadFaultDataAsync, () => !_downloading, e => { _downloading = false; DownloadText = null; FaultDataStatus = $"Download failed: {e.Message}"; DownloadFaultDataCommand?.RaiseCanExecuteChanged(); });
        OpenFaultDataPageCommand = new AsyncCommand(() => _dialogs.OpenUrlAsync(FaultDataDownload.ProjectUrl));
        OpenFaultDataLicenseCommand = new AsyncCommand(() => _dialogs.OpenUrlAsync(FaultDataDownload.LicenseUrl));
        ChooseEcuFolderCommand = new AsyncCommand(ChooseEcuFolderAsync);
        DownloadReadingsCommand = new AsyncCommand(DownloadReadingsAsync, () => !_downloading,
            e => { _downloading = false; DownloadText = null; FaultDataStatus = $"Download failed: {e.Message}"; DownloadReadingsCommand?.RaiseCanExecuteChanged(); DownloadFaultDataCommand.RaiseCanExecuteChanged(); });

        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not nameof(SessionState.Backend)) return;
            DisconnectCommand.RaiseCanExecuteChanged();
            Raise(nameof(IsConnected));
            Raise(nameof(ConnectedText));
        };
        RebuildPreview();
        RefreshPorts();
        if (EdiabasAvailable) _ = FindEdiabasAsync();
        if (startup?.Official is { } official) ShowFaultData(official);
        else _ = RefreshFaultDataAsync();
        _startup = null;   // only for the first look; later refreshes look again
    }

    // ------------------------------------------------------------------ official fault texts

    /// <summary>Which official BMW fault texts this computer has.</summary>
    public string FaultDataStatus { get => _faultDataStatus; private set => Set(ref _faultDataStatus, value); }

    private bool _needsFaultData;

    /// <summary>True until the user has official fault texts (download or own files); shows the prompt at the top.</summary>
    public bool NeedsFaultData { get => _needsFaultData; private set => Set(ref _needsFaultData, value); }

    /// <summary>Download progress text, or null when not downloading.</summary>
    public string? DownloadText { get => _downloadText; private set { Set(ref _downloadText, value); Raise(nameof(IsDownloading)); } }
    public bool IsDownloading => _downloadText is not null;
    public string DownloadButtonText => FaultDataDownload.ReadManifest() is null ? $"Download fault database ({FaultDataDownload.ApproxSize})" : "Update fault database";
    public string FaultDataProject => FaultDataDownload.ProjectUrl.Replace("https://", "");

    public AsyncCommand DownloadFaultDataCommand { get; }
    public AsyncCommand OpenFaultDataPageCommand { get; }
    public AsyncCommand OpenFaultDataLicenseCommand { get; }
    public AsyncCommand ChooseEcuFolderCommand { get; }
    public AsyncCommand DownloadReadingsCommand { get; }
    public string DownloadReadingsText => _session.Database.Measurements is null
        ? $"Add engine readings ({Klemme15.DataImport.FullDatabaseImport.ApproxSize})" : "Update engine readings";

    /// <summary>
    /// Downloads the full database once and keeps only the engine measurement tables, so the Readings page works
    /// without BMW's files. Fetches the fault dictionaries first if they aren't installed yet.
    /// </summary>
    private async Task DownloadReadingsAsync()
    {
        _downloading = true;
        DownloadFaultDataCommand.RaiseCanExecuteChanged();
        DownloadReadingsCommand.RaiseCanExecuteChanged();
        var finished = false;
        try
        {
            if (FaultDataDownload.ReadManifest() is null)
            {
                DownloadText = "Downloading fault database first...";
                await FaultDataDownload.DownloadAsync(_session.Database);
            }
            var progress = new Progress<double>(p =>
            {
                if (!finished) DownloadText = p < 1 ? $"Downloading full database... {p:P0}" : "Checking and extracting engine tables (this takes a minute)...";
            });
            var count = await Klemme15.DataImport.FullDatabaseImport.DownloadAsync(_session.Database, progress: progress);
            finished = true;
            DownloadText = null;
            await RefreshFaultDataAsync();
            FaultDataStatus += $" Added readings for {count} engine computers; the large download was deleted again.";
        }
        finally
        {
            finished = true;
            DownloadText = null;
            _downloading = false;
            DownloadFaultDataCommand.RaiseCanExecuteChanged();
            DownloadReadingsCommand.RaiseCanExecuteChanged();
            Raise(nameof(DownloadReadingsText));
        }
    }

    private async Task RefreshFaultDataAsync()
    {
        var folder = _settings.EcuFolder ?? _settings.EdiabasPath;
        ShowFaultData(await Task.Run(() => OfficialSetup.Apply(_session.Database, folder)));
    }

    private void ShowFaultData(OfficialStatus status)
    {
        FaultDataStatus = status.Summary;
        NeedsFaultData = !status.Any;
        Raise(nameof(DownloadButtonText));
    }

    private async Task DownloadFaultDataAsync()
    {
        _downloading = true;
        DownloadFaultDataCommand.RaiseCanExecuteChanged();
        DownloadText = "Starting download...";
        // Progress updates are posted to the UI thread and can arrive after the download has finished;
        // once it's done they're ignored, so the status can't get stuck on "Checking and unpacking...".
        var finished = false;
        var progress = new Progress<double>(p =>
        {
            if (!finished) DownloadText = p < 1 ? $"Downloading... {p:P0}" : "Checking and unpacking...";
        });
        FaultDataDownload.Manifest manifest;
        try
        {
            manifest = await FaultDataDownload.DownloadAsync(_session.Database, progress: progress);
        }
        finally
        {
            finished = true;
            DownloadText = null;
        }
        _downloading = false;
        DownloadFaultDataCommand.RaiseCanExecuteChanged();
        await RefreshFaultDataAsync();
        FaultDataStatus += $" Installed {manifest.Variants} module dictionaries (checksum verified). Rescan to see the new meanings.";
    }

    private async Task ChooseEcuFolderAsync()
    {
        var folder = await _dialogs.PickFolderAsync("Choose your BMW Ecu folder (from ISTA, INPA or EDIABAS: the one with .prg files)");
        if (folder is null) return;
        _settings.EcuFolder = folder;
        _settings.Save();
        await RefreshFaultDataAsync();
    }

    /// <summary>What we know about the user's SGBD files, shown under the EDIABAS option.</summary>
    public string EdiabasStatus { get => _ediabasStatus; private set => Set(ref _ediabasStatus, value); }

    /// <summary>Looks for the user's EDIABAS/ISTA SGBD files and switches EDIABAS mode on when found.</summary>
    private async Task FindEdiabasAsync()
    {
        if (!string.IsNullOrWhiteSpace(_ediabasPath) && EdiabasLocator.CountPrg(_ediabasPath) > 0)
        {
            EdiabasStatus = $"Using {EdiabasLocator.CountPrg(_ediabasPath)} SGBD files in {_ediabasPath}.";
            return;
        }
        EdiabasStatus = "Looking for EDIABAS, INPA or ISTA files on this computer...";
        var found = _startup is { EdiabasSearched: true } done ? done.Ediabas : await Task.Run(EdiabasLocator.Find);
        if (found is null)
        {
            EdiabasStatus = "No EDIABAS files found. Klemme15 will use its own protocol stack; choose a folder if you have them elsewhere.";
            return;
        }
        EdiabasPath = found.Path;
        UseEdiabas = true;
        EdiabasStatus = $"Found {found.PrgCount} SGBD files from {found.Origin} in {found.Path}. Fault texts will match ISTA's for your exact modules.";
    }

    public override string Title => "Garage";
    public override Geometry Icon => Icons.Garage;

    public IReadOnlyList<FamilyOption> Families { get; }
    public IReadOnlyList<VehicleModel> Models => _family.Family.Models;
    public ObservableCollection<MarkerViewModel> PreviewMarkers { get; } = [];
    public ObservableCollection<PortInfo> Ports { get; } = [];

    public VehicleModel? Model
    {
        get => _model;
        set
        {
            if (value is null || !Set(ref _model, value)) return;
            RebuildPreview();
            foreach (var p in new[] { nameof(ModelTitle), nameof(Engine), nameof(Drive), nameof(Body), nameof(Years), nameof(DemoSummary) })
                Raise(p);
            OpenDemoCommand.RaiseCanExecuteChanged();
            ConnectCarCommand.RaiseCanExecuteChanged();
        }
    }

    public string ModelTitle => _model is null ? "" : $"{_family.Family.Id} {_model.Name}";
    public string Engine => _model?.Engine ?? "";
    public string Drive => _model?.Drive ?? "";
    public string Body => _model?.Body ?? "";
    public string Years => _model?.Years ?? "";
    public string CableHint => _family.Family.Cable == "KLine"
        ? "Set the cable switch to K-line. Ignition on (key position 2)."
        : "Set the cable switch to D-CAN. Ignition on (key position 2).";

    public string DemoSummary
    {
        get
        {
            if (_model is null) return "";
            var n = _model.DemoFaults.Count;
            return $"The demo {_model.Name} has {_model.ModuleIds.Count} modules and {(n == 1 ? "1 fault" : $"{n} faults")} waiting to be found.";
        }
    }

    public bool ShowCable { get => _showCable; set => Set(ref _showCable, value); }

    public PortInfo? Port
    {
        get => _port;
        set { if (Set(ref _port, value)) ConnectCarCommand.RaiseCanExecuteChanged(); }
    }

    public bool NoPorts => Ports.Count == 0 && !UsesNetwork;

    private string _networkAddress = "";

    /// <summary>A Wi-Fi or network cable: rfc2217://192.168.4.1:2217 or tcp://host:port. Takes precedence over the port list.</summary>
    public string NetworkAddress
    {
        get => _networkAddress;
        set
        {
            if (!Set(ref _networkAddress, value?.Trim() ?? "")) return;
            Raise(nameof(UsesNetwork));
            Raise(nameof(NetworkHint));
            Raise(nameof(NoPorts));
            ConnectCarCommand.RaiseCanExecuteChanged();
        }
    }

    public bool UsesNetwork => Klemme15.Protocols.Transport.NetworkTransport.IsNetworkAddress(_networkAddress);

    public string NetworkHint => _networkAddress.Length == 0 ? ""
        : UsesNetwork ? "Klemme15 will connect over the network instead of USB."
        : "Use rfc2217://address:port (or tcp://address:port), e.g. rfc2217://192.168.4.1:2217";

    private string? PortName => UsesNetwork ? _networkAddress : _port?.Name;
    public bool EdiabasAvailable => BackendFactory.EdiabasAvailable;
    public bool UseEdiabas { get => _useEdiabas; set => Set(ref _useEdiabas, value); }
    public string? EdiabasPath { get => _ediabasPath; set => Set(ref _ediabasPath, value); }
    public bool TrustDsr { get => _trustDsr; set => Set(ref _trustDsr, value); }
    public bool IsConnected => _session.IsConnected;
    public string ConnectedText => _session.IsConnected ? $"Connected: {_session.Status}" : "";

    public string? Error
    {
        get => _error;
        set { if (Set(ref _error, value)) Raise(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);

    public AsyncCommand OpenDemoCommand { get; }
    public AsyncCommand ConnectCarCommand { get; }
    public RelayCommand ShowCableCommand { get; }
    public AsyncCommand DisconnectCommand { get; }
    public RelayCommand RefreshPortsCommand { get; }
    public AsyncCommand BrowseEdiabasCommand { get; }

    public void SelectFamily(FamilyOption option)
    {
        if (option == _family) return;
        _family.IsSelected = false;
        _family = option;
        _family.IsSelected = true;
        Raise(nameof(Models));
        Raise(nameof(CableHint));
        Model = _family.Family.Models.FirstOrDefault();
    }

    /// <summary>Selects a model by id (used by the screenshot tool and saved settings).</summary>
    public void SelectModel(string modelId)
    {
        var family = Families.FirstOrDefault(f => f.Family.Models.Any(m => m.Id == modelId));
        if (family is null) return;
        SelectFamily(family);
        Model = family.Family.Models.First(m => m.Id == modelId);
    }

    private void RebuildPreview()
    {
        PreviewMarkers.Clear();
        if (_model is null) return;
        foreach (var ecu in _family.Family.Modules.Where(m => m.Position is not null))
        {
            var fitted = _model.ModuleIds.Contains(ecu.Id);
            PreviewMarkers.Add(new MarkerViewModel(MarkerKind.Module, ecu.Id, ecu.Id.Split('.').Last(), ecu.Position!.Value)
            {
                Ecu = ecu,
                State = fitted ? MarkerState.Idle : MarkerState.Absent,
                Detail = fitted ? ecu.Name : $"{ecu.Name} (not fitted to this model's demo car)",
            });
        }
    }

    private void RefreshPorts()
    {
        Ports.Clear();
        try
        {
            foreach (var p in _startup?.Ports ?? PortDiscovery.List()) Ports.Add(p);
        }
        catch (Exception e)
        {
            Error = $"Couldn't list serial ports: {e.Message}";
        }
        if (Klemme15.Protocols.Transport.NetworkTransport.IsNetworkAddress(_settings.Port)) NetworkAddress = _settings.Port!;
        Port = Ports.FirstOrDefault(p => p.Name == _settings.Port) ?? Ports.FirstOrDefault(p => p.LikelyKDCan) ?? Ports.FirstOrDefault();
        Raise(nameof(NoPorts));
    }

    private async Task BrowseEdiabasAsync()
    {
        var folder = await _dialogs.PickFolderAsync("Choose your EDIABAS Ecu folder (the one with .prg files)");
        if (folder is not null) EdiabasPath = folder;
    }

    private async Task ConnectAsync(bool demo)
    {
        if (_model is null) return;
        Error = null;
        _busy = true;
        OpenDemoCommand.RaiseCanExecuteChanged();
        ConnectCarCommand.RaiseCanExecuteChanged();
        var settings = new ConnectionSettings
        {
            VehicleId = _family.Family.Id,
            ModelId = _model.Id,
            Port = demo ? null : PortName,
            Demo = demo,
            EdiabasPath = !demo && _useEdiabas ? _ediabasPath : null,
            TrustDsr = _trustDsr,
            LiveFavorites = _settings.LiveFavorites.Select(f => f.ToParameter()).ToList(),
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await _session.ConnectAsync(settings, _family.Family, _model, cts.Token);
        }
        catch (DiagnosticException e)
        {
            Error = e.Message;
            return;
        }
        finally
        {
            _busy = false;
            OpenDemoCommand.RaiseCanExecuteChanged();
            ConnectCarCommand.RaiseCanExecuteChanged();
        }

        _settings.VehicleId = _family.Family.Id;
        _settings.ModelId = _model.Id;
        _settings.Port = PortName ?? _settings.Port;
        _settings.UseDemo = demo;
        _settings.UseEdiabas = _useEdiabas;
        _settings.EdiabasPath = _ediabasPath;
        _settings.TrustDsr = _trustDsr;
        _settings.Save();
        _onConnected();
    }
}
