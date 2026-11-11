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
using System.Text;
using Avalonia.Media;
using Klemme15.App.Services;
using Klemme15.Core;

namespace Klemme15.App.ViewModels;

public sealed class LiveParameterViewModel(LiveParameter parameter) : ViewModelBase
{
    private const int HistoryLength = 120;
    private readonly Queue<double> _history = new();
    private bool _isSelected;
    private string _value = "--";
    private string? _note;
    private bool _hasNumber;
    private double? _min, _max;
    private IReadOnlyList<double> _points = [];

    public LiveParameter Parameter { get; } = parameter;
    public string Name => Parameter.Name;
    /// <summary>The unit as shown (°F and mph in US units); recordings keep the car's own metric unit.</summary>
    public string Unit => DisplayUnits.Unit(Parameter.Unit);

    /// <summary>Why there's no number ("not reported by this engine computer"), shown small under the dash.</summary>
    public string? Note { get => _note; private set { if (Set(ref _note, value)) Raise(nameof(HasNote)); } }
    public bool HasNote => !string.IsNullOrEmpty(_note);
    public bool HasNumber { get => _hasNumber; private set => Set(ref _hasNumber, value); }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public string Value { get => _value; private set => Set(ref _value, value); }
    public IReadOnlyList<double> Points { get => _points; private set => Set(ref _points, value); }
    public string Range => _min is null ? "" : $"min {_min:0.#}  max {_max:0.#}";
    public double? Latest { get; private set; }

    public void Update(LiveValue v)
    {
        var shown = v.Value is { } raw ? DisplayUnits.Convert(raw, Parameter.Unit).Value : (double?)null;
        Value = shown is { } d ? d.ToString(Math.Abs(d) >= 100 ? "0" : "0.#", CultureInfo.CurrentCulture) : "--";
        HasNumber = v.Value is not null;
        Note = v.Value is null ? v.Text : null;
        Latest = v.Value;
        if (shown is not { } x) return;
        _min = _min is null ? x : Math.Min(_min.Value, x);
        _max = _max is null ? x : Math.Max(_max.Value, x);
        _history.Enqueue(x);
        while (_history.Count > HistoryLength) _history.Dequeue();
        Points = _history.ToArray();
        Raise(nameof(Range));
    }

    public void Reset()
    {
        _history.Clear();
        _min = _max = null;
        Latest = null;
        Points = [];
        Value = "--";
        HasNumber = false;
        Note = null;
        Raise(nameof(Range));
    }
}

public sealed class LiveViewModel : PageViewModel
{
    private readonly SessionState _session;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _cts;
    private bool _running;
    private string _rate = "";
    private string? _error;
    private StringBuilder? _recording;

    /// <summary>The recording's columns, fixed when it starts, so every row lines up with the header.</summary>
    private List<LiveParameterViewModel> _columns = [];

    private void StartColumns()
    {
        _columns = Selected.ToList();
        Row("time," + string.Join(",", _columns.Select(p => $"\"{p.Name} ({p.Parameter.Unit})\"")));
    }

    /// <summary>A copy of the recording on disk while it runs, so a crash or a flat laptop battery doesn't lose it.</summary>
    private RecordingAutosave? _autosave;
    private string? _recovered = RecordingAutosave.Leftovers(RecordingAutosave.DefaultFolder).FirstOrDefault();

    private void Row(string line)
    {
        _recording?.AppendLine(line);
        _autosave?.AppendLine(line);
    }

    /// <summary>A recording from a session that ended without saving it.</summary>
    public bool HasRecovered => _recovered is not null;
    public string RecoveredText => _recovered is { } file
        ? $"A live data recording from {RecordingAutosave.When(file)} wasn't saved before Klemme15 closed. It's still here."
        : "";
    public RelayCommand OpenRecoveredCommand { get; }
    public RelayCommand DiscardRecoveredCommand { get; }

    private void ForgetRecovered(bool delete)
    {
        if (delete)
            foreach (var file in RecordingAutosave.Leftovers(RecordingAutosave.DefaultFolder, _autosave?.Path))
                try { File.Delete(file); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        _recovered = null;
        Raise(nameof(HasRecovered));
        Raise(nameof(RecoveredText));
    }

    /// <summary>Hands the current recording's CSV text to the Analyse page.</summary>
    public Action<string>? AnalyseRecording { get; set; }

    public LiveViewModel(SessionState session, IDialogService dialogs)
    {
        _session = session;
        _dialogs = dialogs;
        StartCommand = new AsyncCommand(RunAsync, () => _session.IsConnected && !_session.IsWatching && _session.BusyWith is null && !_running && Parameters.Any(p => p.IsSelected), e => Error = e.Message);
        StopCommand = new RelayCommand(Stop, () => _running);
        RecordCommand = new RelayCommand(() =>
        {
            _recording = new StringBuilder();
            try { _autosave = new RecordingAutosave(RecordingAutosave.DefaultFolder); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _autosave = null; }   // record in memory only
            if (_running) StartColumns();
            Raise(nameof(IsRecording));
            RaiseCommands();
        }, () => _recording is null);
        SaveRecordingCommand = new AsyncCommand(SaveRecordingAsync, () => _recording is not null, e => Error = Problems.Describe(e));
        AnalyseCommand = new RelayCommand(() => { if (_recording is { } rec) AnalyseRecording?.Invoke(rec.ToString()); },
            () => _recording is not null && AnalyseRecording is not null);
        OpenRecoveredCommand = new RelayCommand(() =>
        {
            if (_recovered is not { } file) return;
            try { AnalyseRecording?.Invoke(File.ReadAllText(file)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Error = $"Couldn't open the recording: {e.Message}"; }
        });
        DiscardRecoveredCommand = new RelayCommand(() => ForgetRecovered(delete: true));
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionState.Backend)) OnUi(LoadParameters);
            if (e.PropertyName is nameof(SessionState.IsWatching) or nameof(SessionState.BusyWith)) OnUi(StartCommand.RaiseCanExecuteChanged);
        };
    }

    public override string Title => "Live data";
    public override Geometry Icon => Icons.Live;

    public ObservableCollection<LiveParameterViewModel> Parameters { get; } = [];
    public IEnumerable<LiveParameterViewModel> Selected => Parameters.Where(p => p.IsSelected);
    public bool IsRunning { get => _running; private set { if (Set(ref _running, value)) RaiseCommands(); } }
    public bool IsRecording => _recording is not null;
    public string Rate { get => _rate; private set => Set(ref _rate, value); }
    public bool HasParameters => Parameters.Count > 0;
    public string EmptyText => _session.IsSnapshot ? "This snapshot has no live data. If the owner included some, it replays on the Analyse page."
        : _session.IsConnected
        ? "This connection offers no live engine values yet. In EDIABAS mode, add values from the Expert page."
        : "Connect to your car (or open a demo car) in the Garage first.";

    public string? Error
    {
        get => _error;
        set { if (Set(ref _error, value)) Raise(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);

    public AsyncCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand RecordCommand { get; }
    public AsyncCommand SaveRecordingCommand { get; }
    public RelayCommand AnalyseCommand { get; }

    /// <summary>
    /// Leaving the page pauses live data (unless it's recording), so the quick test, readings and service functions
    /// have the bus to themselves: on a real E90, background polling doubled the quick test's time.
    /// </summary>
    public override void OnHidden()
    {
        if (_recording is null) Stop();
    }

    public override void OnShown()
    {
        if (Parameters.Count == 0) LoadParameters();
        Raise(nameof(EmptyText));
        StartCommand.RaiseCanExecuteChanged();
    }

    public void Stop() => _cts?.Cancel();

    private EcuDefinition? Engine => _session.Vehicle?.Modules.FirstOrDefault(m => m.IsEngine);

    private void LoadParameters()
    {
        Stop();
        foreach (var p in Parameters) p.PropertyChanged -= OnParameterChanged;
        Parameters.Clear();
        if (_session.Backend is { } backend && Engine is { } engine)
        {
            var defaults = new HashSet<string> { "rpm", "coolant", "iat", "load", "stft1", "ltft1", "voltage", "speed" };
            foreach (var p in backend.GetLiveParameters(engine))
            {
                var vm = new LiveParameterViewModel(p) { IsSelected = defaults.Contains(p.Id) || p.Source.StartsWith("ediabas:", StringComparison.Ordinal) };
                vm.PropertyChanged += OnParameterChanged;
                Parameters.Add(vm);
            }
        }
        Raise(nameof(HasParameters));
        Raise(nameof(Selected));
        Raise(nameof(EmptyText));
        StartCommand.RaiseCanExecuteChanged();
    }

    private void OnParameterChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LiveParameterViewModel.IsSelected)) return;
        Raise(nameof(Selected));
        StartCommand.RaiseCanExecuteChanged();
    }

    private async Task RunAsync()
    {
        if (_session.Backend is not { } backend || Engine is not { } engine) return;
        Error = null;
        var selected = Selected.ToList();
        foreach (var p in selected) p.Reset();
        StartColumns();

        _cts = new CancellationTokenSource();
        IsRunning = true;
        using var bus = _session.ClaimBus("Live data");
        var frames = 0;
        var started = DateTime.UtcNow;
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                // Values ticked or unticked while running take effect on the next update.
                selected = Selected.ToList();
                if (selected.Count == 0) { await Task.Delay(200, _cts.Token); continue; }
                var byId = selected.ToDictionary(p => p.Parameter.Id);
                var tick = System.Diagnostics.Stopwatch.GetTimestamp();
                var values = await backend.ReadLiveAsync(engine, selected.Select(p => p.Parameter).ToList(), _cts.Token);
                foreach (var v in values)
                    if (byId.TryGetValue(v.Parameter.Id, out var vm)) vm.Update(v);
                _session.LiveBuffer.Add(DateTimeOffset.Now, selected.ToDictionary(p => string.IsNullOrEmpty(p.Parameter.Unit) ? p.Name : $"{p.Name} ({p.Parameter.Unit})", p => p.Latest));
                if (_recording is not null)
                    Row(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "," +
                        string.Join(",", _columns.Select(p => byId.ContainsKey(p.Parameter.Id) ? p.Latest?.ToString(CultureInfo.InvariantCulture) ?? "" : "")));
                frames++;
                var seconds = (DateTime.UtcNow - started).TotalSeconds;
                if (seconds > 0) Rate = $"{frames / seconds:0.0} updates/s";
                // Pace to about 25 updates a second at most. A read that already took that long (many values, a slow
                // module) goes straight on: the old fixed 50 ms pause on top of every read halved the rate for nothing.
                var took = System.Diagnostics.Stopwatch.GetElapsedTime(tick).TotalMilliseconds;
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(40 - took, 5, 40)), _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private async Task SaveRecordingAsync()
    {
        if (_recording is null) return;
        var text = _recording.ToString();
        // Keep the recording until it's really saved: cancelling the dialog, or a save that fails, loses nothing.
        if (!await _dialogs.SaveTextAsync("Save live data recording", $"klemme15-live-{DateTime.Now:yyyyMMdd-HHmm}.csv", "csv", text)) return;
        _recording = null;
        _autosave?.Discard();   // it's saved where the owner wants it; the safety copy isn't needed any more
        _autosave = null;
        Raise(nameof(IsRecording));
        RaiseCommands();
    }

    private void RaiseCommands()
    {
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        RecordCommand.RaiseCanExecuteChanged();
        SaveRecordingCommand.RaiseCanExecuteChanged();
        AnalyseCommand.RaiseCanExecuteChanged();
    }
}
