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

public sealed class GuidedTestViewModel(GuidedTestInfo info, DiagnosticChain? chain = null) : ViewModelBase
{
    private bool _isSelected, _needsEdiabas;
    public GuidedTestInfo Info { get; } = info;

    /// <summary>A community diagnostic chain, rather than one of Klemme15's built-in tests.</summary>
    public DiagnosticChain? Chain { get; } = chain;
    public bool IsChain => Chain is not null;
    public string Title => Info.Title;
    public bool DrivesParts => Info.DrivesParts;

    /// <summary>On this car's connection the test can't get its values (a K-line car without EDIABAS mode).</summary>
    public bool NeedsEdiabas { get => _needsEdiabas; set => Set(ref _needsEdiabas, value); }

    /// <summary>Why the test doesn't fit this car's engine (a diesel, or no electric water pump), or null.</summary>
    public string? NotForEngine { get => _notForEngine; set { if (Set(ref _notForEngine, value)) Raise(nameof(HasNotForEngine)); } }
    private string? _notForEngine;
    public bool HasNotForEngine => _notForEngine is not null;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public RelayCommand? SelectCommand { get; set; }
}

internal sealed class CompositeHold(IDisposable a, IDisposable b) : IDisposable
{
    public void Dispose() { a.Dispose(); b.Dispose(); }
}

/// <summary>One answer button while a chain asks a question.</summary>
public sealed class ChainChoiceViewModel(ChainChoice choice, Action<string> pick)
{
    public string Label => choice.Label;
    public RelayCommand PickCommand { get; } = new(() => pick(choice.Value));
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
public sealed class TestsViewModel : PageViewModel, IChainPrompt
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
        foreach (var c in session.Database.Chains)
        {
            var vm = new GuidedTestViewModel(c.Info, c);
            vm.SelectCommand = new RelayCommand(() => Select(vm));
            Chains.Add(vm);
        }
        StartCommand = new AsyncCommand(RunAsync, () => !_running && _selected is not null && _session.IsConnected && !_session.IsSnapshot && !_session.IsWatching && (_session.BusyWith is null || _running) && Engine is not null
            && !(_selected.NeedsEdiabas) && !_selected.HasNotForEngine, e => Instruction = $"Couldn't finish: {e.Message}");
        StopCommand = new RelayCommand(() => _cts?.Cancel(), () => _running);
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionState.IsWatching) or nameof(SessionState.BusyWith)) OnUi(Changed);
            if (e.PropertyName is nameof(SessionState.Backend)) OnUi(() => { Raise(nameof(NotConnected)); MarkAvailability(); Changed(); if (_selected is { } t) Select(t); });
        };
        Select(Tests[0]);
    }

    /// <summary>Marks the tests this car's connection can't run (BMW values on a K-line car without EDIABAS mode).</summary>
    private void MarkAvailability()
    {
        var obdOnly = _session.ObdValuesOnly;
        foreach (var t in Tests)
        {
            t.NeedsEdiabas = obdOnly && t.Info.NeedsBmwValues;
            t.NotForEngine = t.IsChain ? null : t.Info.NotForEngine(_session.Model);
        }
    }

    public override string Title => "Tests";
    public override Geometry Icon => Icons.Tests;

    public ObservableCollection<GuidedTestViewModel> Tests { get; } = [];

    /// <summary>Community diagnostic chains (db/chains.json), listed under the built-in tests.</summary>
    public ObservableCollection<GuidedTestViewModel> Chains { get; } = [];

    /// <summary>Selects a test or chain by id (from a fault's "Run the ... chain" button).</summary>
    public void SelectById(string id)
    {
        if (Tests.Concat(Chains).FirstOrDefault(t => t.Info.Id == id) is { } t) Select(t);
    }

    // ---------------------------------------------------------------- questions a chain asks

    private TaskCompletionSource<string>? _answer;
    private string _question = "";
    public ObservableCollection<ChainChoiceViewModel> Choices { get; } = [];
    public string Question { get => _question; private set { if (Set(ref _question, value)) Raise(nameof(IsAsking)); } }
    public bool IsAsking => _question.Length > 0;

    public async Task<string> AskAsync(string question, IReadOnlyList<ChainChoice> choices, CancellationToken ct)
    {
        _answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Choices.Clear();
        foreach (var c in choices) Choices.Add(new ChainChoiceViewModel(c, v => _answer?.TrySetResult(v)));
        Question = question;
        await using var _ = ct.Register(() => _answer?.TrySetCanceled());
        try { return await _answer.Task; }
        finally { Question = ""; Choices.Clear(); }
    }
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
        foreach (var t in Tests.Concat(Chains)) t.IsSelected = false;
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

    /// <summary>A quiet thank-you link under a finished test's verdict. Klemme15 stays free either way.</summary>
    public RelayCommand TipCommand => _tipCommand ??= new RelayCommand(() => _openUrl(Services.Links.Coffee));
    private RelayCommand? _tipCommand;
    public bool HasTip => HasResult && !string.IsNullOrWhiteSpace(Services.Links.Coffee);
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
        foreach (var t in Tests.Concat(Chains)) t.IsSelected = t == test;
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
        else if (test.NotForEngine is { } why) Instruction = why;
        foreach (var p in new[] { nameof(TestTitle), nameof(Summary), nameof(Requirements), nameof(Duration), nameof(HasResult), nameof(HasValues), nameof(HasCharts), nameof(DrivesParts) })
            Raise(p);
        Changed();
    }

    public override void OnHidden() => _cts?.Cancel();

    /// <summary>"N52 3.0 L inline-6" → 3.0 (0 when unknown).</summary>
    public static double Displacement(string? engine) =>
        engine is not null && System.Text.RegularExpressions.Regex.Match(engine, @"(\d\.\d)\s*L") is { Success: true } m
            ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;

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
        Raise(nameof(HasTip));
        Raise(nameof(HasValues));
    }

    /// <summary>Whether experimental functions are switched on (the switch on the Service page; set by the main window).</summary>
    public Func<bool> ExperimentalOn { get; set; } = () => false;

    private async Task RunAsync()
    {
        if (_selected is not { } test || _session.Backend is not { } backend || Engine is not { } engine) return;
        // Driving a part follows the same rule as service functions: experimental until confirmed on a real car.
        if (test.Info.DrivesParts && !_session.IsDemo && !ExperimentalOn())
        {
            Instruction = "This test switches a part on, which is still experimental. Turn on experimental functions on the Service page first.";
            return;
        }
        var demoSignals = _session.IsDemo && _session.Measurements is { } demo ? new Klemme15.Protocols.Simulation.DemoSignals(demo, engine) { IdleCheck = test.Info.Id == "idle-ac" } : null;
        ISignalSource signals = demoSignals is not null ? demoSignals : new EngineSignals(backend, engine, _session.Measurements);
        IActuator? actuator = !test.Info.DrivesParts ? null
            : demoSignals is not null ? new Klemme15.App.Services.DemoPumpActuator(demoSignals)
            : Klemme15.App.Services.JobPumpActuator.For(_session, engine);
        Findings.Clear();
        Values.Clear();
        Charts.Clear();
        SavedNote = "";
        Raise(nameof(HasResult));
        Raise(nameof(HasTip));
        Raise(nameof(HasValues));
        Raise(nameof(HasCharts));
        _cts = new CancellationTokenSource();
        IsRunning = true;
        _session.Log.Note($"Guided test '{test.Title}' started");
        // Keep the computer awake (the bleed assistant runs 13 minutes); a test that drives a part is also journaled,
        // so a cut-off is noticed and the part is switched off again on reconnect.
        var cutOff = false;
        using var job = actuator is not null
            ? _session.BeginOperation(new RunningOperation
            {
                Kind = OperationKind.PartTest, Title = test.Title, ModuleId = engine.Id, ModuleName = engine.Name,
                Car = $"{_session.Vehicle?.Id} {_session.Model?.Name}".Trim(), Started = DateTimeOffset.Now, StopAction = "pump",
            })
            : new CompositeHold(Klemme15.App.Services.SleepGuard.Hold(test.Title), _session.ClaimBus(test.Title));
        try
        {
            var progress = new Progress<TestProgress>(p => { Instruction = p.Instruction; Status = p.Status; Fraction = p.Fraction * 100; ShowTraces(p.Traces); });
            Func<CancellationToken, Task<IReadOnlyList<NoStartTriage.ModuleFault>>>? faults =
                test.Info.Id == "no-start" ? StartChainFaultsAsync : null;
            var report = test.Chain is { } chain
                ? await ChainRunner.RunAsync(chain, signals, this, progress, _cts.Token, Speed, Displacement(_session.Model?.Engine))
                : await GuidedTests.RunAsync(test.Info.Id, signals, progress, _cts.Token, Speed, faults, actuator);
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
        catch (Exception e) when (e is DiagnosticException or TimeoutException)
        {
            Instruction = $"Couldn't finish: {e.Message}";
            cutOff = actuator is not null && Klemme15.Data.ServiceOutcome.IsCutOff(e);
        }
        finally
        {
            if (actuator is not null) { if (cutOff) _session.OperationCutOff(); else _session.EndOperation(); }
            _cts.Dispose();
            _cts = null;
            IsRunning = false;
            Raise(nameof(HasResult));
            Raise(nameof(HasTip));
            Raise(nameof(HasValues));
        }
    }
}
