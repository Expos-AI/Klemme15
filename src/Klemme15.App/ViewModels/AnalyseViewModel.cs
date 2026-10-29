using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Threading;
using Klemme15.App.Services;
using Klemme15.Core;

namespace Klemme15.App.ViewModels;

/// <summary>A channel the user can pick for an axis or a value.</summary>
public sealed class ChannelChoice(LogChannel channel)
{
    public LogChannel Channel { get; } = channel;
    public string Label => Channel.Unit.Length > 0 ? $"{Channel.Name} ({Channel.Unit})" : Channel.Name;
    public override string ToString() => Label;
}

/// <summary>The value of one channel at the current playback position, for the readout beside the plots.</summary>
public sealed class ChannelReadoutViewModel(LogChannel channel) : ViewModelBase
{
    private string _value = "–";
    public string Name => channel.Name;
    public string Unit => channel.Unit;
    public string Value { get => _value; private set => Set(ref _value, value); }

    public void ShowSample(int i)
    {
        var v = i >= 0 && i < channel.Samples.Count ? channel.Samples[i] : null;
        Value = v is null ? "–" : v.Value.ToString(v.Value is > -10 and < 10 ? "0.0#" : "0.#", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Analyse: load a CSV datalog (recorded by Klemme15 or any tool), scatter one channel against another (fuel trims vs
/// RPM or load), see a value averaged over a tuning-style map, and play the log back with a scrubber while every channel
/// reads out in step. Nothing here touches the car.
/// </summary>
public sealed class AnalyseViewModel : PageViewModel
{
    private readonly IDialogService _dialogs;
    private DataLog _log = new([], []);
    private string _status = "Load a CSV you recorded on the Live data page, or any datalog with a time column first.";
    private string? _fileName;
    private DispatcherTimer? _timer;
    private int _position;
    private bool _playing;
    private double _speed = 1;

    public AnalyseViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        OpenCommand = new AsyncCommand(OpenAsync, null, e => Status = e.Message);
        PlayCommand = new RelayCommand(TogglePlay, () => HasData);
        var speeds = new[] { 0.5, 1, 2, 4 };
        CycleSpeedCommand = new RelayCommand(() => { Speed = speeds[(Array.IndexOf(speeds, _speed) + 1) % speeds.Length]; });
    }

    public override string Title => "Analyse";
    public override Geometry Icon => Icons.Analyse;

    public AsyncCommand OpenCommand { get; }
    public RelayCommand PlayCommand { get; }
    public RelayCommand CycleSpeedCommand { get; }

    public ObservableCollection<ChannelChoice> Channels { get; } = [];
    public ObservableCollection<ChannelReadoutViewModel> Readouts { get; } = [];

    public bool HasData => _log.Count > 0;
    public bool NoData => !HasData;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string FileTitle => _fileName is null ? "" : $"{_fileName} · {_log.Count} samples · {_log.Duration.TotalSeconds:0.#} s";
    public bool HasFile => _fileName is not null;

    private ChannelChoice? _scatterX, _scatterY, _mapX, _mapY, _mapValue;

    public ChannelChoice? ScatterX { get => _scatterX; set { if (Set(ref _scatterX, value)) RaiseScatter(); } }
    public ChannelChoice? ScatterY { get => _scatterY; set { if (Set(ref _scatterY, value)) RaiseScatter(); } }
    public ChannelChoice? MapX { get => _mapX; set { if (Set(ref _mapX, value)) RaiseMap(); } }
    public ChannelChoice? MapY { get => _mapY; set { if (Set(ref _mapY, value)) RaiseMap(); } }
    public ChannelChoice? MapValue { get => _mapValue; set { if (Set(ref _mapValue, value)) RaiseMap(); } }

    public IReadOnlyList<ScatterPoint> ScatterPoints =>
        _scatterX is { } x && _scatterY is { } y ? _log.Scatter(x.Channel, y.Channel) : [];

    public Bin[,]? MapBins =>
        _mapX is { } x && _mapY is { } y && _mapValue is { } v ? _log.Map(x.Channel, y.Channel, v.Channel) : null;

    public string ScatterCaption => _scatterX is { } x && _scatterY is { } y ? $"{y.Channel.Name} vs {x.Channel.Name}" : "";
    public string MapCaption => _mapX is { } x && _mapY is { } y && _mapValue is { } v
        ? $"{v.Channel.Name}: average across {x.Channel.Name} (→) and {y.Channel.Name} (↑)" : "";
    public string MapAxes => _mapX is { } x && _mapY is { } y ? $"→ {x.Label}      ↑ {y.Label}" : "";

    /// <summary>Whether the map's value is a fuel trim, so the colours centre on zero.</summary>
    public bool MapDiverging => _mapValue?.Channel.Name.Contains("trim", StringComparison.OrdinalIgnoreCase) ?? false;

    public int Position
    {
        get => _position;
        set
        {
            if (!Set(ref _position, Math.Clamp(value, 0, Math.Max(0, _log.Count - 1)))) return;
            Raise(nameof(PositionTime));
            Raise(nameof(Highlight));
            foreach (var r in Readouts) r.ShowSample(_position);
        }
    }

    public int MaxPosition => Math.Max(0, _log.Count - 1);
    public int Highlight => _position;
    public string PositionTime => _log.Count == 0 ? "0.0 s" : $"{_log.Times[Math.Clamp(_position, 0, _log.Count - 1)].TotalSeconds:0.0} s";
    public bool Playing { get => _playing; private set { if (Set(ref _playing, value)) Raise(nameof(PlayLabel)); } }
    public string PlayLabel => _playing ? "Pause" : "Play";
    public double Speed { get => _speed; private set { if (Set(ref _speed, value)) Raise(nameof(SpeedLabel)); } }
    public string SpeedLabel => $"{Speed:0.#}×";

    private void RaiseScatter() { Raise(nameof(ScatterPoints)); Raise(nameof(ScatterCaption)); }
    private void RaiseMap() { Raise(nameof(MapBins)); Raise(nameof(MapCaption)); Raise(nameof(MapAxes)); Raise(nameof(MapDiverging)); }

    public override void OnHidden() => Pause();

    private async Task OpenAsync()
    {
        var text = await _dialogs.OpenTextAsync("Open a datalog (CSV)", "CSV datalog", ["*.csv", "*.txt"]);
        if (text is null) return;
        var log = DataLog.Parse(text);
        if (log.Count == 0) { Status = "That file has no rows Klemme15 could read. It needs a header and a time column first."; return; }
        Load(log, "datalog.csv");
    }

    /// <summary>Loads a parsed log and picks sensible default axes (RPM, load, the first fuel trim).</summary>
    public void Load(DataLog log, string fileName)
    {
        Pause();
        _log = log;
        _fileName = fileName;
        Channels.Clear();
        foreach (var c in log.Channels.Where(c => c.HasData)) Channels.Add(new ChannelChoice(c));
        Readouts.Clear();
        foreach (var c in log.Channels.Where(c => c.HasData)) Readouts.Add(new ChannelReadoutViewModel(c));

        ChannelChoice? Find(params string[] names) => Channels.FirstOrDefault(c => names.Any(n => c.Channel.Name.Contains(n, StringComparison.OrdinalIgnoreCase)));
        var rpm = Find("speed", "rpm", "drehzahl") ?? Channels.FirstOrDefault();
        var load = Find("load", "last") ?? Channels.Skip(1).FirstOrDefault() ?? rpm;
        var trim = Find("trim", "integrator", "adaption") ?? load;
        _scatterX = rpm; _scatterY = trim;
        _mapX = rpm; _mapY = load; _mapValue = trim;

        Position = 0;
        foreach (var r in Readouts) r.ShowSample(0);
        Status = "";
        foreach (var p in new[] { nameof(HasData), nameof(NoData), nameof(FileTitle), nameof(HasFile), nameof(MaxPosition),
                     nameof(ScatterX), nameof(ScatterY), nameof(MapX), nameof(MapY), nameof(MapValue) })
            Raise(p);
        RaiseScatter(); RaiseMap();
        PlayCommand.RaiseCanExecuteChanged();
    }

    private void TogglePlay()
    {
        if (_playing) { Pause(); return; }
        if (_log.Count == 0) return;
        if (_position >= _log.Count - 1) Position = 0;
        Playing = true;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        var last = DateTime.UtcNow;
        _timer.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow;
            var dt = (now - last).TotalSeconds * _speed;
            last = now;
            var target = _log.Times[Math.Clamp(_position, 0, _log.Count - 1)] + TimeSpan.FromSeconds(dt);
            var next = _log.SampleAt(target);
            if (next <= _position && _position < _log.Count - 1) next = _position + 1; // always creep forward
            Position = next;
            if (_position >= _log.Count - 1) Pause();
        };
        _timer.Start();
    }

    private void Pause()
    {
        _timer?.Stop();
        _timer = null;
        Playing = false;
    }
}
