using System.Collections.ObjectModel;
using Avalonia.Media;
using Klemme15.Core;
using Klemme15.Data;

namespace Klemme15.App.ViewModels;

public sealed class TestFindingViewModel(TestFinding finding)
{
    public string Title { get; } = finding.Title;
    public string Explanation { get; } = finding.Explanation;
    public bool Ok => finding.Severity == TestSeverity.Ok;
    public bool Watch => finding.Severity == TestSeverity.Watch;
    public bool Problem => finding.Severity == TestSeverity.Problem;
    public bool Unknown => finding.Severity == TestSeverity.Unknown;
    public string Mark => finding.Severity switch { TestSeverity.Ok => "OK", TestSeverity.Watch => "!", TestSeverity.Problem => "!!", _ => "?" };
}

/// <summary>One number from a test, with a bar scaled against the largest value.</summary>
public sealed class TestValueViewModel(string label, double value, string unit, double max)
{
    public string Label { get; } = label;
    public string Text { get; } = $"{(unit == "%" && value > 0 ? "+" : "")}{value:0.0#}{(unit.Length > 0 ? " " + unit : "")}";
    public double BarWidth { get; } = max <= 0 ? 0 : Math.Max(2, 260 * Math.Abs(value) / max);
}

public sealed class GuidedTestViewModel(GuidedTestInfo info) : ViewModelBase
{
    private bool _isSelected;
    public GuidedTestInfo Info { get; } = info;
    public string Title => Info.Title;
    public bool DrivesParts => Info.DrivesParts;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public RelayCommand? SelectCommand { get; set; }
}

public sealed class GuideItemViewModel(Guide guide) : ViewModelBase
{
    private bool _isSelected;
    public Guide Guide { get; } = guide;
    public string Title => Guide.Title;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public RelayCommand? SelectCommand { get; set; }
}

/// <summary>
/// Guided tests: Klemme15 measures the engine while telling you what to do (idle, hold 2,500 rpm, blip the throttle)
/// and explains what the numbers mean. Read-only except the tests marked "drives a part", which switch one part on through the engine computer.
/// </summary>
public sealed class TestsViewModel : PageViewModel
{
    private readonly SessionState _session;
    private GuidedTestViewModel? _selected;
    private CancellationTokenSource? _cts;
    private bool _running;
    private string _instruction = "", _status = "";
    private double _fraction;

    private readonly Action<string> _openUrl;
    private GuideViewModel? _guide;

    public TestsViewModel(SessionState session, Action<string>? openUrl = null)
    {
        _session = session;
        _openUrl = openUrl ?? (_ => { });
        foreach (var g in session.Database.Guides.All.OrderBy(g => g.ReferenceOnly ? 0 : 1).ThenBy(g => g.Title).Concat(session.Database.Checks.All.OrderBy(g => g.Title)))
        {
            var item = new GuideItemViewModel(g);
            item.SelectCommand = new RelayCommand(() => SelectGuide(item));
            Guides.Add(item);
        }
        foreach (var t in GuidedTests.All)
        {
            var vm = new GuidedTestViewModel(t);
            vm.SelectCommand = new RelayCommand(() => Select(vm));
            Tests.Add(vm);
        }
        StartCommand = new AsyncCommand(RunAsync, () => !_running && _selected is not null && _session.IsConnected && !_session.IsSnapshot && Engine is not null);
        StopCommand = new RelayCommand(() => _cts?.Cancel(), () => _running);
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionState.Backend)) OnUi(() => { Raise(nameof(NotConnected)); Changed(); if (_selected is { } t) Select(t); });
        };
        Select(Tests[0]);
    }

    public override string Title => "Tests";
    public override Geometry Icon => Icons.Tests;

    public ObservableCollection<GuidedTestViewModel> Tests { get; } = [];
    public ObservableCollection<TestFindingViewModel> Findings { get; } = [];
    public ObservableCollection<TestValueViewModel> Values { get; } = [];
    public ObservableCollection<GuideItemViewModel> Guides { get; } = [];

    /// <summary>The test's graphs, live while it runs and kept afterwards.</summary>
    public ObservableCollection<ChartViewModel> Charts { get; } = [];
    public bool HasCharts => Charts.Count > 0;

    private void ShowTraces(IReadOnlyList<TestTrace>? traces)
    {
        if (traces is null) return;
        Charts.Clear();
        foreach (var c in ChartViewModel.From(traces)) Charts.Add(c);
        Raise(nameof(HasCharts));
    }

    private string _savedNote = "";
    /// <summary>Whether the result went into the car's health history, and if not, why.</summary>
    public string SavedNote { get => _savedNote; private set { if (Set(ref _savedNote, value)) Raise(nameof(HasSavedNote)); } }
    public bool HasSavedNote => _savedNote.Length > 0;

    public bool DrivesParts => _selected?.DrivesParts == true;

    /// <summary>The how-to guide being read, when one is picked instead of a test.</summary>
    public GuideViewModel? Guide
    {
        get => _guide;
        private set { if (Set(ref _guide, value)) { Raise(nameof(ShowingGuide)); Raise(nameof(ShowingTest)); } }
    }

    public bool ShowingGuide => _guide is not null;
    public bool ShowingTest => _guide is null;

    private void SelectGuide(GuideItemViewModel item)
    {
        if (_running) return;
        foreach (var t in Tests) t.IsSelected = false;
        foreach (var g in Guides) g.IsSelected = g == item;
        var steps = item.Guide.StepsFor(_session.Model?.EngineCode).Select((s, i) => new GuideStepViewModel(i + 1, s.Text, null, null)).ToList();
        Guide = new GuideViewModel(item.Guide.Title, item.Guide.Summary, item.Guide.Safety, steps, item.Guide.SourceList.Select(u => new SourceLink(u, _openUrl)).ToList());
    }

    public bool NotConnected => !_session.IsConnected;
    private EcuDefinition? Engine => _session.Vehicle?.Modules.FirstOrDefault(m => m.IsEngine);

    public string TestTitle => _selected?.Title ?? "";
    public string Summary => _selected?.Info.Summary ?? "";
    public string Requirements => _selected?.Info.Requirements ?? "";
    public string Duration => _selected is null ? "" : $"Takes: {_selected.Info.Duration}";

    public bool IsRunning { get => _running; private set { Set(ref _running, value); Changed(); } }
    public string Instruction { get => _instruction; private set => Set(ref _instruction, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public double Fraction { get => _fraction; private set => Set(ref _fraction, value); }
    public bool HasResult => Findings.Count > 0;
    public bool HasValues => Values.Count > 0;

    /// <summary>Runs tests faster (screenshots and demos only).</summary>
    public double Speed { get; set; } = 1;

    public AsyncCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }

    private void Changed()
    {
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Reads the fault memories of the start-chain modules (engine, key and steering lock), described, for the no-start triage.</summary>
    private async Task<IReadOnlyList<NoStartTriage.ModuleFault>> StartChainFaultsAsync(CancellationToken ct)
    {
        if (_session.Backend is not { } backend || _session.Vehicle is not { } vehicle) return [];
        var wanted = new[] { "DME", "DDE", "CAS", "ELV", "EWS", "EKPS" };
        var scanner = new VehicleScanner(backend, _session.Codes);
        var result = new List<NoStartTriage.ModuleFault>();
        foreach (var ecu in vehicle.Modules.Where(m => wanted.Any(w => m.Id.EndsWith("." + w, StringComparison.OrdinalIgnoreCase) || m.Id.Equals(w, StringComparison.OrdinalIgnoreCase))))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var identity = _session.LastScan?.Ecus.FirstOrDefault(e => e.Ecu.Id == ecu.Id)?.Identity;
                foreach (var f in await backend.ReadFaultsAsync(ecu, ct))
                    result.Add(new(ecu.Id.Split('.').Last(), scanner.Describe([f], ecu, identity)[0]));
            }
            catch (Exception e) when (e is DiagnosticException or NotSupportedException or TimeoutException) { }
        }
        return result;
    }

    private void Select(GuidedTestViewModel test)
    {
        if (_running) return;
        foreach (var t in Tests) t.IsSelected = t == test;
        foreach (var g in Guides) g.IsSelected = false;
        Guide = null;
        _selected = test;
        Findings.Clear();
        Values.Clear();
        Charts.Clear();
        SavedNote = "";
        Instruction = Status = "";
        Fraction = 0;
        // Viewing a snapshot: show the owner's own run of this test, graphs and all.
        if (_session.Snapshot?.Tests.LastOrDefault(t => t.TestId == test.Info.Id) is { } recorded) ShowReport(recorded.Report, $"From the snapshot: the owner ran this test on {recorded.Time.LocalDateTime:d MMM yyyy HH:mm}.");
        else if (_session.IsSnapshot) Instruction = "The snapshot has no result for this test.";
        foreach (var p in new[] { nameof(TestTitle), nameof(Summary), nameof(Requirements), nameof(Duration), nameof(HasResult), nameof(HasValues), nameof(HasCharts), nameof(DrivesParts) })
            Raise(p);
        Changed();
    }

    public override void OnHidden() => _cts?.Cancel();

    private void ShowReport(TestReport report, string note)
    {
        Findings.Clear();
        Values.Clear();
        foreach (var f in report.Findings) Findings.Add(new TestFindingViewModel(f));
        var max = report.Values.Count == 0 ? 0 : report.Values.Max(v => Math.Abs(v.Value));
        foreach (var v in report.Values) Values.Add(new TestValueViewModel(v.Key, v.Value, report.Unit, max));
        ShowTraces(report.Traces);
        SavedNote = note;
        Raise(nameof(HasResult));
        Raise(nameof(HasValues));
    }

    private async Task RunAsync()
    {
        if (_selected is not { } test || _session.Backend is not { } backend || Engine is not { } engine) return;
        var demoSignals = _session.IsDemo && _session.Measurements is { } demo ? new Klemme15.Protocols.Simulation.DemoSignals(demo, engine) : null;
        ISignalSource signals = demoSignals is not null ? demoSignals : new EngineSignals(backend, engine, _session.Measurements);
        IActuator? actuator = !test.Info.DrivesParts ? null
            : demoSignals is not null ? new Klemme15.App.Services.DemoPumpActuator(demoSignals)
            : Klemme15.App.Services.JobPumpActuator.For(_session, engine);
        Findings.Clear();
        Values.Clear();
        Charts.Clear();
        SavedNote = "";
        Raise(nameof(HasResult));
        Raise(nameof(HasValues));
        Raise(nameof(HasCharts));
        _cts = new CancellationTokenSource();
        IsRunning = true;
        _session.Log.Note($"Guided test '{test.Title}' started");
        try
        {
            var progress = new Progress<TestProgress>(p => { Instruction = p.Instruction; Status = p.Status; Fraction = p.Fraction * 100; ShowTraces(p.Traces); });
            Func<CancellationToken, Task<IReadOnlyList<NoStartTriage.ModuleFault>>>? faults =
                test.Info.Id == "no-start" ? StartChainFaultsAsync : null;
            var report = await GuidedTests.RunAsync(test.Info.Id, signals, progress, _cts.Token, Speed, faults, actuator);
            foreach (var f in report.Findings) Findings.Add(new TestFindingViewModel(f));
            if (report.Traces.Count > 0) ShowTraces(report.Traces);
            SavedNote = report.Verdict == TestSeverity.Unknown ? ""
                : _session.RecordTest(test.Info, report) ? "Saved to this car's health history: the Faults page shows if it gets worse over time."
                : _session.IsDemo ? "" : "Not saved to the health history: run a quick test first so Klemme15 knows which car this is.";
            var max = report.Values.Count == 0 ? 0 : report.Values.Max(v => Math.Abs(v.Value));
            foreach (var v in report.Values) Values.Add(new TestValueViewModel(v.Key, v.Value, report.Unit, max));
            Instruction = "Finished.";
            Status = "";
            _session.Log.Note($"Guided test '{test.Title}': {string.Join("; ", report.Findings.Select(f => f.Title))}");
        }
        catch (OperationCanceledException) { Instruction = "Stopped."; Status = ""; }
        catch (Exception e) when (e is DiagnosticException or TimeoutException) { Instruction = $"Couldn't finish: {e.Message}"; }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsRunning = false;
            Raise(nameof(HasResult));
            Raise(nameof(HasValues));
        }
    }
}
