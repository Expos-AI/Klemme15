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
using System.Globalization;
using System.Text.Json;
using Avalonia.Media;
using Klemme15.Core;
using Klemme15.Core.GhostHunter;

namespace Klemme15.App.ViewModels;

public sealed record SignalOption(string Id, string Label)
{
    public override string ToString() => Label;
}

public sealed record OperatorOption(TriggerOperator Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One condition row in the builder: value, comparison, threshold, how long it must hold.</summary>
public sealed class ConditionRow : ViewModelBase
{
    private SignalOption? _signal;
    private OperatorOption _op;
    private string _threshold = "", _hold = "1";

    public ConditionRow(IReadOnlyList<OperatorOption> operators, IReadOnlyList<SignalOption> signals, Action<ConditionRow> remove)
    {
        Operators = operators;
        Signals = signals;
        _op = operators[0];
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    // The choices live on the row so the combo boxes have them before a selection is applied.
    public IReadOnlyList<OperatorOption> Operators { get; }
    public IReadOnlyList<SignalOption> Signals { get; }
    public SignalOption? Signal { get => _signal; set => Set(ref _signal, value); }
    public OperatorOption Operator { get => _op; set { if (value is not null) Set(ref _op, value); } }
    public string Threshold { get => _threshold; set => Set(ref _threshold, value); }
    public string Hold { get => _hold; set => Set(ref _hold, value); }
    public RelayCommand RemoveCommand { get; }

    public TriggerCondition? ToCondition(out string? problem)
    {
        problem = null;
        if (_signal is null) { problem = "Pick a value for every condition."; return null; }
        if (!TriggerViewModel.TryNumber(_threshold, out var t)) { problem = $"{_signal.Label}: the threshold isn't a number."; return null; }
        if (!TriggerViewModel.TryNumber(_hold, out var h) || h < 0) { problem = $"{_signal.Label}: hold for how many seconds?"; return null; }
        return new TriggerCondition { Parameter = _signal.Id, Operator = _op.Value, Threshold = t, HoldDuration = TimeSpan.FromSeconds(h) };
    }
}

/// <summary>A rule in the list: armed or not.</summary>
public sealed class RuleItem : ViewModelBase
{
    private bool _armed;

    public RuleItem(TriggerRule rule, bool preset, Action<RuleItem>? delete)
    {
        Rule = rule;
        IsPreset = preset;
        DeleteCommand = delete is null ? null : new RelayCommand(() => delete(this));
    }

    public TriggerRule Rule { get; }
    public bool IsPreset { get; }
    public TriggerTemplate? Template => IsPreset ? TriggerPresets.For(Rule.Id) : null;
    public string Title => Template?.Title ?? Rule.Title;
    public string? WhenToUse => Template?.WhenToUse;
    public bool HasWhenToUse => WhenToUse is not null;
    public string? DriveTip => Template is { } t ? "How to catch it: " + t.DriveTip : null;
    public bool HasDriveTip => DriveTip is not null;
    public string Describe => $"{TriggerViewModel.Plain(Rule)} · keeps {Rule.Before.TotalSeconds:0} s before and {Rule.After.TotalSeconds:0} s after";
    public bool IsArmed { get => _armed; set => Set(ref _armed, value); }
    public RelayCommand? DeleteCommand { get; }
    public bool CanDelete => DeleteCommand is not null;
    public bool IsAvailable { get; set; } = true;
    public string? Unavailable { get; set; }
    public bool HasUnavailable => Unavailable is not null;
}

/// <summary>One line of the "Right now" list.</summary>
public sealed class RightNowItem(RuleProgressLine line)
{
    public string Title { get; } = TriggerPresets.For(line.RuleId)?.Title ?? line.Title;
    public string Text { get; } = line.Text;
    public bool IsReady => line.State == RuleReadiness.Ready;
    public bool IsWaiting => line.State is RuleReadiness.Waiting or RuleReadiness.NoReading;
    public bool IsHolding => line.State == RuleReadiness.Holding;
}

/// <summary>A saved capture (.k15) in the captures folder.</summary>
public sealed class CaptureItem(string path, string title, string when, Func<Task> open, Action delete)
{
    public string Path { get; } = path;
    public string Title { get; } = title;
    public string When { get; } = when;
    public AsyncCommand OpenCommand { get; } = new(open);
    public RelayCommand DeleteCommand { get; } = new(delete);
}

/// <summary>
/// The Ghost Hunter: rules that watch live values in the background and save a snapshot with the minute before and after
/// an intermittent fault, so nobody has to watch a laptop while driving. Read-only.
/// </summary>
public sealed class TriggerViewModel : PageViewModel
{
    private readonly SessionState _session;
    private readonly string _version;
    private CancellationTokenSource? _cts;
    private bool _watching, _probing;
    private string _status = "", _banner = "", _builderError = "", _newTitle = "", _before = "60", _after = "60", _signalsText = "";
    private HashSet<string>? _available;

    public static readonly IReadOnlyList<OperatorOption> Operators =
    [
        new(TriggerOperator.Above, "above"), new(TriggerOperator.Below, "below"), new(TriggerOperator.AtOrAbove, "at or above"),
        new(TriggerOperator.AtOrBelow, "at or below"), new(TriggerOperator.Equal, "equal to"), new(TriggerOperator.NotEqual, "not equal to"),
    ];

    /// <summary>Values worth watching; only those the car actually reports are offered.</summary>
    private static readonly string[] Candidates =
        ["rpm", "speed", "coolant", "voltage", "load", "maf", "iat", "throttle", "map", "stft1", "ltft1", "stft2", "ltft2", "oiltemp", "timing",
         "rough1", "rough2", "rough3", "rough4", "rough5", "rough6", "rough7", "rough8", "idle_target", "ac", "ac_compressor",
         DerivedSignals.StftMax, DerivedSignals.StftMin, DerivedSignals.RoughRatio];

    /// <summary>A rule in words: "Engine speed above 1500 rpm and Short-term trim, leaner bank above 15 % for 2 s".</summary>
    public static string Plain(TriggerRule rule) => string.Join(" and ", rule.Conditions.Select(c =>
    {
        var unit = SignalLabels.Unit(c.Parameter);
        var op = Operators.First(o => o.Value == c.Operator).Label;
        var value = c.Threshold.ToString("0.##", CultureInfo.CurrentCulture) + (unit.Length == 0 ? "" : unit is "%" or "×" ? (unit == "%" ? " %" : "×") : " " + unit);
        return $"{SignalLabels.Name(c.Parameter)} {op} {value}" + (c.HoldDuration > TimeSpan.Zero ? $" for {c.HoldDuration.TotalSeconds:0.#} s" : "");
    }));

    /// <summary>Ready-made rules to start from: picking one fills the builder.</summary>
    public IReadOnlyList<TriggerTemplate> Templates => TriggerPresets.Templates;

    private TriggerTemplate? _template;
    private string _templateHint = "";

    public TriggerTemplate? SelectedTemplate
    {
        get => _template;
        set
        {
            if (!Set(ref _template, value) || value is null) return;
            ApplyTemplate(value);
        }
    }

    public string TemplateHint { get => _templateHint; private set { if (Set(ref _templateHint, value)) Raise(nameof(HasTemplateHint)); } }
    public bool HasTemplateHint => _templateHint.Length > 0;

    /// <summary>Fills the builder from a template: name, every condition, and how much to keep. Everything stays editable.</summary>
    public void ApplyTemplate(TriggerTemplate template)
    {
        var rule = template.Rule;
        BuilderError = "";
        NewTitle = rule.Title;
        Before = rule.Before.TotalSeconds.ToString("0", CultureInfo.InvariantCulture);
        After = rule.After.TotalSeconds.ToString("0", CultureInfo.InvariantCulture);
        NewConditions.Clear();
        var missing = new List<string>();
        foreach (var c in rule.Conditions)
        {
            var option = Signals.FirstOrDefault(o => o.Id == c.Parameter);
            if (option is null)
            {
                if (_available is not null) missing.Add(SignalLabels.Name(c.Parameter));
                // Not checked against the car yet (or not reported): offer it so the row still reads right.
                option = new SignalOption(c.Parameter, SignalLabels.Header(c.Parameter));
                Signals.Add(option);
            }
            var row = new ConditionRow(Operators, Signals, r => { NewConditions.Remove(r); })
            {
                Signal = option,
                Operator = Operators.First(o => o.Value == c.Operator),
                Threshold = c.Threshold.ToString("0.##", CultureInfo.InvariantCulture),
                Hold = c.HoldDuration.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture),
            };
            NewConditions.Add(row);
        }
        TemplateHint = $"{template.WhenToUse} {template.DriveTip}" +
            (missing.Count > 0 ? $" This car doesn't report {string.Join(", ", missing)} on this connection, so this rule can't be saved for it." : " Adjust the numbers if you like, then add the rule.");
    }

    public TriggerViewModel(SessionState session, string version)
    {
        _session = session;
        _version = version;
        foreach (var r in TriggerPresets.All) Rules.Add(new RuleItem(r, preset: true, null));
        foreach (var r in LoadCustom()) Rules.Add(new RuleItem(r, preset: false, DeleteRule));
        AddConditionCommand = new RelayCommand(AddCondition);
        SaveRuleCommand = new RelayCommand(SaveRule);
        StartCommand = new AsyncCommand(StartAsync, () => CanStart, e => Status = e.Message);
        StopCommand = new RelayCommand(Stop, () => _watching);
        ProbeCommand = new AsyncCommand(ProbeAsync, () => _session.IsConnected && !_session.IsSnapshot && !_watching && !_probing, e => SignalsText = e.Message);
        AddCondition();
        LoadCaptures();
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionState.Backend) or nameof(SessionState.LastScan) or nameof(SessionState.BusyWith))
                OnUi(() =>
                {
                    if (e.PropertyName == nameof(SessionState.Backend))
                    {
                        // A different connection (or none): watching the old one would read a closed cable forever.
                        if (_watching) Stop();
                        _available = null; Signals.Clear(); SignalsText = "";
                    }
                    Changed();
                });
        };
        Status = "Not watching yet.";
    }

    public override string Title => "Ghost Hunter";
    public override Geometry Icon => Icons.GhostHunter;

    public static string CapturesFolder =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Klemme15", "captures");

    private static string RulesFile =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Klemme15", "ghost-hunter-rules.json");

    public ObservableCollection<RuleItem> Rules { get; } = [];
    public ObservableCollection<ConditionRow> NewConditions { get; } = [];
    public ObservableCollection<SignalOption> Signals { get; } = [];
    public ObservableCollection<CaptureItem> Captures { get; } = [];
    public IReadOnlyList<OperatorOption> OperatorChoices => Operators;

    public string NewTitle { get => _newTitle; set => Set(ref _newTitle, value); }
    public string Before { get => _before; set => Set(ref _before, value); }
    public string After { get => _after; set => Set(ref _after, value); }
    public string BuilderError { get => _builderError; private set { if (Set(ref _builderError, value)) Raise(nameof(HasBuilderError)); } }
    public bool HasBuilderError => _builderError.Length > 0;
    public string SignalsText { get => _signalsText; private set => Set(ref _signalsText, value); }

    public bool IsWatching { get => _watching; private set { if (Set(ref _watching, value)) { Raise(nameof(NotWatching)); Changed(); } } }
    public bool NotWatching => !_watching;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Banner { get => _banner; private set { if (Set(ref _banner, value)) Raise(nameof(HasBanner)); } }
    public bool HasBanner => _banner.Length > 0;
    public bool HasCaptures => Captures.Count > 0;

    public bool CanStart => _session.IsConnected && !_session.IsSnapshot && !_watching && _session.BusyWith is null && _session.LastScan is not null && Rules.Any(r => r.IsArmed);

    public string StartHint => !_session.IsConnected ? "Connect to the car (or a demo car) in the Garage first."
        : _session.IsSnapshot ? "Watching needs a car: this is a snapshot."
        : _session.BusyWith is { } busy ? $"Wait for {busy} to finish (or stop it): one cable can only carry one conversation with the car."
        : _session.LastScan is null ? "Run a quick test first: every capture includes it, so whoever opens it sees the car's modules and faults too."
        : !Rules.Any(r => r.IsArmed) ? "Tick at least one rule."
        : "Then drive the way the problem usually shows up. Nothing to tap while driving: captures are saved by themselves. Stop when you park.";

    public RelayCommand AddConditionCommand { get; }
    public RelayCommand SaveRuleCommand { get; }
    public AsyncCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public AsyncCommand ProbeCommand { get; }

    private void Changed()
    {
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        ProbeCommand.RaiseCanExecuteChanged();
        Raise(nameof(StartHint));
        Raise(nameof(CanStart));
    }

    public override void OnShown()
    {
        foreach (var r in Rules) r.PropertyChanged -= OnRuleChanged;
        foreach (var r in Rules) r.PropertyChanged += OnRuleChanged;
        LoadCaptures();
        Changed();
        if (_available is null && _session.IsConnected && !_session.IsSnapshot && !_watching) ProbeCommand.Execute(null);
    }

    // Watching carries on when the page is hidden: that's the point.
    public override void OnHidden() { }

    private void OnRuleChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RuleItem.IsArmed)) Changed();
    }

    // ---------------------------------------------------------------- what the car reports

    private ISignalSource? Source() => _session.EngineSignalSource() is { } source ? new DerivedSignals(source) : null;

    private async Task ProbeAsync()
    {
        if (Source() is not { } source) return;
        _probing = true;
        Changed();
        SignalsText = "Checking which values this car reports...";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var read = await source.ReadAsync(Candidates, cts.Token);
            _available = read.Keys.ToHashSet();
            if (source is DerivedSignals { ReportsRoughness: true }) _available.UnionWith([DerivedSignals.RoughRatio, DerivedSignals.RoughCylinder]);
            Signals.Clear();
            foreach (var id in Candidates.Where(_available.Contains)) Signals.Add(new SignalOption(id, SignalLabels.Header(id)));
            SignalsText = _available.Count == 0 ? "The engine computer didn't report any live values on this connection." : $"This car reports {_available.Count} values to watch.";
            foreach (var r in Rules)
            {
                var missing = r.Rule.Parameters.Where(p => !_available.Contains(p)).ToList();
                r.IsAvailable = missing.Count == 0;
                r.Unavailable = missing.Count == 0 ? null : $"Not reported on this connection: {string.Join(", ", missing.Select(SignalLabels.Name))}";
                if (!r.IsAvailable) r.IsArmed = false;
            }
            var refreshed = Rules.ToList();
            Rules.Clear();
            foreach (var r in refreshed) Rules.Add(r);
            foreach (var row in NewConditions) row.Signal ??= Signals.FirstOrDefault();
        }
        catch (OperationCanceledException) { SignalsText = "The car didn't answer in time. Check the connection and try again."; }
        finally { _probing = false; Changed(); }
    }

    // ---------------------------------------------------------------- the builder

    private void AddCondition()
    {
        var row = new ConditionRow(Operators, Signals, r => { NewConditions.Remove(r); });
        row.Signal = Signals.FirstOrDefault();
        NewConditions.Add(row);
    }

    private void SaveRule()
    {
        BuilderError = "";
        var conditions = new List<TriggerCondition>();
        foreach (var row in NewConditions)
        {
            if (row.ToCondition(out var problem) is { } c) conditions.Add(c);
            else { BuilderError = problem ?? "Check the conditions."; return; }
        }
        if (!TryNumber(_before, out var before) || !TryNumber(_after, out var after))
        { BuilderError = "Seconds before and after must be numbers."; return; }
        var rule = new TriggerRule
        {
            Id = "custom-" + Guid.NewGuid().ToString("N")[..8],
            Title = string.IsNullOrWhiteSpace(_newTitle) ? string.Join(" and ", conditions.Select(c => c.ToString())) : _newTitle.Trim(),
            Conditions = conditions,
            Before = TimeSpan.FromSeconds(before),
            After = TimeSpan.FromSeconds(after),
        };
        var problems = rule.Validate(_available);
        if (problems.Count > 0) { BuilderError = string.Join(" ", problems); return; }
        var item = new RuleItem(rule, preset: false, DeleteRule) { IsArmed = true };
        item.PropertyChanged += OnRuleChanged;
        Rules.Add(item);
        SaveCustom();
        NewTitle = "";
        NewConditions.Clear();
        AddCondition();
        _template = null;
        Raise(nameof(SelectedTemplate));
        TemplateHint = "";
        Changed();
    }

    private void DeleteRule(RuleItem item)
    {
        if (_watching) return;
        Rules.Remove(item);
        SaveCustom();
        Changed();
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static IEnumerable<TriggerRule> LoadCustom()
    {
        List<TriggerRule> rules;
        try { rules = File.Exists(RulesFile) ? JsonSerializer.Deserialize<List<TriggerRule>>(File.ReadAllText(RulesFile), Json) ?? [] : []; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or NotSupportedException) { return []; }
        // A hand-edited file mustn't clash with a ready-made rule or another rule, or put odd characters in file names.
        var taken = TriggerPresets.All.Select(r => r.Id).ToHashSet();
        var safe = new List<TriggerRule>();
        foreach (var r in rules.Where(r => r is { Conditions.Count: > 0 }))
        {
            var id = r.Id is { } given && System.Text.RegularExpressions.Regex.IsMatch(given, "^custom-[a-z0-9]{1,16}$") && taken.Add(given)
                ? given : "custom-" + Guid.NewGuid().ToString("N")[..8];
            taken.Add(id);
            safe.Add(r with { Id = id });
        }
        return safe;
    }

    /// <summary>
    /// Reads a number as people type it: "1500", "1,500" (thousands), "11.5" or "11,5" (decimal comma). A comma
    /// followed by exactly three digits is a thousands separator, so "1,500 rpm" never becomes 1.5.
    /// </summary>
    public static bool TryNumber(string? text, out double value)
    {
        var s = (text ?? "").Trim();
        if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^-?\d{1,3}(,\d{3})+(\.\d+)?$")) s = s.Replace(",", "");
        else s = s.Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    private void SaveCustom()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(RulesFile)!);
            File.WriteAllText(RulesFile, JsonSerializer.Serialize(Rules.Where(r => !r.IsPreset).Select(r => r.Rule).ToList(), Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { BuilderError = $"Couldn't save your rules: {e.Message}"; }
    }

    // ---------------------------------------------------------------- watching

    private async Task StartAsync()
    {
        if (Source() is not { } source || _session.LastScan is null) return;
        var rules = Rules.Where(r => r.IsArmed).Select(r => r.Rule).DistinctBy(r => r.Id).ToList();
        var baseSnapshot = _session.BuildSnapshot(null, includeLive: false, includeTests: false, includeCoding: false, includeLog: false, _version);
        // Worked-out values (the roughest cylinder) come from others the car reports, so they're never filtered out.
        var ids = TriggerMonitor.SignalsFor(rules).Where(id => _available is null || _available.Contains(id) || DerivedSignals.Ids.Contains(id)).ToList();
        var monitor = new TriggerMonitor(rules, (capture, ct) => SaveCaptureAsync(capture, baseSnapshot));
        monitor.Fired += ev => OnUi(() => Banner = $"Caught {ev.At.LocalDateTime:HH:mm:ss}: {ev.Title}. Recording {rules.First(r => r.Id == ev.RuleId).After.TotalSeconds:0} s more...");
        monitor.Captured += c => OnUi(() => { LoadCaptures(); Banner = $"Saved {c.Event.At.LocalDateTime:HH:mm:ss}: {c.Event.Title}. Open it below."; });
        var lastShown = DateTime.MinValue;
        monitor.FrameSeen += frame =>
        {
            // About once a second is plenty to read, and keeps the window calm on a long drive.
            if (DateTime.UtcNow - lastShown < TimeSpan.FromSeconds(1)) return;
            lastShown = DateTime.UtcNow;
            var lines = rules.Select(r => RuleProgress.Describe(r, frame.Values)).ToList();
            OnUi(() => ShowRightNow(lines));
        };
        monitor.StatusChanged += st => OnUi(() => Status = st.State == WatchState.Stopped ? $"Stopped. {st.Captures} capture{(st.Captures == 1 ? "" : "s")} this drive."
            : $"Watching {st.ArmedRules} rule{(st.ArmedRules == 1 ? "" : "s")} · {st.FramesPerSecond:0.0} readings/s · {st.Captures} caught");

        _cts = new CancellationTokenSource();
        using var awake = Klemme15.App.Services.SleepGuard.Hold("Ghost Hunter watching");
        IsWatching = true;
        _session.IsWatching = true;
        Banner = "";
        _session.Log.Note($"Ghost Hunter started: {string.Join("; ", rules.Select(r => r.Title))}");
        Task? poller = null;
        try
        {
            poller = TriggerPoller.RunAsync(source, ids, monitor.Input, _cts.Token,
                onError: e => _session.Log.Note($"Ghost Hunter read skipped: {e.Message}"),
                onStall: stalled => OnUi(() =>
                {
                    Banner = stalled
                        ? $"The car stopped answering at {DateTime.Now:HH:mm}. Check the cable is still plugged in and the ignition is on: watching carries on when it answers again."
                        : "The car is answering again. Still watching.";
                    _session.Log.Note(stalled ? "Ghost Hunter: no values from the car" : "Ghost Hunter: values again");
                }));
            await Task.Run(() => monitor.RunAsync(_cts.Token));
            await poller;
        }
        finally
        {
            // Never leave the poller reading the car after the monitor has stopped, however it stopped.
            _cts.Cancel();
            if (poller is not null) try { await poller; } catch (Exception) { }
            _cts.Dispose();
            _cts = null;
            IsWatching = false;
            _session.IsWatching = false;
            ShowRightNow([]);
            _session.Log.Note("Ghost Hunter stopped");
        }
    }

    private void Stop() => _cts?.Cancel();

    /// <summary>While watching: each rule's state from the latest reading ("Waiting for coolant above 70 °C (now 58 °C)").</summary>
    public ObservableCollection<RightNowItem> RightNow { get; } = [];
    public bool HasRightNow => RightNow.Count > 0;

    private void ShowRightNow(IReadOnlyList<RuleProgressLine> lines)
    {
        RightNow.Clear();
        foreach (var l in lines) RightNow.Add(new RightNowItem(l));
        Raise(nameof(HasRightNow));
    }

    /// <summary>Stops watching (when the app closes); a capture in progress is saved as it stands.</summary>
    public void StopWatching() => Stop();

    private static async Task SaveCaptureAsync(TriggerCapture capture, CarSnapshot baseSnapshot)
    {
        Directory.CreateDirectory(CapturesFolder);
        var name = $"ghost-{capture.Event.RuleId}-{capture.Event.At.LocalDateTime:yyyyMMdd-HHmmss}{SnapshotFile.Extension}";
        await File.WriteAllBytesAsync(System.IO.Path.Combine(CapturesFolder, name), SnapshotFile.Write(capture.ToSnapshot(baseSnapshot)));
    }

    private void LoadCaptures()
    {
        Captures.Clear();
        try
        {
            if (Directory.Exists(CapturesFolder))
                foreach (var f in new DirectoryInfo(CapturesFolder).GetFiles("*" + SnapshotFile.Extension).OrderByDescending(f => f.LastWriteTime).Take(50))
                {
                    var title = f.Name;
                    try { if (SnapshotFile.Read(File.ReadAllBytes(f.FullName)).Manifest.Trigger is { } t) title = t.Title + (t.Partial ? " (stopped early)" : ""); }
                    catch (SnapshotException) { title += " (can't be read)"; }
                    var path = f.FullName;
                    Captures.Add(new CaptureItem(path, title, f.LastWriteTime.ToString("d MMM yyyy HH:mm:ss", CultureInfo.CurrentCulture),
                        () => OpenCaptureAsync(path), () => DeleteCapture(path)));
                }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        Raise(nameof(HasCaptures));
    }

    /// <summary>Opens a capture like any snapshot: read-only, live data on the Analyse page. Watching stops first.</summary>
    /// <summary>Opens a capture; returns why it couldn't, or null.</summary>
    public Func<byte[], Task<string?>>? OpenSnapshot { get; set; }

    private async Task OpenCaptureAsync(string path)
    {
        if (_watching) { Banner = "Stop watching before opening a capture: opening it disconnects from the car."; return; }
        if (_session.JobRunning) { Banner = $"Wait for \"{_session.BusyWith}\" to finish first: opening a capture disconnects from the car."; return; }
        if (OpenSnapshot is not { } open) return;
        byte[] data;
        try { data = await File.ReadAllBytesAsync(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Banner = $"That capture can't be opened: {Problems.Describe(e)}"; LoadCaptures(); return; }
        if (await open(data) is { } problem) Banner = $"That capture can't be opened: {problem}";
    }

    private void DeleteCapture(string path)
    {
        try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        LoadCaptures();
    }
}
