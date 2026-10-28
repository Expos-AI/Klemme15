using System.Collections.ObjectModel;
using Avalonia.Media;
using OpenHood.App.Services;
using OpenHood.Core;

namespace OpenHood.App.ViewModels;

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

    public VehicleViewModel(SessionState session, IDialogService dialogs)
    {
        _session = session;
        _dialogs = dialogs;
        RunCommand = new AsyncCommand(RunAsync, () => _session.IsConnected && !_isScanning, e => Error = e.Message);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => _isScanning);
        ClearSelectionCommand = new RelayCommand(() => Select(null));
        ExportHtmlCommand = new AsyncCommand(() => ExportAsync("html"), () => _session.LastScan is not null);
        ExportMarkdownCommand = new AsyncCommand(() => ExportAsync("md"), () => _session.LastScan is not null);
        ExportJsonCommand = new AsyncCommand(() => ExportAsync("json"), () => _session.LastScan is not null);

        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionState.Backend))
            {
                RunCommand.RaiseCanExecuteChanged();
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
            return $"{s.RespondingEcus} of {s.Ecus.Count} modules answered in {s.Elapsed.TotalSeconds:0.0} s. {faults}.";
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
    public string SelectedTitle => _selected?.Ecu?.Name ?? _selected?.Label ?? "";
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
        Select(null);
        _session.PublishScan(null);
        IsScanning = true;
        _cts = new CancellationTokenSource();
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
            _session.PublishScan(scan);
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
        EcuScanStatus.Faults => r.Faults.Any(f => f.State == FaultState.Present) ? MarkerState.Active : MarkerState.Stored,
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
                foreach (var fault in result.Faults)
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
            foreach (var s in sources.Values) Markers.Add(s);   // fault sources drawn above modules
        }

        // Active faults first, then by module.
        var ordered = Faults.OrderByDescending(f => f.IsActive).ThenBy(f => f.ModuleName).ToList();
        Faults.Clear();
        foreach (var f in ordered) Faults.Add(f);

        foreach (var p in new[] { nameof(HasScan), nameof(HasFaults), nameof(NoFaultsAfterScan), nameof(BeforeScan), nameof(Summary) })
            Raise(p);
        ExportHtmlCommand.RaiseCanExecuteChanged();
        ExportMarkdownCommand.RaiseCanExecuteChanged();
        ExportJsonCommand.RaiseCanExecuteChanged();
        if (selectedId is not null && Markers.FirstOrDefault(m => m.Id == selectedId) is { } again) Select(again);
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
        var name = $"openhood-{scan.Chassis?.Replace(' ', '-')}-{scan.Started:yyyyMMdd-HHmm}.{ext}";
        await _dialogs.SaveTextAsync("Save quick test report", name, ext, content);
    }
}
