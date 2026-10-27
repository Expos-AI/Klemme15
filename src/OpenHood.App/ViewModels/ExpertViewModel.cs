using System.Collections.ObjectModel;
using Avalonia.Media;
using OpenHood.App.Services;
using OpenHood.Core;

namespace OpenHood.App.ViewModels;

public sealed class JobResultRowViewModel(int set, string name, string value, Action<JobResultRowViewModel> addToLive) : ViewModelBase
{
    private bool _added;
    public int SetIndex { get; } = set;
    public string Name { get; } = name;
    public string Value { get; } = value;
    public bool IsNumeric => double.TryParse(Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
    public bool Added { get => _added; set { if (Set(ref _added, value)) Raise(nameof(CanAdd)); } }
    public bool CanAdd => IsNumeric && !_added;
    private RelayCommand? _addToLive;
    public RelayCommand AddToLiveCommand => _addToLive ??= new RelayCommand(() => addToLive(this));
}

/// <summary>Tool32-style job runner for EDIABAS mode: list jobs, run any job, pin results to Live data.</summary>
public sealed class ExpertViewModel : PageViewModel
{
    private readonly SessionState _session;
    private readonly AppSettings _settings;
    private string _sgbd = "";
    private string? _job;
    private string _arguments = "";
    private string? _error;
    private string _status = "";

    public ExpertViewModel(SessionState session, AppSettings settings)
    {
        _session = session;
        _settings = settings;
        ListJobsCommand = new AsyncCommand(ListJobsAsync, () => Runner is not null && !string.IsNullOrWhiteSpace(_sgbd), e => Error = e.Message);
        RunCommand = new AsyncCommand(RunAsync, () => Runner is not null && !string.IsNullOrWhiteSpace(_sgbd) && !string.IsNullOrWhiteSpace(_job), e => Error = e.Message);
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(SessionState.Backend)) return;
            Raise(nameof(Available));
            Raise(nameof(NotAvailable));
            ListJobsCommand.RaiseCanExecuteChanged();
            RunCommand.RaiseCanExecuteChanged();
        };
    }

    public override string Title => "Expert";
    public override Geometry Icon => Icons.Expert;

    private IJobRunner? Runner => _session.Backend as IJobRunner;
    public bool Available => Runner is not null;
    public bool NotAvailable => Runner is null;

    public IReadOnlyList<string> KnownSgbds =>
        _session.Vehicle?.Modules.SelectMany(m => m.Sgbd).Distinct().ToList() ?? [];

    public string Sgbd
    {
        get => _sgbd;
        set
        {
            if (!Set(ref _sgbd, value)) return;
            ListJobsCommand.RaiseCanExecuteChanged();
            RunCommand.RaiseCanExecuteChanged();
        }
    }

    public ObservableCollection<string> Jobs { get; } = [];

    public string? Job
    {
        get => _job;
        set { if (Set(ref _job, value)) RunCommand.RaiseCanExecuteChanged(); }
    }

    public string Arguments { get => _arguments; set => Set(ref _arguments, value); }
    public ObservableCollection<JobResultRowViewModel> Results { get; } = [];
    public string Status { get => _status; private set => Set(ref _status, value); }

    public string? Error
    {
        get => _error;
        set { if (Set(ref _error, value)) Raise(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);

    public AsyncCommand ListJobsCommand { get; }
    public AsyncCommand RunCommand { get; }

    public override void OnShown()
    {
        Raise(nameof(Available));
        Raise(nameof(NotAvailable));
        Raise(nameof(KnownSgbds));
    }

    private async Task ListJobsAsync()
    {
        Error = null;
        Jobs.Clear();
        foreach (var j in await Runner!.ListJobsAsync(_sgbd.Trim())) Jobs.Add(j);
        Status = $"{Jobs.Count} jobs in {_sgbd}";
        Job ??= Jobs.FirstOrDefault(j => j.Equals("IDENT", StringComparison.OrdinalIgnoreCase));
    }

    private async Task RunAsync()
    {
        Error = null;
        Results.Clear();
        var started = DateTime.UtcNow;
        var sets = await Runner!.RunJobAsync(_sgbd.Trim(), _job!.Trim(), _arguments);
        for (var i = 0; i < sets.Count; i++)
            foreach (var kv in sets[i])
                Results.Add(new JobResultRowViewModel(i, kv.Key, kv.Value, AddToLive));
        Status = $"{_job}: {sets.Count} result set(s) in {(DateTime.UtcNow - started).TotalMilliseconds:0} ms";
    }

    /// <summary>Pin a numeric job result as a live value. Takes effect on next connect.</summary>
    private void AddToLive(JobResultRowViewModel row)
    {
        var source = $"ediabas:{_sgbd.Trim()}/{_job!.Trim()}/{row.Name}" + (string.IsNullOrWhiteSpace(_arguments) ? "" : "/" + _arguments);
        if (_settings.LiveFavorites.All(f => f.Source != source))
        {
            _settings.LiveFavorites.Add(new AppSettings.FavoriteParameter { Id = source, Name = row.Name, Source = source });
            _settings.Save();
        }
        row.Added = true;
        Status = $"Pinned {row.Name}. Reconnect to see it on the Live data page.";
    }
}
