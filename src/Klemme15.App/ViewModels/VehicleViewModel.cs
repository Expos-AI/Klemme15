using System.Collections.ObjectModel;
using Avalonia.Media;
using Klemme15.App.Services;
using Klemme15.Core;
using Klemme15.Data;

namespace Klemme15.App.ViewModels;

/// <summary>
/// The car drawing: every module sits where it is in the car and lights up as the quick test reaches it;
/// every fault gets a marker on the part it points at.
/// </summary>
public sealed class VehicleViewModel : PageViewModel
{
    private readonly SessionState _session;
    private readonly IDialogService _dialogs;
    private readonly Dictionary<string, MarkerViewModel> _moduleMarkers = new();
    private CancellationTokenSource? _cts;
    private MarkerViewModel? _selected;
    private bool _isScanning;
    private string _progressText = "";
    private double _progress;
    private string? _error;
    private string? _discovery;
    private bool _isDiscovering;
    private string? _warning;
    private bool _confirmClearAll;

    public VehicleViewModel(SessionState session, IDialogService dialogs)
    {
        _session = session;
        _dialogs = dialogs;
        RunCommand = new AsyncCommand(RunAsync, () => _session.IsConnected && !_isScanning && !_isDiscovering, e => Error = e.Message);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => _isScanning);
        ClearSelectionCommand = new RelayCommand(() => Select(null));
        AskClearAllCommand = new RelayCommand(() => ConfirmClearAll = true, () => HasFaults);
        CancelClearAllCommand = new RelayCommand(() => ConfirmClearAll = false);
        ClearAllCommand = new AsyncCommand(ClearAllAsync, () => _session.IsConnected && !_isScanning, e => Error = e.Message);
        DiscoverCommand = new AsyncCommand(DiscoverAsync, () => _session.Backend is IModuleDiscovery && !_isScanning && !_isDiscovering, e => Error = e.Message);
        ExportHtmlCommand = new AsyncCommand(() => ExportAsync("html"), () => _session.LastScan is not null);
        ExportMarkdownCommand = new AsyncCommand(() => ExportAsync("md"), () => _session.LastScan is not null);
        CopyForumCommand = new AsyncCommand(CopyForumAsync, () => _session.LastScan is not null);
        ExportJsonCommand = new AsyncCommand(() => ExportAsync("json"), () => _session.LastScan is not null);

        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionState.Backend))
            {
                RunCommand.RaiseCanExecuteChanged();
                DiscoverCommand.RaiseCanExecuteChanged();
                Discovery = null;
                Raise(nameof(IsConnected));
                Raise(nameof(NotConnected));
                OnUi(Rebuild);
            }
        };
        _session.ScanUpdated += () => OnUi(Rebuild);
    }

    public override string Title => "Vehicle";
    public override Geometry Icon => Icons.Car;

    public ObservableCollection<MarkerViewModel> Markers { get; } = [];
    public ObservableCollection<FaultItemViewModel> Faults { get; } = [];

    public bool IsConnected => _session.IsConnected;
    public bool NotConnected => !_session.IsConnected;
    public bool HasScan => _session.LastScan is not null;
    public bool HasFaults => Faults.Count > 0;
    public bool NoFaultsAfterScan => HasScan && !HasFaults;
    public bool BeforeScan => _session.IsConnected && !HasScan && !_isScanning;

    public string Summary
    {
        get
        {
            if (_session.LastScan is not { } s) return "";
            var faults = s.TotalFaults == 1 ? "1 fault" : $"{s.TotalFaults} faults";
            var battery = s.BatteryVoltage is { } v ? $" Battery {v:0.0} V." : "";
            return $"{s.RespondingEcus} of {s.Ecus.Count} modules answered in {s.Elapsed.TotalSeconds:0.0} s. {faults}.{battery}";
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (!Set(ref _isScanning, value)) return;
            RunCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
            DiscoverCommand.RaiseCanExecuteChanged();
            Raise(nameof(BeforeScan));
        }
    }

    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    public string? Error
    {
        get => _error;
        set { if (Set(ref _error, value)) Raise(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);

    // ------------------------------------------------------------------ selection

    public MarkerViewModel? Selected
    {
        get => _selected;
        private set
        {
            if (!Set(ref _selected, value)) return;
            foreach (var p in new[] { nameof(HasSelection), nameof(NoSelection), nameof(SelectedTitle), nameof(SelectedKind), nameof(SelectedInfo), nameof(SelectedFaults), nameof(SelectedHasFaults) })
                Raise(p);
        }
    }

    public bool HasSelection => _selected is not null;
    public bool NoSelection => _selected is null;
    public string SelectedTitle => _selected is null ? "" : _selected.IsFaultSource ? _selected.Detail : _selected.Ecu?.Name ?? _selected.Label;
    public string SelectedKind => _selected?.Kind == MarkerKind.FaultSource ? "Where the fault points" : "Module";
    public IReadOnlyList<FaultItemViewModel> SelectedFaults => _selected?.Faults ?? [];
    public bool SelectedHasFaults => SelectedFaults.Count > 0;

    public string SelectedInfo
    {
        get
        {
            if (_selected is null) return "";
            if (_selected.IsFaultSource) return "Check this area first. Positions are approximate.";
            var ecu = _selected.Ecu!;
            var result = _session.LastScan?.Ecus.FirstOrDefault(e => e.Ecu.Id == ecu.Id);
            var status = result?.Status switch
            {
                EcuScanStatus.Ok => "Answered, no faults stored.",
                EcuScanStatus.Faults => "Answered with faults.",
                EcuScanStatus.NoResponse => "Did not answer. It may not be fitted to this car.",
                EcuScanStatus.Error => $"Error: {result.Error}",
                _ => "Not asked yet. Run a quick test.",
            };
            var ident = result?.Identity?.Summary;
            var lines = new List<string> { $"Address {ecu.AddressHex}", status };
            if (!string.IsNullOrWhiteSpace(ident)) lines.Add(ident);
            if (result?.Note is { } note) lines.Add(note);
            if (!ecu.Verified) lines.Add("This module's address hasn't been confirmed on a real car yet.");
            return string.Join("\n", lines);
        }
    }

    public RelayCommand ClearSelectionCommand { get; }

    public void Select(MarkerViewModel? marker)
    {
        if (_selected is not null) _selected.IsSelected = false;
        Selected = marker == _selected ? null : marker;
        if (_selected is not null) _selected.IsSelected = true;
    }

    /// <summary>Selects the marker for a fault (used by "Show on car" on the Faults page).</summary>
    public void ShowFault(Fault fault, EcuDefinition ecu)
    {
        var item = Faults.FirstOrDefault(f => f.Fault.Code == fault.Code && f.Ecu.Id == ecu.Id);
        if (item?.Marker is { } m && m != _selected) Select(m);
    }

    // ------------------------------------------------------------------ commands

    public AsyncCommand RunCommand { get; }
    public RelayCommand AskClearAllCommand { get; }
    public RelayCommand CancelClearAllCommand { get; }
    public AsyncCommand ClearAllCommand { get; }

    /// <summary>Shown above the drawing, e.g. a low battery before a scan.</summary>
    public string? Warning
    {
        get => _warning;
        private set { if (Set(ref _warning, value)) Raise(nameof(HasWarning)); }
    }

    public bool HasWarning => !string.IsNullOrEmpty(_warning);

    private string? _frmNotice;
    private bool _frmFragile;

    /// <summary>What the quick test found out about the footwell module (FRM3 or not), shown above the car.</summary>
    public string? FrmNotice
    {
        get => _frmNotice;
        private set { if (Set(ref _frmNotice, value)) { Raise(nameof(HasFrmNotice)); Raise(nameof(FrmFragile)); Raise(nameof(FrmSafe)); } }
    }

    public bool HasFrmNotice => !string.IsNullOrEmpty(_frmNotice);
    public bool FrmFragile => HasFrmNotice && _frmFragile;
    public bool FrmSafe => HasFrmNotice && !_frmFragile;

    private void UpdateFrmNotice(VehicleScanResult scan)
    {
        var frm = scan.Ecus.FirstOrDefault(e => FrmGuard.IsFootwellModule(e.Ecu) && e.Identity is not null);
        if (frm is null) { FrmNotice = null; return; }
        var a = FrmGuard.Assess(frm.Identity);
        _frmFragile = a.Fragile;
        FrmNotice = $"{a.Label}{(frm.Identity!.PartNumber is { } pn ? $", part {pn}" : "")}: {a.Advice}";
    }

    public bool ConfirmClearAll
    {
        get => _confirmClearAll;
        set { if (Set(ref _confirmClearAll, value)) Raise(nameof(NotConfirmClearAll)); }
    }

    public bool NotConfirmClearAll => !_confirmClearAll;

    public string ClearAllQuestion =>
        $"Clear the fault memory of {(_session.LastScan?.Ecus.Count(e => e.Faults.Count > 0) ?? 0)} modules? Read them again after a drive to see what returns.";

    /// <summary>Clears every module that reported faults, then runs the quick test again.</summary>
    private async Task ClearAllAsync()
    {
        ConfirmClearAll = false;
        if (_session.Backend is not { } backend || _session.LastScan is not { } scan) return;
        var failed = new List<string>();
        var skipped = new List<string>();
        foreach (var e in scan.Ecus.Where(e => e.Faults.Count > 0))
        {
            try
            {
                if (!_session.IsDemo)
                {
                    var guard = await WriteGuard.CheckAsync(backend, e.Ecu, e.Identity, WriteKind.ClearFaults,
                        _session.Vehicle?.Modules.FirstOrDefault(m => m.IsEngine), _session.Measurements);
                    if (!guard.Allowed) { skipped.Add($"{e.Ecu.Name} ({guard.Reason})"); continue; }
                }
                await backend.ClearFaultsAsync(e.Ecu);
            }
            catch (Exception ex) when (ex is DiagnosticException or NotSupportedException) { failed.Add(e.Ecu.Name); }
        }
        await RunAsync();
        var problems = new List<string>();
        if (failed.Count > 0) problems.Add("Couldn't clear: " + string.Join(", ", failed));
        if (skipped.Count > 0) problems.Add("Left alone to protect it: " + string.Join("; ", skipped));
        if (problems.Count > 0) Error = string.Join("\n", problems);
    }

    /// <summary>
    /// Low voltage makes modules log false faults, which is why dealer tools want a charger connected.
    /// Reads the battery voltage through the engine computer when the car reports it.
    /// </summary>
    private async Task<double?> CheckBatteryAsync(IDiagnosticBackend backend, VehicleFamily vehicle)
    {
        var engine = vehicle.Modules.FirstOrDefault(m => m.IsEngine);
        if (engine is null) return null;
        try
        {
            // The standard OBD value first; many BMW engine computers (MSV70 among them) don't answer it, so then
            // BMW's own battery voltage measurement.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var read = await new EngineSignals(backend, engine, _session.Measurements).ReadAsync(["voltage"], cts.Token);
            double? v = read.TryGetValue("voltage", out var volts) ? volts : null;
            Warning = v switch
            {
                < 12.0 => $"Battery at {v:0.0} V. Low voltage makes modules log faults that aren't real. Charge the battery or connect a charger, then scan again."
                          + (FrmGuard.FamiliesWithFrm.Contains(vehicle.Id)
                              ? " Don't clear faults, code or run service functions until it's charged: the footwell module (FRM) on these cars can corrupt itself if the voltage dips while it writes."
                              : ""),
                > 15.2 => $"Voltage is {v:0.0} V, higher than normal. Check the charger or alternator before trusting new faults.",
                _ => null,
            };
            return v;
        }
        catch (Exception) { return null; }
    }
    public AsyncCommand DiscoverCommand { get; }

    /// <summary>Result of "Find other modules", shown in the side panel.</summary>
    public string? Discovery
    {
        get => _discovery;
        private set { if (Set(ref _discovery, value)) Raise(nameof(HasDiscovery)); }
    }

    public bool HasDiscovery => !string.IsNullOrEmpty(_discovery);

    private async Task DiscoverAsync()
    {
        if (_session.Backend is not IModuleDiscovery discovery || _session.Vehicle is not { } vehicle) return;
        var addresses = Klemme15.Data.VehicleDatabase.UnlistedAddresses(vehicle);
        _isDiscovering = true;
        DiscoverCommand.RaiseCanExecuteChanged();
        RunCommand.RaiseCanExecuteChanged();
        Discovery = $"Asking {addresses.Count} other addresses...";
        try
        {
            var progress = new Progress<int>(n => Discovery = $"Asking other addresses: {n} of {addresses.Count}");
            var found = await discovery.DiscoverAsync(addresses, progress);
            Discovery = found.Count == 0
                ? "No other modules answered. The database already covers everything on this car."
                : $"{found.Count} more module(s) answered at addresses the database doesn't list yet:\n"
                  + string.Join("\n", found.Select(m => $"{m.AddressHex}  {m.Identity.Summary}"))
                  + "\nPlease report these with your traffic log so they can be named and placed on the car.";
            foreach (var m in found) _session.Log.Note($"Discovered module at {m.AddressHex}: {m.Identity.Summary}");
        }
        finally
        {
            _isDiscovering = false;
            DiscoverCommand.RaiseCanExecuteChanged();
            RunCommand.RaiseCanExecuteChanged();
        }
    }
    public RelayCommand CancelCommand { get; }
    public AsyncCommand ExportHtmlCommand { get; }
    public AsyncCommand ExportMarkdownCommand { get; }
    public AsyncCommand ExportJsonCommand { get; }

    public override void OnShown()
    {
        RunCommand.RaiseCanExecuteChanged();
        if (Markers.Count == 0) Rebuild();
    }

    private async Task RunAsync()
    {
        var backend = _session.Backend;
        var vehicle = _session.Vehicle;
        if (backend is null || vehicle is null) return;

        Error = null;
        Warning = null;
        Select(null);
        _session.PublishScan(null);
        IsScanning = true;
        _cts = new CancellationTokenSource();
        ProgressText = "Checking battery voltage";
        var battery = await CheckBatteryAsync(backend, vehicle);
        var progress = new Progress<ScanProgress>(p =>
        {
            Progress = p.Total == 0 ? 0 : 100.0 * p.Done / p.Total;
            ProgressText = p.Message;
            if (p.Current is { } cur && _moduleMarkers.TryGetValue(cur.Id, out var asking)) asking.State = MarkerState.Asking;
            if (p.Latest is { } r && _moduleMarkers.TryGetValue(r.Ecu.Id, out var done)) done.State = StateOf(r);
        });

        try
        {
            var scan = await new VehicleScanner(backend, _session.Codes).ScanAsync(
                vehicle.Modules, $"{vehicle.Id} {_session.Model?.Name}".Trim(), progress, _cts.Token);
            Progress = 100;
            ProgressText = scan.Cancelled ? "Quick test stopped." : "";
            _session.PublishScan(scan with { BatteryVoltage = battery });
            UpdateFrmNotice(scan);
        }
        finally
        {
            IsScanning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private static MarkerState StateOf(EcuScanResult r) => r.Status switch
    {
        EcuScanStatus.Ok => MarkerState.Ok,
        EcuScanStatus.Faults => r.Faults.Any(f => f.State == FaultState.Present && !f.IsInfo) ? MarkerState.Active : MarkerState.Stored,
        EcuScanStatus.NoResponse => MarkerState.Absent,
        EcuScanStatus.Error => MarkerState.Stored,
        _ => MarkerState.Idle,
    };

    /// <summary>Rebuilds every marker and the fault list from the vehicle and the latest scan.</summary>
    private void Rebuild()
    {
        var selectedId = _selected?.Id;
        Select(null);
        Markers.Clear();
        Faults.Clear();
        _moduleMarkers.Clear();

        if (_session.Vehicle is { } vehicle)
        {
            foreach (var ecu in vehicle.Modules.Where(m => m.Position is not null))
            {
                var m = new MarkerViewModel(MarkerKind.Module, ecu.Id, ecu.Id.Split('.').Last(), ecu.Position!.Value, Select)
                {
                    Ecu = ecu,
                    Detail = ecu.Name,
                };
                _moduleMarkers[ecu.Id] = m;
                Markers.Add(m);
            }
        }

        if (_session.LastScan is { } scan)
        {
            var sources = new Dictionary<string, MarkerViewModel>();
            foreach (var result in scan.Ecus)
            {
                if (_moduleMarkers.TryGetValue(result.Ecu.Id, out var mm)) mm.State = StateOf(result);
                foreach (var fault in result.Faults.Where(f => !f.IsInfo))
                {
                    var where = _session.Locate(fault, result.Ecu);
                    var item = new FaultItemViewModel(fault, result.Ecu, where?.Title, where?.IsComponent == true);
                    Faults.Add(item);
                    if (where is null) continue;

                    MarkerViewModel marker;
                    if (!where.IsComponent && mm is not null) marker = mm;   // the module itself is the best we know
                    else
                    {
                        var key = $"{where.Position.X:0},{where.Position.Y:0}";
                        if (!sources.TryGetValue(key, out marker!))
                        {
                            marker = new MarkerViewModel(MarkerKind.FaultSource, "src:" + key, where.Title, where.Position, Select) { Detail = where.Title };
                            sources[key] = marker;
                        }
                    }
                    marker.Faults.Add(item);
                    if (marker.IsFaultSource)
                        marker.State = marker.Faults.Any(f => f.IsActive) ? MarkerState.Active : MarkerState.Stored;
                    if (mm is not null && marker != mm) mm.Faults.Add(item);
                    item.Marker = marker;
                    item.ShowCommand = new RelayCommand(() => Select(item.Marker));
                }
            }
            foreach (var s in sources.Values)
            {
                s.Label = string.Join("  ", s.Faults.Select(f => f.Code).Distinct());
                Markers.Add(s);   // fault sources drawn above modules
            }
        }

        // Active faults first, then by module.
        var ordered = Faults.OrderByDescending(f => f.IsActive).ThenBy(f => f.ModuleName).ToList();
        Faults.Clear();
        foreach (var f in ordered) Faults.Add(f);

        foreach (var p in new[] { nameof(HasScan), nameof(HasFaults), nameof(NoFaultsAfterScan), nameof(BeforeScan), nameof(Summary), nameof(ClearAllQuestion) })
            Raise(p);
        AskClearAllCommand.RaiseCanExecuteChanged();
        ExportHtmlCommand.RaiseCanExecuteChanged();
        ExportMarkdownCommand.RaiseCanExecuteChanged();
        ExportJsonCommand.RaiseCanExecuteChanged();
        CopyForumCommand.RaiseCanExecuteChanged();
        if (selectedId is not null && Markers.FirstOrDefault(m => m.Id == selectedId) is { } again) Select(again);
    }

    public AsyncCommand CopyForumCommand { get; }

    private string _copied = "";

    /// <summary>"Copied: paste it into your forum post" after the forum summary was copied.</summary>
    public string CopiedText { get => _copied; private set => Set(ref _copied, value); }

    private async Task CopyForumAsync()
    {
        if (_session.LastScan is not { } scan) return;
        var car = _session.Model is { } m ? $"{_session.Vehicle?.Id} {m.Name} ({m.Engine})" : null;
        var text = ReportWriter.ToForumPost(scan, car);
        if (await _dialogs.CopyTextAsync(text))
            CopiedText = "Copied (VIN removed). Paste it into your forum post.";
        else
            await _dialogs.SaveTextAsync("Save forum summary", $"klemme15-forum-{scan.Started:yyyyMMdd-HHmm}.md", "md", text);
    }

    private async Task ExportAsync(string ext)
    {
        if (_session.LastScan is not { } scan) return;
        var content = ext switch
        {
            "json" => ReportWriter.ToJson(scan),
            "md" => ReportWriter.ToMarkdown(scan),
            _ => ReportWriter.ToHtml(scan),
        };
        var name = $"klemme15-{scan.Chassis?.Replace(' ', '-')}-{scan.Started:yyyyMMdd-HHmm}.{ext}";
        await _dialogs.SaveTextAsync("Save quick test report", name, ext, content);
    }
}
