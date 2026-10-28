using System.Collections.ObjectModel;
using Avalonia.Media;
using Klemme15.Core;

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
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public RelayCommand? SelectCommand { get; set; }
}

/// <summary>
/// Guided tests: Klemme15 measures the engine while telling you what to do (idle, hold 2,500 rpm, blip the throttle)
/// and explains what the numbers mean. Read-only: nothing is changed on the car.
/// </summary>
public sealed class TestsViewModel : PageViewModel
{
    private readonly SessionState _session;
    private GuidedTestViewModel? _selected;
    private CancellationTokenSource? _cts;
    private bool _running;
    private string _instruction = "", _status = "";
    private double _fraction;

    public TestsViewModel(SessionState session)
    {
        _session = session;
        foreach (var t in GuidedTests.All)
        {
            var vm = new GuidedTestViewModel(t);
            vm.SelectCommand = new RelayCommand(() => Select(vm));
            Tests.Add(vm);
        }
        StartCommand = new AsyncCommand(RunAsync, () => !_running && _selected is not null && _session.IsConnected && Engine is not null);
        StopCommand = new RelayCommand(() => _cts?.Cancel(), () => _running);
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionState.Backend)) OnUi(() => { Raise(nameof(NotConnected)); Changed(); });
        };
        Select(Tests[0]);
    }

    public override string Title => "Tests";
    public override Geometry Icon => Icons.Tests;

    public ObservableCollection<GuidedTestViewModel> Tests { get; } = [];
    public ObservableCollection<TestFindingViewModel> Findings { get; } = [];
    public ObservableCollection<TestValueViewModel> Values { get; } = [];

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

    private void Select(GuidedTestViewModel test)
    {
        if (_running) return;
        foreach (var t in Tests) t.IsSelected = t == test;
        _selected = test;
        Findings.Clear();
        Values.Clear();
        Instruction = Status = "";
        Fraction = 0;
        foreach (var p in new[] { nameof(TestTitle), nameof(Summary), nameof(Requirements), nameof(Duration), nameof(HasResult), nameof(HasValues) })
            Raise(p);
        Changed();
    }

    public override void OnHidden() => _cts?.Cancel();

    private async Task RunAsync()
    {
        if (_selected is not { } test || _session.Backend is not { } backend || Engine is not { } engine) return;
        ISignalSource signals = _session.IsDemo && _session.Measurements is { } demo
            ? new Klemme15.Protocols.Simulation.DemoSignals(demo, engine)
            : new EngineSignals(backend, engine, _session.Measurements);
        Findings.Clear();
        Values.Clear();
        Raise(nameof(HasResult));
        Raise(nameof(HasValues));
        _cts = new CancellationTokenSource();
        IsRunning = true;
        _session.Log.Note($"Guided test '{test.Title}' started");
        try
        {
            var progress = new Progress<TestProgress>(p => { Instruction = p.Instruction; Status = p.Status; Fraction = p.Fraction * 100; });
            var report = await GuidedTests.RunAsync(test.Info.Id, signals, progress, _cts.Token, Speed);
            foreach (var f in report.Findings) Findings.Add(new TestFindingViewModel(f));
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
