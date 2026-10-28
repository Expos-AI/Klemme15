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

    public LiveViewModel(SessionState session, IDialogService dialogs)
    {
        _session = session;
        _dialogs = dialogs;
        StartCommand = new AsyncCommand(RunAsync, () => _session.IsConnected && !_running && Parameters.Any(p => p.IsSelected), e => Error = e.Message);
        StopCommand = new RelayCommand(Stop, () => _running);
        RecordCommand = new RelayCommand(() =>
        {
            _recording = new StringBuilder();
            if (_running) _recording.AppendLine("time," + string.Join(",", Selected.Select(p => $"\"{p.Name} ({p.Unit})\"")));
            Raise(nameof(IsRecording));
            RaiseCommands();
        }, () => _recording is null);
        SaveRecordingCommand = new AsyncCommand(SaveRecordingAsync, () => _recording is not null);
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionState.Backend)) OnUi(LoadParameters);
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
        ? "This backend offers no live values for the engine yet. In EDIABAS mode, add values from the Expert page."
        : "Connect to the car first.";

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
        var byId = selected.ToDictionary(p => p.Parameter.Id);
        _recording?.AppendLine("time," + string.Join(",", selected.Select(p => $"\"{p.Name} ({p.Unit})\"")));

        _cts = new CancellationTokenSource();
        IsRunning = true;
        var frames = 0;
        var started = DateTime.UtcNow;
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var values = await backend.ReadLiveAsync(engine, selected.Select(p => p.Parameter).ToList(), _cts.Token);
                foreach (var v in values)
                    if (byId.TryGetValue(v.Parameter.Id, out var vm)) vm.Update(v);
                _recording?.AppendLine(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "," +
                    string.Join(",", selected.Select(p => p.Latest?.ToString(CultureInfo.InvariantCulture) ?? "")));
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
    }
}
