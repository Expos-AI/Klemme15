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
    private double? _min, _max;
    private IReadOnlyList<double> _points = [];

    public LiveParameter Parameter { get; } = parameter;
    public string Name => Parameter.Name;
    public string Unit => Parameter.Unit;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public string Value { get => _value; private set => Set(ref _value, value); }
    public IReadOnlyList<double> Points { get => _points; private set => Set(ref _points, value); }
    public string Range => _min is null ? "" : $"min {_min:0.#}  max {_max:0.#}";
    public double? Latest { get; private set; }

    public void Update(LiveValue v)
    {
        Value = v.Value is { } d ? d.ToString(Math.Abs(d) >= 100 ? "0" : "0.#", CultureInfo.CurrentCulture) : v.Text ?? "--";
        Latest = v.Value;
        if (v.Value is not { } x) return;
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
        _recording?.AppendLine("time," + string.Join(",", _columns.Select(p => $"\"{p.Name} ({p.Unit})\"")));
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
            if (_running) StartColumns();
            Raise(nameof(IsRecording));
            RaiseCommands();
        }, () => _recording is null);
        SaveRecordingCommand = new AsyncCommand(SaveRecordingAsync, () => _recording is not null);
        AnalyseCommand = new RelayCommand(() => { if (_recording is { } rec) AnalyseRecording?.Invoke(rec.ToString()); },
            () => _recording is not null && AnalyseRecording is not null);
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
    public string EmptyText => _session.IsConnected
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
                var values = await backend.ReadLiveAsync(engine, selected.Select(p => p.Parameter).ToList(), _cts.Token);
                foreach (var v in values)
                    if (byId.TryGetValue(v.Parameter.Id, out var vm)) vm.Update(v);
                _session.LiveBuffer.Add(DateTimeOffset.Now, selected.ToDictionary(p => string.IsNullOrEmpty(p.Parameter.Unit) ? p.Name : $"{p.Name} ({p.Parameter.Unit})", p => p.Latest));
                _recording?.AppendLine(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "," +
                    string.Join(",", _columns.Select(p => byId.ContainsKey(p.Parameter.Id) ? p.Latest?.ToString(CultureInfo.InvariantCulture) ?? "" : "")));
                frames++;
                var seconds = (DateTime.UtcNow - started).TotalSeconds;
                if (seconds > 0) Rate = $"{frames / seconds:0.0} updates/s";
                await Task.Delay(50, _cts.Token);
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
        _recording = null;
        Raise(nameof(IsRecording));
        RaiseCommands();
        await _dialogs.SaveTextAsync("Save live data recording", $"klemme15-live-{DateTime.Now:yyyyMMdd-HHmm}.csv", "csv", text);
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
