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
        Topology = new TopologyOverlayViewModel(session) { PageBusy = () => _isScanning || _isDiscovering };
        RunCommand = new AsyncCommand(RunAsync, () => _session.IsConnected && !_session.IsSnapshot && !_session.IsWatching && _session.BusyWith is null && !_isScanning && !_isDiscovering, e => Error = Problems.Describe(e));
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => _isScanning);
        ClearSelectionCommand = new RelayCommand(() => Select(null));
        AskClearAllCommand = new RelayCommand(() => ConfirmClearAll = true, () => HasAnythingToClear && !_session.IsSnapshot);
        CancelClearAllCommand = new RelayCommand(() => ConfirmClearAll = false);
        ClearAllCommand = new AsyncCommand(ClearAllAsync, () => _session.IsConnected && !_session.IsSnapshot && !_session.IsWatching && _session.BusyWith is null && !_isScanning, e => Error = Problems.Describe(e));
        DiscoverCommand = new AsyncCommand(DiscoverAsync, () => _session.Backend is IModuleDiscovery && !_session.IsWatching && _session.BusyWith is null && !_isScanning && !_isDiscovering, e => Error = Problems.Describe(e));
        ExportHtmlCommand = new AsyncCommand(() => ExportAsync("html"), () => _session.LastScan is not null);
        ExportMarkdownCommand = new AsyncCommand(() => ExportAsync("md"), () => _session.LastScan is not null);
        CopyForumCommand = new AsyncCommand(CopyForumAsync, () => _session.LastScan is not null);
        ExportJsonCommand = new AsyncCommand(() => ExportAsync("json"), () => _session.LastScan is not null);

        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionState.IsWatching) or nameof(SessionState.BusyWith)) OnUi(() => { RunCommand.RaiseCanExecuteChanged(); ClearAllCommand.RaiseCanExecuteChanged(); DiscoverCommand.RaiseCanExecuteChanged(); Topology.RaiseCanRun(); });
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

    /// <summary>Dash (Check Control) messages from the cluster: active first, then its history.</summary>
    public ObservableCollection<CheckControlItem> DashMessages { get; } = [];
    public bool HasDashMessages => DashMessages.Count > 0;
    public int ActiveDashMessages => DashMessages.Count(m => !m.IsHistory);
    public bool DashRed => DashMessages.Any(m => m.IsRed);
    public bool DashYellow => !DashRed && DashMessages.Any(m => m.IsYellow);
    public bool DashNone => HasDashMessages && !DashRed && !DashYellow;
    public string DashHeading => ActiveDashMessages switch
    {
        0 => "Dash messages: none showing now",
        1 => "1 dash message (Check Control)",
        var n => $"{n} dash messages (Check Control)",
    };

    /// <summary>Shown when the triangle has a reason but the fault memory is empty.</summary>
    public bool DashExplainsTriangle => ActiveDashMessages > 0 && (_session.LastScan?.TotalFaults ?? 0) == 0;
    public string? DashNote => _session.LastScan?.CheckControlNote;
    public bool HasDashNote => !HasDashMessages && DashNote is not null;

    public bool IsConnected => _session.IsConnected;
    public bool NotConnected => !_session.IsConnected;
    public bool HasScan => _session.LastScan is not null;
    public bool HasFaults => Faults.Count > 0;
    public bool NoFaultsAfterScan => HasScan && !HasFaults;

    /// <summary>Faults or Shadow / Info memory entries anywhere: both are cleared, as ISTA does.</summary>
    public bool HasAnythingToClear => _session.LastScan?.Ecus.Any(e => e.Faults.Count > 0) == true;
    public bool BeforeScan => _session.IsConnected && !HasScan && !_isScanning;

    public string Summary
    {
        get
        {
            if (_session.LastScan is not { } s) return "";
            var faults = s.TotalFaults == 1 ? "1 fault" : $"{s.TotalFaults} faults";
            var battery = s.BatteryVoltage is { } v ? $" Battery {v:0.0} V." : "";
            var dash = s.ActiveCheckControl switch { 0 => "", 1 => " 1 dash message.", var n => $" {n} dash messages." };
            // In EDIABAS mode a slow cable setting shows up as modules that don't answer, not as an error.
            var few = _session.Backend is IJobRunner && !_session.IsDemo && !_session.IsSnapshot && s.RespondingEcus < 4 && s.Ecus.Count > 8
                ? " Very few modules answered. In EDIABAS mode that is often the cable's latency timer: set it to 1 ms (Garage, Check cable), then run the quick test again."
                : "";
            return $"{s.RespondingEcus} of {s.Ecus.Count} modules answered in {s.Elapsed.TotalSeconds:0.0} s. {faults}.{dash}{battery}{few}";
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
            Topology.RaiseCanRun();
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
            var lines = new List<string>();
            if (ModuleGlossary.For(ecu) is { } what) lines.Add(what);
            lines.Add($"Address {ecu.AddressHex}");
            lines.Add(status);
            if (!string.IsNullOrWhiteSpace(ident)) lines.Add(ident);
            if (result?.Note is { } note) lines.Add(note);
            if (!ecu.Verified) lines.Add("Address pending validation on this model.");
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

    /// <summary>One line above the car; the full rules are one click away. Writes still show the full warning where they happen.</summary>
    public string FrmShort { get => _frmShort; private set => Set(ref _frmShort, value); }
    private string _frmShort = "";
    public bool FrmFragile => HasFrmNotice && _frmFragile;
    public bool FrmSafe => HasFrmNotice && !_frmFragile;

    private void UpdateFrmNotice(VehicleScanResult scan)
    {
        var frm = scan.Ecus.FirstOrDefault(e => FrmGuard.IsFootwellModule(e.Ecu) && e.Identity is not null);
        if (frm is null) { FrmNotice = null; return; }
        var a = FrmGuard.Assess(frm.Identity);
        // Only a confirmed FRM3 gets the loud banner; an unidentified one still gets the strict rules, said calmly.
        _frmFragile = a.Generation == FrmGeneration.Frm3;
        FrmShort = _frmFragile
            ? $"{a.Label}: extra voltage rules apply before Klemme15 writes to it."
            : $"{a.Label}{(frm.Identity!.PartNumber is { } p ? $", part {p}" : "")}: strict voltage rules apply before writes.";
        FrmNotice = $"{a.Label}{(frm.Identity!.PartNumber is { } pn ? $", part {pn}" : "")}: {a.Advice}";
    }

    public bool ConfirmClearAll
    {
        get => _confirmClearAll;
        set { if (Set(ref _confirmClearAll, value)) Raise(nameof(NotConfirmClearAll)); }
    }

    public bool NotConfirmClearAll => !_confirmClearAll;

    public string ClearAllQuestion =>
        $"Clear the fault memory, and the background entries where the module allows it, of {(_session.LastScan?.Ecus.Count(e => e.Faults.Count > 0) ?? 0)} modules? Read them again after a drive to see what returns. Dash messages clear by themselves once their cause is fixed.";

    /// <summary>Clears every module that reported faults, then runs the quick test again.</summary>
    private async Task ClearAllAsync()
    {
        ConfirmClearAll = false;
        if (_session.Backend is not { } backend || _session.LastScan is not { } scan) return;
        await Topology.StopAndWaitAsync();
        var failed = new List<string>();
        var skipped = new List<string>();
        var clearing = _session.ClaimBus("Clearing faults");
        try
        {
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
                catch (Exception ex) when (ex is DiagnosticException or NotSupportedException or TimeoutException) { failed.Add(e.Ecu.Name); }
            }
        }
        finally { clearing.Dispose(); }
        var before = scan.Ecus.SelectMany(e => e.Faults.Where(f => !f.IsInfo).Select(f => (e.Ecu.Id, f.Code))).ToHashSet();
        await RunAsync();
        ClearResult = AfterClear(before, _session.LastScan);
        var problems = new List<string>();
        if (failed.Count > 0) problems.Add("Couldn't clear: " + string.Join(", ", failed));
        if (skipped.Count > 0) problems.Add("Left alone to protect it: " + string.Join("; ", skipped));
        if (problems.Count > 0) Error = string.Join("\n", problems);
    }

    private string? _clearResult;

    /// <summary>What the quick test after "Clear all" found: faults that came straight back, background entries kept.</summary>
    public string? ClearResult { get => _clearResult; private set { if (Set(ref _clearResult, value)) Raise(nameof(HasClearResult)); } }
    public bool HasClearResult => !string.IsNullOrEmpty(_clearResult);

    internal static string? AfterClear(IReadOnlySet<(string Ecu, string Code)> before, VehicleScanResult? after)
    {
        if (after is null) return null;
        var back = after.Ecus.SelectMany(e => e.Faults.Where(f => !f.IsInfo && before.Contains((e.Ecu.Id, f.Code))).Select(f => $"{e.Ecu.Name}: {f.Code}")).ToList();
        var kept = after.Ecus.Where(e => e.Faults.Any(f => f.IsInfo)).Select(e => e.Ecu.Name).ToList();
        var parts = new List<string>();
        if (before.Count > 0)
            parts.Add(back.Count == 0 ? "Cleared. No fault came straight back." :
                $"Came straight back: {string.Join(", ", back)}. {(back.Count == 1 ? "Its cause is" : "Their causes are")} still there right now, so {(back.Count == 1 ? "it's" : "they're")} worth fixing rather than clearing again.");
        if (kept.Count > 0)
            parts.Add($"Background entries kept by: {string.Join(", ", kept)}. Many modules don't let these be cleared; they never light a warning and are safe to ignore.");
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    /// <summary>
    /// Low voltage makes modules log false faults, which is why dealer tools want a charger connected.
    /// Reads the battery voltage through the engine computer when the car reports it.
    /// </summary>
    /// <summary>Engine values read with the battery check, kept for the health snapshot.</summary>
    private IReadOnlyDictionary<string, double> _engineReadings = new Dictionary<string, double>();

    private async Task<double?> CheckBatteryAsync(IDiagnosticBackend backend, VehicleFamily vehicle)
    {
        _engineReadings = new Dictionary<string, double>();
        var engine = vehicle.Modules.FirstOrDefault(m => m.IsEngine);
        if (engine is null) return null;
        try
        {
            // The standard OBD value first; many BMW engine computers (MSV70 among them) don't answer it, so then
            // BMW's own battery voltage measurement. Engine speed, coolant and fuel trims ride along for the health history.
            var signals = new EngineSignals(backend, engine, _session.Measurements);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var read = await signals.ReadAsync(["voltage"], cts.Token);
            var all = new Dictionary<string, double>(read);
            try
            {
                using var more = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                foreach (var kv in await signals.ReadAsync(["rpm", "coolant", "ltft1", "ltft2"], more.Token)) all[kv.Key] = kv.Value;
            }
            catch (Exception) { /* the extras are optional */ }
            _engineReadings = all;
            double? v = read.TryGetValue("voltage", out var volts) ? volts : null;
            _session.Voltage = v;
            Warning = v switch
            {
                < 12.0 => $"Battery at {v:0.0} V. Low voltage makes modules log faults that aren't real. Charge the battery or connect a charger, then run the quick test again."
                          + (FrmGuard.FamiliesWithFrm.Contains(vehicle.Id)
                              ? " Don't clear faults, code or run service functions until it's charged: the footwell module (FRM) on these cars can corrupt itself if the voltage dips while it writes."
                              : ""),
                < 12.4 => $"Battery at {v:0.0} V with the engine off. While Klemme15 is connected the car stays awake and draws 10-20 A, so keep this short, start the engine, or connect a charger. Switch the ignition off when you're done.",
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
        await Topology.StopAndWaitAsync();
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
            Topology.RaiseCanRun();
            DiscoverCommand.RaiseCanExecuteChanged();
            RunCommand.RaiseCanExecuteChanged();
        }
    }
    public RelayCommand CancelCommand { get; }
    public AsyncCommand ExportHtmlCommand { get; }
    public AsyncCommand ExportMarkdownCommand { get; }
    public AsyncCommand ExportJsonCommand { get; }

    /// <summary>Live values pinned to the car drawing.</summary>
    public TopologyOverlayViewModel Topology { get; }

    public override void OnHidden() => Topology.Stop();

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
        await Topology.StopAndWaitAsync();

        Error = null;
        Warning = null;
        ClearResult = null;
        Select(null);
        _session.PublishScan(null);
        // The quick test holds the cable: live data, tests and service functions on other pages wait for it.
        using var bus = _session.ClaimBus("The quick test");
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
            var final = scan with { BatteryVoltage = battery };
            if (!scan.Cancelled && !_session.IsDemo && scan.RespondingEcus == 0)
                Warning = CableCheck.NoAnswerHelp(dcan: vehicle.Cable != "KLine");
            _session.RecordHealth(final, _engineReadings);
            _session.PublishScan(_session.WithHealth(final));
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
        DashMessages.Clear();
        _moduleMarkers.Clear();
        var cc = _session.LastScan?.CheckControl ?? [];
        foreach (var m in cc.Where(m => m.Active).Concat(cc.Where(m => !m.Active).Take(8))) DashMessages.Add(new CheckControlItem(m));

        if (_session.Vehicle is { } vehicle)
        {
            foreach (var ecu in vehicle.Modules.Where(m => m.Position is not null))
            {
                var m = new MarkerViewModel(MarkerKind.Module, ecu.Id, ecu.Id.Split('.').Last(), ecu.Position!.Value, Select)
                {
                    Ecu = ecu,
                    Detail = ModuleGlossary.Describe(ecu),
                };
                _moduleMarkers[ecu.Id] = m;
            }
            MarkerLayout.SpreadModules(_moduleMarkers.Values.ToList());
            foreach (var m in _moduleMarkers.Values) Markers.Add(m);
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
                s.Label = string.Join("  ", s.Faults.Select(f => f.Code).Distinct());
            MarkerLayout.CombineCrowdedLabels(sources.Values.ToList(), Markers.Where(m => m.IsModule).ToList());
            foreach (var s in sources.Values)
                Markers.Add(s);   // fault sources drawn above modules
        }

        // Active faults first, then by module.
        var ordered = Faults.OrderByDescending(f => f.IsActive).ThenBy(f => f.ModuleName).ToList();
        Faults.Clear();
        foreach (var f in ordered) Faults.Add(f);

        foreach (var p in new[] { nameof(HasScan), nameof(HasFaults), nameof(NoFaultsAfterScan), nameof(BeforeScan), nameof(Summary), nameof(ClearAllQuestion),
                                  nameof(HasDashMessages), nameof(ActiveDashMessages), nameof(DashRed), nameof(DashYellow), nameof(DashNone), nameof(DashHeading),
                                  nameof(DashExplainsTriangle), nameof(DashNote), nameof(HasDashNote) })
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
