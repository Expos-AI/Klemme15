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
    public string Name => Family.Id == "E9x" ? "E90 / E91 / E92 / E93" : Family.IsBeta ? Family.Id + " beta" : Family.Id;
    public string Years => Family.Years;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public RelayCommand? SelectCommand { get; set; }
}

public sealed class CableFindingViewModel(CableFinding finding)
{
    public string Title => finding.Title;
    public string Detail => finding.Detail;
    public bool IsWarn => finding.Level == CheckLevel.Warn;
    public bool IsAsk => finding.Level == CheckLevel.Ask;
    public bool IsGood => finding.Level == CheckLevel.Good;
    public bool IsInfo => finding.Level == CheckLevel.Info;
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

        // While a job is changing the car, switching cars or disconnecting would cut it off: these wait.
        OpenDemoCommand = new AsyncCommand(() => ConnectAsync(demo: true), () => !_busy && !_session.JobRunning && _model is not null, e => Error = Problems.Describe(e));
        OpenSnapshotCommand = new AsyncCommand(OpenSnapshotAsync, () => !_busy && !_session.JobRunning, e => Error = Problems.Describe(e));
        ConnectCarCommand = new AsyncCommand(() => ConnectAsync(demo: false), () => !_busy && !_session.JobRunning && _model is not null && (_port is not null || UsesNetwork), e => Error = Problems.Describe(e));
        ShowCableCommand = new RelayCommand(() => ShowCable = true);
        DisconnectCommand = new AsyncCommand(_session.DisconnectAsync, () => _session.IsConnected && !_session.JobRunning);
        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        CheckCableCommand = new RelayCommand(() => _ = CheckCableAsync(tryOpen: !_session.IsConnected), () => _port is not null && !UsesNetwork && !_busy);
        BrowseEdiabasCommand = new AsyncCommand(BrowseEdiabasAsync);
        DownloadFaultDataCommand = new AsyncCommand(DownloadFaultDataAsync, () => !_downloading, e => { _downloading = false; DownloadText = null; FaultDataStatus = $"Download failed ({e.Message}). Check your internet connection and try again."; DownloadFaultDataCommand?.RaiseCanExecuteChanged(); });
        OpenFaultDataPageCommand = new AsyncCommand(() => _dialogs.OpenUrlAsync(FaultDataDownload.ProjectUrl));
        OpenFaultDataLicenseCommand = new AsyncCommand(() => _dialogs.OpenUrlAsync(FaultDataDownload.LicenseUrl));
        ChooseEcuFolderCommand = new AsyncCommand(ChooseEcuFolderAsync);
        DownloadReadingsCommand = new AsyncCommand(DownloadReadingsAsync, () => !_downloading,
            e => { _downloading = false; DownloadText = null; FaultDataStatus = $"Download failed ({e.Message}). Check your internet connection and try again."; DownloadReadingsCommand?.RaiseCanExecuteChanged(); DownloadFaultDataCommand.RaiseCanExecuteChanged(); });

        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionState.JobRunning))
            {
                foreach (var c in new[] { OpenDemoCommand, OpenSnapshotCommand, ConnectCarCommand, DisconnectCommand }) c.RaiseCanExecuteChanged();
                return;
            }
            if (e.PropertyName is not nameof(SessionState.Backend)) return;
            DisconnectCommand.RaiseCanExecuteChanged();
            Raise(nameof(IsConnected));
            Raise(nameof(ConnectedText));
        };
        _session.HistoryChanged += () => OnUi(RebuildCars);
        RebuildCars();
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
    /// <summary>
    /// The opt-in for third-party, BMW-derived data: the first time, the notice says what it is and whose it is, and
    /// the download only starts after a deliberate "Download it".
    /// </summary>
    private async Task<bool> AcceptFaultDataAsync()
    {
        if (_settings.AcceptedFaultDataNotice) return true;
        if (!await _dialogs.ConfirmAsync("Third-party BMW data", FaultDataDownload.Notice, "Download it")) return false;
        _settings.AcceptedFaultDataNotice = true;
        _settings.Save();
        return true;
    }

    private async Task DownloadReadingsAsync()
    {
        if (!await AcceptFaultDataAsync()) return;
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
        if (!await AcceptFaultDataAsync()) return;
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
            EdiabasStatus = "No EDIABAS files found. Klemme15 will use its own built-in connection; choose a folder if you have them elsewhere.";
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
    /// <summary>Show miles, mph, °F and psi instead of km, km/h, °C and kPa. Files Klemme15 writes stay metric.</summary>
    public bool UsUnits
    {
        get => DisplayUnits.IsUs;
        set
        {
            if (value == DisplayUnits.IsUs) return;
            DisplayUnits.System = value ? UnitSystem.US : UnitSystem.Metric;
            _settings.Units = value ? "US" : "Metric";
            _settings.Save();
            Raise(nameof(UsUnits));
            Raise(nameof(MetricUnits));
            _session.RefreshViews();
        }
    }
    public bool MetricUnits { get => !UsUnits; set { if (value) UsUnits = false; } }

    public string? FamilyNote => _family.Family.Note;
    public bool HasFamilyNote => _family.Family.Note is not null;

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
        set
        {
            if (!Set(ref _port, value)) return;
            ConnectCarCommand.RaiseCanExecuteChanged();
            CheckCableCommand?.RaiseCanExecuteChanged();
            _ = CheckCableAsync(tryOpen: false);
        }
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
    public AsyncCommand OpenSnapshotCommand { get; }

    private async Task OpenSnapshotAsync()
    {
        Error = null;
        var file = await _dialogs.OpenBytesAsync("Open a snapshot", "Klemme15 snapshot", ["*.k15"]);
        if (file is not { } f) return;
        await OpenSnapshotAsync(f.Data);
    }

    /// <summary>Opens a snapshot's bytes (from the button, or a .k15 file the app was started with).</summary>
    /// <summary>Opens a snapshot; returns why it couldn't (also shown in the Garage), or null when it opened.</summary>
    public async Task<string?> OpenSnapshotAsync(byte[] data)
    {
        Error = null;
        try
        {
            var snapshot = Klemme15.Core.SnapshotFile.Read(data);
            if (snapshot.Manifest.ModelId is { } id) SelectModel(id);
            await _session.OpenSnapshotAsync(snapshot);
            return null;
        }
        catch (Klemme15.Core.SnapshotException e) { return Error = e.Message; }
    }
    public AsyncCommand ConnectCarCommand { get; }
    public RelayCommand ShowCableCommand { get; }
    public AsyncCommand DisconnectCommand { get; }
    public RelayCommand RefreshPortsCommand { get; }

    // ---------------------------------------------------------------- cable check

    public RelayCommand CheckCableCommand { get; }
    public ObservableCollection<CableFindingViewModel> CableFindings { get; } = [];
    public bool HasCableFindings => CableFindings.Count > 0;

    /// <summary>
    /// Checks the cable behind the selected port: FTDI or not, known signs of a cloned chip, the latency timer, and
    /// (when tryOpen) whether another program has the port. Reads only what the computer knows; nothing goes to the car.
    /// </summary>
    private int _cableCheck;

    private async Task<IReadOnlyList<CableFinding>> CheckCableAsync(bool tryOpen)
    {
        var mine = ++_cableCheck;
        IReadOnlyList<CableFinding> findings = [];
        if (_port is { } port && !UsesNetwork)
        {
            // Off the UI thread: the registry walk, and opening a port (a Bluetooth COM port can hang for seconds).
            try { findings = await Task.Run(() => CableCheck.Assess(CableInspector.Read(port.Name, tryOpen))); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        }
        if (mine != _cableCheck) return findings;   // a newer check (another port picked) replaced this one
        CableFindings.Clear();
        foreach (var f in findings) CableFindings.Add(new CableFindingViewModel(f));
        Raise(nameof(HasCableFindings));
        return findings;
    }
    public AsyncCommand BrowseEdiabasCommand { get; }

    public void SelectFamily(FamilyOption option)
    {
        if (option == _family) return;
        _family.IsSelected = false;
        _family = option;
        _family.IsSelected = true;
        Raise(nameof(Models));
        Raise(nameof(CableHint));
        Raise(nameof(FamilyNote));
        Raise(nameof(HasFamilyNote));
        Model = _family.Family.Models.FirstOrDefault();
    }

    /// <summary>Every real car scanned on this computer, newest first (from the local health history).</summary>
    public System.Collections.ObjectModel.ObservableCollection<CarCardViewModel> Cars { get; } = [];
    public bool HasCars => Cars.Count > 0;
    public string CarsNote => "Saved on this computer after every quick test, never uploaded. Pick a car to select its model; its trends are on the Faults page after the next quick test.";

    private void RebuildCars()
    {
        Cars.Clear();
        try
        {
            foreach (var latest in _session.History.Cars().Take(12))
                Cars.Add(new CarCardViewModel(latest, _session.History.Load(latest.CarKey), SelectModel, _session.ForgetCar));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        Raise(nameof(HasCars));
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
        var preview = new List<MarkerViewModel>();
        foreach (var ecu in _family.Family.Modules.Where(m => m.Position is not null))
        {
            var fitted = _model.ModuleIds.Contains(ecu.Id);
            preview.Add(new MarkerViewModel(MarkerKind.Module, ecu.Id, ecu.Id.Split('.').Last(), ecu.Position!.Value)
            {
                Ecu = ecu,
                State = fitted ? MarkerState.Idle : MarkerState.Absent,
                Detail = fitted ? ModuleGlossary.Describe(ecu) : $"{ModuleGlossary.Describe(ecu)}\n(Not fitted to this model's demo car.)",
            });
        }
        MarkerLayout.SpreadModules(preview);
        foreach (var m in preview) PreviewMarkers.Add(m);
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
            Error = $"Couldn't list serial ports ({e.Message}). Unplug the cable, plug it back in and press Refresh.";
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
        // Pre-flight: a port another program holds can't work, so say so instead of timing out against the car.
        // Klemme15's own connection holds the port: let go of it first, or the check would blame "another program".
        if (!demo && !UsesNetwork) await _session.DisconnectAsync();
        if (!demo && !UsesNetwork && (await CheckCableAsync(tryOpen: true)).FirstOrDefault(f => f.Title.Contains("in use", StringComparison.Ordinal)) is { } busy)
        {
            Error = $"{busy.Title}. {busy.Detail}";
            return;
        }
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
