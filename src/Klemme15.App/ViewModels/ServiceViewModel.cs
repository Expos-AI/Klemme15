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
        RunCommand = new AsyncCommand(RunAsync, CanRun);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => _running);
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionState.Backend)) OnUi(Refresh);
        };
        _session.ScanUpdated += () => OnUi(Refresh);
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
    }

    public bool NothingShown => Groups.Count == 0;

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
    public string Caution => _selected?.Function.Caution ?? "";
    public bool HasCaution => _selected?.Function.Caution is not null;
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
        if (!_session.IsConnected || _session.Backend is not { } backend)
            return new(false, "Connect to your car in the Garage first.");
        var ecu = ModuleOf(f);
        var scan = _session.LastScan;
        var answered = ecu is not null && (_session.IsDemo
            || (scan?.Ecus.Any(r => r.Ecu.Id == ecu.Id && r.Status is EcuScanStatus.Ok or EcuScanStatus.Faults) ?? false));
        var variant = ecu is null ? null : _session.ServiceVariant(ecu, f);
        return ServiceRunner.Availability(f, ecu, variant, answered, backend is IJobRunner, backend is IRawRequests, _session.IsDemo);
    }

    private void Select(ServiceItemViewModel? item)
    {
        if (_running) return;
        foreach (var i in _items) i.IsSelected = i == item;
        _selected = item;
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
        foreach (var p in new[] { nameof(Selected), nameof(HasSelection), nameof(FunctionTitle), nameof(Summary), nameof(When), nameof(ModuleText),
                     nameof(RiskText), nameof(IsHighRisk), nameof(Unverified), nameof(Caution), nameof(HasCaution), nameof(Before), nameof(After), nameof(HasBefore), nameof(HasAfter),
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
        _selected is { IsAvailable: true } item && !_running && !_checking && _checked
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
                : await ServiceRunner.ReadStateAsync(backend, _session.Vehicle?.Modules.FirstOrDefault(m => m.IsEngine));
            Checks.Clear();
            foreach (var c in ServiceRunner.Evaluate(item.Function.Checks, state))
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
        try
        {
            var progress = new Progress<string>(text => Status = text);
            var outcome = await ServiceRunner.RunAsync(item.Function, item.Availability, backend, ecu, InputValues(), progress, _session.Log, _cts.Token);
            ResultOk = outcome.Success;
            foreach (var d in outcome.Details) ResultDetails.Add(d);
            Result = outcome.Success ? outcome.Message : $"Didn't work: {outcome.Message}";
            Status = outcome.Success ? "Finished. Follow the steps under 'Afterwards'." : "Stopped. The part was handed back to the module; the traffic log has the details.";
        }
        catch (OperationCanceledException)
        {
            ResultOk = false;
            Result = "Stopped. The part was handed back to the module.";
            Status = "";
        }
        finally
        {
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
