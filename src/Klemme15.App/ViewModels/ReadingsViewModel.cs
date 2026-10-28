using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Klemme15.App.Services;
using Klemme15.Core;
using Klemme15.Data;

namespace Klemme15.App.ViewModels;

/// <summary>A module you can read from.</summary>
public sealed class ReadingModuleViewModel(EcuDefinition ecu, bool answered) : ViewModelBase
{
    private bool _isSelected;
    public EcuDefinition Ecu { get; } = ecu;
    public string Name => Ecu.Name;
    public string Address => Ecu.AddressHex;
    public bool Answered { get; } = answered;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public RelayCommand? SelectCommand { get; set; }
}

/// <summary>One value from a reading, labelled in English.</summary>
public sealed class ReadingValueViewModel : ViewModelBase
{
    private readonly Action<ReadingValueViewModel>? _pin;
    private bool _pinned;

    public ReadingValueViewModel(ReadingValue value, Action<ReadingValueViewModel>? pin)
    {
        Value = value;
        _pin = pin;
        Label = ReadingLabels.ResultLabel(value.Name, value.Comment);
        PinCommand = new RelayCommand(() => _pin?.Invoke(this));
    }

    public ReadingValue Value { get; }
    public string Label { get; }
    public string Name => Value.Name;
    public string Text => Value.Number is { } d ? d.ToString(Math.Abs(d) >= 1000 ? "0" : "0.###", CultureInfo.CurrentCulture) : ReadingLabels.ValueText(Value.Text);
    public string Unit => Value.Unit ?? "";
    public string? Comment => Value.Comment;
    public bool CanPin => _pin is not null && Value.Number is not null && !_pinned;
    public bool IsPinned { get => _pinned; set { Set(ref _pinned, value); Raise(nameof(CanPin)); } }
    public RelayCommand PinCommand { get; }
}

/// <summary>One status reading (a BMW job) and its latest values.</summary>
public sealed class ReadingItemViewModel : ViewModelBase
{
    private readonly Func<ReadingItemViewModel, Task> _read;
    private bool _busy, _watch;
    private string? _error;
    private string _updated = "";

    public ReadingItemViewModel(ModuleReading reading, Func<ReadingItemViewModel, Task> read)
    {
        Reading = reading;
        _read = read;
        Title = ReadingLabels.JobTitle(reading.Job, reading.Comment);
        Group = ReadingLabels.GroupOf(reading.Job, reading.Comment);
        ReadCommand = new AsyncCommand(() => _read(this), () => !_busy);
        Values.CollectionChanged += (_, _) => Raise(nameof(HasValues));
    }

    public ModuleReading Reading { get; }
    public string Job => Reading.Job;
    public string Title { get; }
    public string Group { get; }

    /// <summary>BMW's own description, shown as a tooltip.</summary>
    public string Tooltip => Reading.Comment is { } c ? $"{Reading.Job}: {c}" : Reading.Job;

    public ObservableCollection<ReadingValueViewModel> Values { get; } = [];
    public bool HasValues => Values.Count > 0;
    public string? Error { get => _error; set { Set(ref _error, value); Raise(nameof(HasError)); } }
    public bool HasError => _error is not null;
    public string Updated { get => _updated; set => Set(ref _updated, value); }
    public bool IsBusy { get => _busy; set { Set(ref _busy, value); ReadCommand.RaiseCanExecuteChanged(); } }

    /// <summary>Included in auto-refresh.</summary>
    public bool Watch { get => _watch; set => Set(ref _watch, value); }

    public AsyncCommand ReadCommand { get; }

    public bool Matches(string filter) =>
        filter.Length == 0
        || Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Job.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Group.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || (Reading.Comment?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
}

public sealed class ReadingGroupViewModel(string name, IReadOnlyList<ReadingItemViewModel> items)
{
    public string Name { get; } = name;
    public IReadOnlyList<ReadingItemViewModel> Items { get; } = items;
    public string Count => Items.Count == 1 ? "1 reading" : $"{Items.Count} readings";
}

/// <summary>
/// Readings: every status value a module offers (sensor values, switch states, adaptation values), computed by
/// BMW's own job scripts from the user's files, grouped and labelled in English. Read-only: nothing is changed on the car.
/// </summary>
public sealed class ReadingsViewModel : PageViewModel
{
    private readonly SessionState _session;
    private readonly AppSettings _settings;
    private ReadingModuleViewModel? _module;
    private List<ReadingItemViewModel> _all = [];
    private string _filter = "";
    private string _status = "";
    private string? _error;
    private bool _loading, _autoRefresh;
    private CancellationTokenSource? _cts;

    public ReadingsViewModel(SessionState session, AppSettings settings)
    {
        _session = session;
        _settings = settings;
        ReadAllCommand = new AsyncCommand(ReadAllAsync, () => _session.Readings is not null && _all.Count > 0 && !_loading);
        StopCommand = new RelayCommand(() => { _cts?.Cancel(); AutoRefresh = false; }, () => _cts is not null);
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionState.Backend) or nameof(SessionState.Readings)) OnUi(Reload);
        };
        _session.ScanUpdated += () => OnUi(RebuildModules);
    }

    public override string Title => "Readings";
    public override Geometry Icon => Icons.Readings;

    public ObservableCollection<ReadingModuleViewModel> Modules { get; } = [];
    public ObservableCollection<ReadingGroupViewModel> Groups { get; } = [];

    public bool Available => _session.Readings is not null;
    public bool NotAvailable => _session.IsConnected && !Available;
    public bool NotConnected => !_session.IsConnected;
    public bool HasModule => _module is not null && Available;

    public string ModuleTitle => _module?.Name ?? "";
    public string VariantText => _module is null || _session.Readings?.VariantOf(_module.Ecu) is not { } v ? ""
        : _session.IsDemo ? "Demo values (made up, so you can try the page)"
        : _session.Readings is Klemme15.Protocols.NativeReadings
            ? $"Module version {v}: values read by Klemme15 using BMW's measurement table from the database you downloaded"
            : $"Module version {v}: values computed by BMW's own scripts in your files";

    public string Filter
    {
        get => _filter;
        set { if (Set(ref _filter, value ?? "")) Regroup(); }
    }

    public string Status { get => _status; private set => Set(ref _status, value); }
    public string? Error { get => _error; private set { Set(ref _error, value); Raise(nameof(HasError)); } }
    public bool HasError => _error is not null;
    public bool IsLoading { get => _loading; private set { Set(ref _loading, value); ReadAllCommand.RaiseCanExecuteChanged(); } }

    /// <summary>Re-reads the watched readings (or every visible one if none are watched) every second.</summary>
    public bool AutoRefresh
    {
        get => _autoRefresh;
        set
        {
            if (!Set(ref _autoRefresh, value)) return;
            if (value) _ = RefreshLoopAsync();
            else _cts?.Cancel();
        }
    }

    public AsyncCommand ReadAllCommand { get; }
    public RelayCommand StopCommand { get; }

    /// <summary>Stop talking to the module when the user leaves the page, so other pages have the bus.</summary>
    public override void OnHidden()
    {
        AutoRefresh = false;
        _cts?.Cancel();
    }

    public override void OnShown()
    {
        if (Modules.Count == 0) Reload();
    }

    /// <summary>Selects a module by id (used by "Readings" on the Faults page).</summary>
    public void ShowModule(string ecuId, string? filter = null)
    {
        if (Modules.Count == 0) Reload();
        if (filter is not null) Filter = filter;
        if (Modules.FirstOrDefault(m => m.Ecu.Id == ecuId) is { } module && module != _module) Select(module);
    }

    private void Reload()
    {
        AutoRefresh = false;
        RebuildModules();
        Raise(nameof(Available));
        Raise(nameof(NotAvailable));
        Raise(nameof(NotConnected));
        Raise(nameof(HasModule));
    }

    private void RebuildModules()
    {
        var keep = _module?.Ecu.Id;
        Modules.Clear();
        if (_session.Vehicle is not { } vehicle || _session.Readings is null) { Select(null); return; }

        var scan = _session.LastScan;
        bool Answered(EcuDefinition e) => scan?.Ecus.Any(r => r.Ecu.Id == e.Id && r.Status is EcuScanStatus.Ok or EcuScanStatus.Faults) ?? false;
        var readings = _session.Readings;
        var modules = vehicle.Modules
            .Where(m => (scan is null || Answered(m)) && readings!.Supports(m))
            .OrderByDescending(m => m.IsEngine).ThenBy(m => m.Category).ThenBy(m => m.Name)
            .Select(m => new ReadingModuleViewModel(m, Answered(m)))
            .ToList();
        foreach (var m in modules)
        {
            m.SelectCommand = new RelayCommand(() => Select(m));
            Modules.Add(m);
        }
        Status = scan is null ? "Tip: run the quick test first, so only the modules your car has are listed." : "";
        Select(Modules.FirstOrDefault(m => m.Ecu.Id == keep) ?? Modules.FirstOrDefault());
    }

    private async void Select(ReadingModuleViewModel? module)
    {
        AutoRefresh = false;
        foreach (var m in Modules) m.IsSelected = m == module;
        _module = module;
        _all = [];
        Groups.Clear();
        Error = null;
        Raise(nameof(HasModule));
        Raise(nameof(ModuleTitle));
        Raise(nameof(VariantText));
        if (module is null || _session.Readings is not { } readings) return;

        IsLoading = true;
        Status = $"Finding the readings {module.Name} offers...";
        try
        {
            var list = await readings.ListReadingsAsync(module.Ecu);
            if (_module != module) return;
            _all = list.Select(r => new ReadingItemViewModel(r, item => ReadOneAsync(module.Ecu, item))).ToList();
            Regroup();
            Raise(nameof(VariantText));
            Status = _all.Count == 0
                ? $"{module.Name} offers no status readings without extra input."
                : $"{_all.Count} readings. Press Read on any of them, or Read all.";
        }
        catch (Exception e)
        {
            Error = $"Couldn't list the readings: {e.Message}";
            Status = "";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Regroup()
    {
        Groups.Clear();
        var visible = _all.Where(i => i.Matches(_filter.Trim())).ToList();
        foreach (var name in ReadingLabels.GroupOrder)
        {
            var items = visible.Where(i => i.Group == name).OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (items.Count > 0) Groups.Add(new ReadingGroupViewModel(name, items));
        }
    }

    private async Task ReadOneAsync(EcuDefinition ecu, ReadingItemViewModel item, CancellationToken ct = default)
    {
        if (_session.Readings is not { } readings) return;
        item.IsBusy = true;
        try
        {
            var values = await readings.ReadAsync(ecu, item.Job, ct);
            OnUi(() =>
            {
                item.Values.Clear();
                foreach (var v in values)
                {
                    var source = readings.PinSource(ecu, item.Job, v.Name);
                    var canPin = source.StartsWith("ediabas:", StringComparison.Ordinal);
                    var vm = new ReadingValueViewModel(v, canPin ? Pin : null) { IsPinned = _settings.LiveFavorites.Any(f => f.Source == source) };
                    item.Values.Add(vm);
                }
                item.Error = values.Count == 0 ? "No values returned." : null;
                item.Updated = DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            OnUi(() => item.Error = e.Message);
        }
        finally
        {
            OnUi(() => item.IsBusy = false);
        }

        void Pin(ReadingValueViewModel value)
        {
            var source = readings.PinSource(ecu, item.Job, value.Name);
            if (_settings.LiveFavorites.All(f => f.Source != source))
            {
                _settings.LiveFavorites.Add(new AppSettings.FavoriteParameter { Id = source, Name = value.Label, Unit = value.Unit, Source = source });
                _settings.Save();
            }
            value.IsPinned = true;
            Status = $"Pinned {value.Label}. It appears on the Live data page after you reconnect.";
        }
    }

    private async Task ReadAllAsync()
    {
        if (_module is not { } module) return;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        StopCommand.RaiseCanExecuteChanged();
        var items = Groups.SelectMany(g => g.Items).ToList();
        var done = 0;
        try
        {
            foreach (var item in items)
            {
                cts.Token.ThrowIfCancellationRequested();
                Status = $"Reading {++done} of {items.Count}: {item.Title}";
                await ReadOneAsync(module.Ecu, item, cts.Token);
            }
            Status = $"Read {items.Count} readings at {DateTime.Now:HH:mm:ss}.";
        }
        catch (OperationCanceledException) { Status = $"Stopped after {done - 1} readings."; }
        finally
        {
            if (_cts == cts) _cts = null;
            StopCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task RefreshLoopAsync()
    {
        if (_module is not { } module) { AutoRefresh = false; return; }
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        StopCommand.RaiseCanExecuteChanged();
        try
        {
            while (!cts.IsCancellationRequested && _module == module)
            {
                var watched = _all.Where(i => i.Watch).ToList();
                var items = watched.Count > 0 ? watched : Groups.SelectMany(g => g.Items).Where(i => i.HasValues).ToList();
                if (items.Count == 0) { Status = "Read something first, or tick Watch on the readings to refresh."; AutoRefresh = false; break; }
                foreach (var item in items) await ReadOneAsync(module.Ecu, item, cts.Token);
                Status = $"Refreshing {items.Count} reading(s), last at {DateTime.Now:HH:mm:ss}.";
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_cts == cts) _cts = null;
            StopCommand.RaiseCanExecuteChanged();
        }
    }
}
