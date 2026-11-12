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

/// <summary>One requirement in the check list. Unknown ones can be confirmed by hand.</summary>
public sealed class ServiceCheckViewModel(CheckResult result, Action changed) : ViewModelBase
{
    private bool _confirmed;
    public CheckResult Result { get; } = result;
    public string Text => Result.Text;
    public string Detail => Result.Detail ?? "";
    public bool HasDetail => Result.Detail is not null;
    public bool Passed => Result.State == CheckState.Pass;
    public bool Failed => Result.State == CheckState.Fail;
    public bool Unknown => Result.State == CheckState.Unknown;
    public string Mark => Result.State switch { CheckState.Pass => "OK", CheckState.Fail => "NO", _ => "?" };
    public bool Confirmed { get => _confirmed; set { if (Set(ref _confirmed, value)) changed(); } }
    public bool Satisfied => Passed || (Unknown && _confirmed);
}

/// <summary>Something the user must tick before running ("The selector is in P").</summary>
public sealed class ServiceConfirmViewModel(string text, Action changed) : ViewModelBase
{
    private bool _ticked;
    public string Text { get; } = text;
    public bool Ticked { get => _ticked; set { if (Set(ref _ticked, value)) changed(); } }
}

/// <summary>A choice or typed value the function needs (service item, cylinder, injector code).</summary>
public sealed class ServiceInputViewModel : ViewModelBase
{
    private readonly Action _changed;
    private ServiceChoice? _selected;
    private string _text = "";

    public ServiceInputViewModel(ServiceInput input, IReadOnlyList<ServiceChoice> choices, Action changed)
    {
        Input = input;
        Choices = choices;
        _changed = changed;
        _selected = choices.FirstOrDefault();
    }

    public ServiceInput Input { get; }
    public string Label => Input.Label;
    public bool IsChoice => Input.IsChoice;
    public bool IsText => !Input.IsChoice;
    public string Hint => Input.Hint ?? "";
    public IReadOnlyList<ServiceChoice> Choices { get; }
    public ServiceChoice? Selected { get => _selected; set { if (Set(ref _selected, value)) _changed(); } }
    public string Text { get => _text; set { if (Set(ref _text, value ?? "")) _changed(); } }
    public string? Value => IsChoice ? _selected?.Value : _text.Trim();
}

/// <summary>A service function in the list, with whether it can run on this car.</summary>
public sealed class ServiceItemViewModel(ServiceFunction function) : ViewModelBase
{
    private bool _isSelected;
    private ServiceAvailability _availability = new(false, "");
    public ServiceFunction Function { get; } = function;
    public string Title => Function.Title;
    public string Category => Function.Category;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public ServiceAvailability Availability { get => _availability; set { Set(ref _availability, value); Raise(nameof(IsAvailable)); Raise(nameof(Badge)); Raise(nameof(Opacity)); } }
    public double Opacity => _availability.Available ? 1 : 0.55;
    public bool IsAvailable => _availability.Available;
    public string Badge => _availability.Available ? "" : "not available";
    public RelayCommand? SelectCommand { get; set; }
}

/// <summary>One line of the "What's due" list.</summary>
public sealed class ServiceDueRowViewModel(ServiceDueItem item)
{
    public string Name => item.Name;
    public string LevelText => item.LevelText;
    public string Summary => item.Summary();
    public bool IsDue => item.Level == ServiceDueLevel.Due;
    public bool IsSoon => item.Level == ServiceDueLevel.Soon;
    public bool IsOk => item.Level == ServiceDueLevel.Ok;
    public bool IsUnknown => item.Level == ServiceDueLevel.Unknown;
}

public sealed class ServiceGroupViewModel(string name, IReadOnlyList<ServiceItemViewModel> items)
{
    public string Name { get; } = name;
    public IReadOnlyList<ServiceItemViewModel> Items { get; } = items;
}

/// <summary>
/// Service: things that change something in the car (battery registration, calibrations, adaptation resets,
/// component tests). Each function shows what it does, checks the car's state, asks the user to confirm, runs
/// once and reports what the module answered. Unverified functions need the experimental switch.
/// </summary>
public sealed class ServiceViewModel : PageViewModel
{
    private readonly SessionState _session;
    private readonly AppSettings _settings;
    private readonly List<ServiceItemViewModel> _items;
    private ServiceItemViewModel? _selected;
    private bool _checking, _running, _checked;
    private string _status = "";
    private string? _result;
    private bool _resultOk;
    private CancellationTokenSource? _cts;

    public ServiceViewModel(SessionState session, AppSettings settings)
    {
        _session = session;
        _settings = settings;
        _items = _session.Database.Service.All.Select(f => new ServiceItemViewModel(f)).ToList();
        foreach (var item in _items) item.SelectCommand = new RelayCommand(() => Select(item));
        RebuildGroups();

        CheckCommand = new AsyncCommand(CheckAsync, () => _selected is { IsAvailable: true } && !_checking && !_running);
        RunCommand = new AsyncCommand(RunAsync, CanRun, e => { Result = $"Didn't work: {Problems.Describe(e)}"; ResultOk = false; Status = "Nothing is running now. The traffic log (Log page) has the details."; });
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => _running);
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionState.Backend)) OnUi(Refresh);
            if (e.PropertyName is nameof(SessionState.BusyWith) or nameof(SessionState.IsWatching)) OnUi(RunCommand.RaiseCanExecuteChanged);
        };
        _session.ScanUpdated += () => OnUi(Refresh);
        DueCommand = new AsyncCommand(ReadDueAsync, () => !_readingDue && !_running,
            e => { DueHeadline = "Couldn't read what's due."; DueStatus = Problems.Describe(e); });
        CloseDueCommand = new RelayCommand(() => ShowingDue = false);
        Select(_items.FirstOrDefault());
    }

    public override string Title => "Service";
    public override Geometry Icon => Icons.Service;

    public ObservableCollection<ServiceGroupViewModel> Groups { get; } = [];
    public ObservableCollection<ServiceCheckViewModel> Checks { get; } = [];
    public ObservableCollection<ServiceConfirmViewModel> Confirms { get; } = [];
    public ObservableCollection<ServiceInputViewModel> Inputs { get; } = [];
    public bool HasInputs => Inputs.Count > 0;

    /// <summary>Why the choices can't be used yet ("Choose or enter: ..."), or empty.</summary>
    public string InputProblem => _selected is { } item && Inputs.Count > 0
        ? ServiceRunner.ValidateInputs(item.Function, item.Availability.Mode == ServiceMode.Demo ? null : item.Availability.Implementation, InputValues()) ?? ""
        : "";
    public bool HasInputProblem => InputProblem.Length > 0;
    public string Duration => _selected?.Function.Duration is { } d ? $"Takes: {d}" : "";
    public bool HasDuration => _selected?.Function.Duration is not null;

    private Dictionary<string, string> InputValues() =>
        Inputs.Where(i => i.Value is not null).ToDictionary(i => i.Input.Id, i => i.Value!);

    private string _filter = "";
    private bool _onlyMine = true;

    /// <summary>Searches titles, descriptions and categories ("brake", "oil", "window").</summary>
    public string Filter { get => _filter; set { if (Set(ref _filter, value ?? "")) RebuildGroups(); } }

    /// <summary>Hide functions this car can't run (when connected).</summary>
    public bool OnlyMine { get => _onlyMine; set { if (Set(ref _onlyMine, value)) RebuildGroups(); } }

    public string CountText
    {
        get
        {
            var available = _items.Count(i => i.IsAvailable);
            return _session.IsConnected ? $"{available} of {_items.Count} functions available for this car" : $"{_items.Count} functions";
        }
    }

    private void RebuildGroups()
    {
        Groups.Clear();
        var words = _filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var visible = _items.Where(i =>
                (!_onlyMine || !_session.IsConnected || i.IsAvailable)
                && words.All(w => i.Title.Contains(w, StringComparison.OrdinalIgnoreCase)
                                  || i.Function.Summary.Contains(w, StringComparison.OrdinalIgnoreCase)
                                  || i.Function.When.Contains(w, StringComparison.OrdinalIgnoreCase)
                                  || i.Category.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        foreach (var category in visible.Select(i => i.Category).Distinct())
            Groups.Add(new ServiceGroupViewModel(category, visible.Where(i => i.Category == category).ToList()));
        Raise(nameof(CountText));
        Raise(nameof(NothingShown));
        Raise(nameof(NothingText));
    }

    public bool NothingShown => Groups.Count == 0;
    public string NothingText => _filter.Trim().Length > 0
        ? $"Nothing matches \"{_filter.Trim()}\". Clear the search{(_onlyMine ? " or untick 'Only my car'" : "")}."
        : "None of the functions fits the modules that answered in your quick test. Untick 'Only my car' to see them all.";

    public bool NotConnected => !_session.IsConnected;
    public bool HasScan => _session.LastScan is not null;
    public bool NeedsScan => _session.IsConnected && _session.LastScan is null;

    /// <summary>Unverified functions only run with this switched on (saved in the settings).</summary>
    public bool Experimental
    {
        get => _settings.ExperimentalService;
        set
        {
            if (_settings.ExperimentalService == value) return;
            _settings.ExperimentalService = value;
            _settings.Save();
            Raise();
            RaiseSelection();
        }
    }

    // ---------------------------------------------------------------- what's due (read-only)

    private bool _showingDue, _readingDue;
    private string _dueHeadline = "", _dueStatus = "";

    /// <summary>Reads what the cluster says is due for service. Read-only.</summary>
    public AsyncCommand DueCommand { get; }
    public RelayCommand CloseDueCommand { get; }
    public ObservableCollection<ServiceDueRowViewModel> DueItems { get; } = [];
    public bool ShowingDue { get => _showingDue; private set { if (Set(ref _showingDue, value)) Raise(nameof(ShowFunction)); } }
    public bool ShowFunction => HasSelection && !_showingDue;
    public string DueHeadline { get => _dueHeadline; private set => Set(ref _dueHeadline, value); }
    public string DueStatus { get => _dueStatus; private set { if (Set(ref _dueStatus, value)) Raise(nameof(HasDueStatus)); } }
    public bool HasDueStatus => _dueStatus.Length > 0;

    private async Task ReadDueAsync()
    {
        ShowingDue = true;
        DueItems.Clear();
        DueStatus = "";
        var cluster = _session.Vehicle?.Modules.FirstOrDefault(ServiceDue.IsCluster);
        if (_session.IsSnapshot) { DueHeadline = "A snapshot can't be asked what's due."; DueStatus = "Connect to the car itself to read its service list."; return; }
        if (!_session.IsConnected) { DueHeadline = "Connect to your car first."; DueStatus = "Connect in the Garage, then come back here."; return; }
        if (cluster is null) { DueHeadline = "This car doesn't keep a service list Klemme15 can read."; return; }
        IReadOnlyList<ServiceDueItem> items;
        if (_session.IsDemo) items = DemoDue();
        else if (_session.Backend is IJobRunner jobs)
        {
            _readingDue = true;
            DueCommand.RaiseCanExecuteChanged();
            DueHeadline = "Reading the instrument cluster's service list...";
            try
            {
                using var bus = _session.ClaimBus("Reading what's due for service");
                // The cluster keeps some items; the engine computer (oil), stability control (brake pads) and climate
                // control (microfilter) keep the rest. Ask each one that answered the quick test.
                var answered = _session.LastScan?.Ecus.Where(e => e.Status is EcuScanStatus.Ok or EcuScanStatus.Faults).Select(e => e.Ecu.Id).ToHashSet();
                var keepers = _session.Vehicle!.Modules.Where(ServiceDue.KeepsServiceItems)
                    .Where(m => answered is null || answered.Contains(m.Id) || ServiceDue.IsCluster(m))
                    .OrderByDescending(ServiceDue.IsCluster)
                    .Select(m => (m, _session.Readings?.VariantOf(m) ?? m.Sgbd.FirstOrDefault() ?? $"D_{m.Address:X4}"));
                items = await ServiceDue.ReadAllAsync(jobs, keepers);
                if (items.Count == 0) throw new DiagnosticException("No module reported a service item.");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                DueHeadline = "The instrument cluster didn't give a service list.";
                DueStatus = "Cars from before Condition Based Service (E46, E39, E53 and other older models) show their service interval on the cluster only. " + Problems.Describe(e);
                return;
            }
            finally
            {
                _readingDue = false;
                DueCommand.RaiseCanExecuteChanged();
            }
        }
        else
        {
            DueHeadline = "Reading what's due needs EDIABAS mode.";
            DueStatus = "The service list is read with BMW's own job from your BMW files. In the Garage's cable panel, tick 'Use my own BMW files (EDIABAS mode)' and connect again. Reading it without BMW files is in development.";
            return;
        }
        foreach (var i in items) DueItems.Add(new ServiceDueRowViewModel(i));
        DueHeadline = ServiceDue.Headline(items);
        DueStatus = _session.IsDemo ? "Demo car: the figures are made up."
            : "Read from the instrument cluster and the modules that keep the other items (engine oil, brake pads, microfilter); nothing was changed. Compare it with the service menu on the cluster, and reset an item with the service functions on the left after the work is done.";
    }

    private static IReadOnlyList<ServiceDueItem> DemoDue() => ServiceDue.Parse(
    [
        DemoItem("Oel", 8, 2100, "km", 6), DemoItem("Brfl", 0, 0, "", 3, 9, 26), DemoItem("Br_v", 46, 21000, "km", 2),
        DemoItem("Br_h", 71, 38000, "km", 1), DemoItem("Filt", 54, 16000, "km", 4), DemoItem("ZKrz", 33, 31000, "km", 1), DemoItem("Sic", 60, 0, "", 2, 5, 28),
    ]);

    private static Dictionary<string, string> DemoItem(string key, int percent, long remaining, string unit, int resets, int month = 0, int year = 0) => new()
    {
        ["ID_FN_CBS_MESS_TEXT"] = key, ["AVAI_CBS_WERT"] = $"{percent}", ["RMMI_CBS_WERT"] = $"{(unit == "km" ? remaining / 10 : -32768)}", ["ST_UN_CBS_WERT"] = "1",
        ["COU_RSTG_CBS_MESS_WERT"] = $"{resets}", ["ZIEL_MM_WERT"] = $"{month}", ["ZIEL_YY_WERT"] = $"{year}",
    };

    public ServiceItemViewModel? Selected => _selected;
    public bool HasSelection => _selected is not null;
    public string FunctionTitle => _selected?.Title ?? "";
    public string Summary => _selected?.Function.Summary ?? "";
    public string When => _selected?.Function.When ?? "";
    public string ModuleText => _selected is null ? "" : $"Module: {ModuleOf(_selected.Function)?.Name ?? _selected.Function.Module}";
    public string RiskText => _selected?.Function.Risk switch
    {
        ServiceRisk.Low => "Low risk: it only switches a part on briefly or tells a module something.",
        ServiceRisk.Medium => "Medium risk: the car drives differently until it relearns.",
        ServiceRisk.High => "High risk: done wrongly, safety systems can misbehave. Follow the steps exactly.",
        _ => "",
    };
    public bool IsHighRisk => _selected?.Function.Risk == ServiceRisk.High;
    public bool Unverified => _selected is { Function.Verified: false };

    /// <summary>What "experimental" means for the selected function on this connection.</summary>
    public string UnverifiedText => _selected?.Availability.Mode switch
    {
        ServiceMode.Native => "Experimental, pending field validation. Klemme15 sends the documented request for your module version directly.",
        ServiceMode.Ediabas => "Experimental, pending field validation. Runs BMW's own job from your BMW files.",
        _ => "Experimental, pending field validation.",
    };
    public string ConfirmedText => _selected?.Function.Confirmations is { Count: > 0 } c ? "Validated on: " + string.Join("; ", c) : "";
    public bool HasConfirmed => ConfirmedText.Length > 0;
    public string Caution => string.Join("\n\n", new[] { _selected?.Function.Caution, FrmCaution }.Where(x => x is not null));
    public bool HasCaution => Caution.Length > 0;

    /// <summary>The footwell module's own warning, when this function writes to it.</summary>
    private string? FrmCaution =>
        _selected is { } item && ModuleOf(item.Function) is { } ecu && FrmGuard.IsFootwellModule(ecu)
            ? $"{FrmGuard.Assess(IdentityOf(ecu)).Label}: {FrmGuard.Assess(IdentityOf(ecu)).Advice}"
            : null;

    private EcuIdentity? IdentityOf(EcuDefinition ecu) => _session.LastScan?.Ecus.FirstOrDefault(e => e.Ecu.Id == ecu.Id)?.Identity;
    public IReadOnlyList<string> Before => _selected?.Function.Before ?? [];
    public IReadOnlyList<string> After => _selected?.Function.After ?? [];
    public bool HasBefore => Before.Count > 0;
    public bool HasAfter => After.Count > 0;
    public string AvailabilityText => _selected?.Availability.Reason ?? "";
    public bool IsAvailable => _selected?.IsAvailable ?? false;
    public bool Blocked => _selected is not null && !ServiceRunner.Allowed(_selected.Function, Experimental);
    public bool HasChecks => _checked;

    public string Status { get => _status; private set => Set(ref _status, value); }
    public string? Result { get => _result; private set { Set(ref _result, value); Raise(nameof(HasResult)); } }
    public bool HasResult => _result is not null;
    public bool ResultOk { get => _resultOk; private set { Set(ref _resultOk, value); Raise(nameof(ResultFailed)); } }
    public bool ResultFailed => HasResult && !_resultOk;
    public ObservableCollection<string> ResultDetails { get; } = [];

    public AsyncCommand CheckCommand { get; }
    public AsyncCommand RunCommand { get; }
    public RelayCommand CancelCommand { get; }

    public override void OnShown() => Refresh();

    private EcuDefinition? ModuleOf(ServiceFunction f) =>
        _session.Vehicle is { } v ? ServiceRunner.ModuleFor(f, v.Modules) : null;

    private void Refresh()
    {
        foreach (var item in _items) item.Availability = AvailabilityOf(item.Function);
        RebuildGroups();
        Raise(nameof(NotConnected));
        Raise(nameof(HasScan));
        Raise(nameof(NeedsScan));
        ResetChecks();
        RaiseSelection();
    }

    private ServiceAvailability AvailabilityOf(ServiceFunction f)
    {
        if (_session.IsSnapshot)
            return new(false, "This is a snapshot: service functions need the car itself. Close the snapshot and connect in the Garage.");
        if (!_session.IsConnected || _session.Backend is not { } backend)
            return new(false, "Connect to your car in the Garage first.");
        var ecu = ModuleOf(f);
        var scan = _session.LastScan;
        // The demo car only has the modules its quick test shows (before the first quick test, assume it has them all).
        var answered = ecu is not null && ((_session.IsDemo && scan is null)
            || (scan?.Ecus.Any(r => r.Ecu.Id == ecu.Id && r.Status is EcuScanStatus.Ok or EcuScanStatus.Faults) ?? false));
        var variant = ecu is null ? null : _session.ServiceVariant(ecu, f);
        return ServiceRunner.Availability(f, ecu, variant, answered, backend is IJobRunner, backend is IRawRequests, _session.IsDemo,
            _session.Model?.IsDiesel);
    }

    private void Select(ServiceItemViewModel? item)
    {
        if (_running) return;
        foreach (var i in _items) i.IsSelected = i == item;
        _selected = item;
        ShowingDue = false;
        ResetChecks();
        RaiseSelection();
    }

    private void ResetChecks()
    {
        _checked = false;
        Checks.Clear();
        Confirms.Clear();
        Inputs.Clear();
        Result = null;
        ResultDetails.Clear();
        if (_selected is { } item)
        {
            foreach (var c in item.Function.Checks.Confirm)
                Confirms.Add(new ServiceConfirmViewModel(c, Changed));
            // Only offer the choices this car's module version supports.
            var impl = item.Availability.Mode == ServiceMode.Demo ? null : item.Availability.Implementation;
            foreach (var input in item.Function.Inputs)
                Inputs.Add(new ServiceInputViewModel(input,
                    input.Choices.Where(c => impl is null || impl.Supports(input.Id, c.Value)).ToList(), Changed));
        }
        Raise(nameof(HasInputs));
        Status = "";
    }

    private void RaiseSelection()
    {
        foreach (var p in new[] { nameof(Selected), nameof(HasSelection), nameof(ShowFunction), nameof(FunctionTitle), nameof(Summary), nameof(When), nameof(ModuleText),
                     nameof(RiskText), nameof(IsHighRisk), nameof(Unverified), nameof(UnverifiedText), nameof(ConfirmedText), nameof(HasConfirmed), nameof(Caution), nameof(HasCaution), nameof(Before), nameof(After), nameof(HasBefore), nameof(HasAfter),
                     nameof(AvailabilityText), nameof(IsAvailable), nameof(Blocked), nameof(HasChecks), nameof(Duration), nameof(HasDuration) })
            Raise(p);
        Changed();
    }

    private void Changed()
    {
        Raise(nameof(InputProblem));
        Raise(nameof(HasInputProblem));
        CheckCommand.RaiseCanExecuteChanged();
        RunCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }

    private bool CanRun() =>
        _selected is { IsAvailable: true } item && !_running && !_checking && _checked && !_session.IsWatching && _session.BusyWith is null
        && ServiceRunner.Allowed(item.Function, Experimental)
        && Checks.All(c => c.Satisfied) && Confirms.All(c => c.Ticked) && !HasInputProblem;

    private async Task CheckAsync()
    {
        if (_selected is not { } item || _session.Backend is not { } backend) return;
        _checking = true;
        Changed();
        Status = "Checking the car...";
        try
        {
            var state = _session.IsDemo
                ? ServiceRunner.DemoState(item.Function.Checks)
                : await ServiceRunner.ReadStateAsync(backend, _session.Vehicle?.Modules.FirstOrDefault(m => m.IsEngine), _session.Measurements);
            Checks.Clear();
            var checks = item.Function.Checks;
            // A fragile footwell module raises the voltage floor (FRM3 rule).
            if (ModuleOf(item.Function) is { } target && WriteGuard.FloorFor(target, IdentityOf(target), WriteKind.ServiceFunction) is { } floor
                && (checks.MinVoltage is null || checks.MinVoltage < floor))
                checks = checks with { MinVoltage = floor };
            foreach (var c in ServiceRunner.Evaluate(checks, state))
                Checks.Add(new ServiceCheckViewModel(c, Changed));
            _checked = true;
            Raise(nameof(HasChecks));
            Status = Checks.Any(c => c.Failed) ? "Fix the items marked NO, then check again."
                : Checks.Any(c => c.Unknown) ? "Confirm the items Klemme15 couldn't measure, tick the boxes, then run."
                : "All checks passed. Tick the boxes, then run.";
        }
        catch (Exception e)
        {
            Status = $"Couldn't check the car: {e.Message}";
        }
        finally
        {
            _checking = false;
            Changed();
        }
    }

    private async Task RunAsync()
    {
        if (_selected is not { } item || _session.Backend is not { } backend || ModuleOf(item.Function) is not { } ecu) return;
        _running = true;
        _cts = new CancellationTokenSource();
        Changed();
        Result = null;
        ResultDetails.Clear();
        Status = $"Running '{item.Title}'. Don't switch off the ignition or unplug the cable.";
        var cutOff = false;
        using var job = _session.BeginOperation(new RunningOperation
        {
            Kind = OperationKind.ServiceFunction, Title = item.Title, ModuleId = ecu.Id, ModuleName = ecu.Name,
            Car = $"{_session.Vehicle?.Id} {_session.Model?.Name}".Trim(), Started = DateTimeOffset.Now,
        });
        try
        {
            if (!_session.IsDemo)
            {
                var guard = await WriteGuard.CheckAsync(backend, ecu, IdentityOf(ecu), WriteKind.ServiceFunction,
                    _session.Vehicle?.Modules.FirstOrDefault(m => m.IsEngine), _session.Measurements, _cts.Token);
                if (!guard.Allowed)
                {
                    ResultOk = false;
                    Result = $"Not started: {guard.Reason}";
                    Status = "Nothing was sent to the module.";
                    return;
                }
            }
            var progress = new Progress<string>(text => Status = text);
            var outcome = await ServiceRunner.RunAsync(item.Function, item.Availability, backend, ecu, InputValues(), progress, _session.Log, _cts.Token);
            ResultOk = outcome.Success;
            cutOff = outcome.CutOff;
            foreach (var d in outcome.Details) ResultDetails.Add(d);
            Result = outcome.Success ? outcome.Message : $"Didn't work: {outcome.Message}";
            Status = outcome.Success ? "Finished. Follow the steps under 'Afterwards'." : "Stopped. Nothing is running now; the traffic log (Log page) has the details.";
        }
        catch (OperationCanceledException)
        {
            ResultOk = false;
            Result = "Stopped. The module takes back control of anything it was driving.";
            Status = "";
        }
        catch (Exception e) when (e is DiagnosticException or TimeoutException && ServiceOutcome.IsCutOff(e))
        {
            cutOff = true;
            ResultOk = false;
            Result = $"Cut off: {e.Message}";
        }
        finally
        {
            if (cutOff)
            {
                _session.OperationCutOff();
                Status = "The connection was lost mid-job. The message at the top says what to do now.";
            }
            else _session.EndOperation();
            _running = false;
            _cts.Dispose();
            _cts = null;
            // Each run needs fresh checks and confirmations.
            _checked = false;
            Raise(nameof(HasChecks));
            foreach (var c in Confirms) c.Ticked = false;
            Changed();
        }
    }
}
