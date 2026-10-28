using System.Collections.ObjectModel;
using Avalonia.Media;
using Klemme15.Core;
using Klemme15.Data;

namespace Klemme15.App.ViewModels;

/// <summary>Where a guide step's buttons take the user.</summary>
public sealed record GuideActions(Action<string, string> OpenReadings, Action OpenLive);

public sealed class FaultGroupViewModel : ViewModelBase
{
    private readonly GuideActions? _guideActions;
    private readonly SessionState _session;
    private readonly Action<Fault, EcuDefinition> _showOnCar;
    private readonly Action<string> _openUrl;
    private bool _confirming;
    private string? _message;

    public FaultGroupViewModel(SessionState session, EcuDefinition ecu, IReadOnlyList<Fault> faults, Action<Fault, EcuDefinition> showOnCar, Action<string> openUrl, GuideActions? guideActions = null)
    {
        _guideActions = guideActions;
        _session = session;
        _showOnCar = showOnCar;
        _openUrl = openUrl;
        Ecu = ecu;
        Fill(faults);
        AskClearCommand = new RelayCommand(() => Confirming = true);
        CancelClearCommand = new RelayCommand(() => Confirming = false);
        ClearCommand = new AsyncCommand(ClearAsync, null, e => Message = e.Message);
    }

    public EcuDefinition Ecu { get; }

    /// <summary>Opens this module on the Readings page (set when readings are available).</summary>
    public RelayCommand? ReadingsCommand { get; set; }
    public bool CanOpenReadings => ReadingsCommand is not null && _session.Readings is not null;
    public string Name => Ecu.Name;
    public string Address => Ecu.AddressHex;
    public ObservableCollection<FaultItemViewModel> Faults { get; } = [];
    public string CountText => Faults.Count == 1 ? "1 fault" : $"{Faults.Count} faults";

    public bool Confirming
    {
        get => _confirming;
        set { if (Set(ref _confirming, value)) Raise(nameof(NotConfirming)); }
    }

    public bool NotConfirming => !_confirming;

    public string? Message
    {
        get => _message;
        set { if (Set(ref _message, value)) Raise(nameof(HasMessage)); }
    }

    public bool HasMessage => !string.IsNullOrEmpty(_message);

    public RelayCommand AskClearCommand { get; }
    public RelayCommand CancelClearCommand { get; }
    public AsyncCommand ClearCommand { get; }

    private void Fill(IEnumerable<Fault> faults)
    {
        Faults.Clear();
        foreach (var f in faults.OrderBy(f => f.IsInfo).ThenByDescending(f => f.State == FaultState.Present))
        {
            var where = _session.Locate(f, Ecu);
            var item = new FaultItemViewModel(f, Ecu, where?.Title, where?.IsComponent == true)
            {
                SourceLinks = f.Sources.Where(u => u.StartsWith("http", StringComparison.Ordinal))
                                       .Select(u => new SourceLink(u, _openUrl)).DistinctBy(l => l.Label).ToList(),
            };
            item.ShowCommand = new RelayCommand(() => _showOnCar(f, Ecu));
            item.TranslateCommand = new RelayCommand(() => _openUrl(item.TranslateUrl));
            if (_session.Database.Guides.For(f, Ecu) is { } guide) item.Guide = BuildGuide(guide);
            Faults.Add(item);
        }
        Raise(nameof(CountText));
    }

    private GuideViewModel BuildGuide(Guide guide)
    {
        var modules = _session.Vehicle?.Modules ?? [];
        var steps = guide.StepsFor(_session.Model?.EngineCode).Select((step, i) =>
        {
            // "DME" in a guide means this car family's engine module (E9x.DME, E46.DME).
            var target = step.Module is { } m ? modules.FirstOrDefault(x => x.Id.EndsWith("." + m, StringComparison.OrdinalIgnoreCase)) : null;
            RelayCommand? readings = target is not null && step.Readings is { } filter && _guideActions is { } a && _session.Readings is not null
                ? new RelayCommand(() => a.OpenReadings(target.Id, filter)) : null;
            RelayCommand? live = step.Live && _guideActions is { } b && _session.IsConnected ? new RelayCommand(b.OpenLive) : null;
            return new GuideStepViewModel(i + 1, step.Text, readings, live);
        }).ToList();
        return new GuideViewModel(guide.Title, guide.Summary, guide.Safety, steps);
    }

    private async Task ClearAsync()
    {
        Confirming = false;
        if (_session.Backend is not { } backend) return;
        await backend.ClearFaultsAsync(Ecu);
        var identity = _session.LastScan?.Ecus.FirstOrDefault(e => e.Ecu.Id == Ecu.Id)?.Identity;
        var remaining = new VehicleScanner(backend, _session.Codes).Describe(await backend.ReadFaultsAsync(Ecu), Ecu, identity);
        Fill(remaining);
        Message = remaining.Count == 0
            ? "Fault memory cleared. Drive the car, then read it again to see which faults come back."
            : "Cleared, but these came straight back: they are still active.";
    }
}

public sealed class FaultsViewModel : PageViewModel
{
    private readonly SessionState _session;
    private readonly Action<Fault, EcuDefinition> _showOnCar;
    private readonly Action<string> _openUrl;
    private string? _error;

    public FaultsViewModel(SessionState session, Action<Fault, EcuDefinition> showOnCar, Action<string> openUrl)
    {
        _session = session;
        _showOnCar = showOnCar;
        _openUrl = openUrl;
        RefreshCommand = new AsyncCommand(RefreshAsync, () => _session.IsConnected && _session.LastScan is not null, e => Error = e.Message);
        _session.ScanUpdated += () => OnUi(Rebuild);
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionState.Backend) or nameof(SessionState.LastScan)) RefreshCommand.RaiseCanExecuteChanged();
        };
    }

    public override string Title => "Faults";
    public override Geometry Icon => Icons.Faults;

    public ObservableCollection<FaultGroupViewModel> Groups { get; } = [];

    /// <summary>Navigates to the Readings page for a module.</summary>
    public Action<EcuDefinition>? OpenReadings { get; set; }

    /// <summary>Navigation for the buttons in troubleshooting guides.</summary>
    public GuideActions? GuideActions { get; set; }

    public bool IsEmpty => Groups.Count == 0;
    public int ActiveCount => Groups.Sum(g => g.Faults.Count(f => f.IsActive));
    public int StoredCount => Groups.Sum(g => g.Faults.Count(f => f.IsStored));
    public int InfoCount => Groups.Sum(g => g.Faults.Count(f => f.IsInfo));
    public string Headline => IsEmpty ? "No faults"
        : $"{ActiveCount} active now, {StoredCount} stored" + (InfoCount > 0 ? $", {InfoCount} in info memory" : "");

    public string EmptyText => _session.LastScan is null
        ? "Run a quick test on the Vehicle page. Every fault the modules report will be listed here."
        : "No faults in any module that answered.";

    public string? Error
    {
        get => _error;
        set { if (Set(ref _error, value)) Raise(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);
    public AsyncCommand RefreshCommand { get; }

    private void Rebuild()
    {
        Groups.Clear();
        if (_session.LastScan is { } scan)
            foreach (var e in scan.Ecus.Where(e => e.Faults.Count > 0).OrderByDescending(e => e.Faults.Any(f => f.State == FaultState.Present)))
            {
                var ecu = e.Ecu;
                Groups.Add(new FaultGroupViewModel(_session, ecu, e.Faults, _showOnCar, _openUrl, GuideActions)
                {
                    ReadingsCommand = OpenReadings is { } open ? new RelayCommand(() => open(ecu)) : null,
                });
            }
        foreach (var p in new[] { nameof(IsEmpty), nameof(EmptyText), nameof(Headline), nameof(ActiveCount), nameof(StoredCount) })
            Raise(p);
    }

    /// <summary>Re-read fault memory of every module that answered in the last scan.</summary>
    private async Task RefreshAsync()
    {
        Error = null;
        if (_session.Backend is not { } backend || _session.LastScan is not { } scan) return;
        var scanner = new VehicleScanner(backend, _session.Codes);
        var updated = new List<EcuScanResult>();
        foreach (var e in scan.Ecus)
        {
            if (e.Status is EcuScanStatus.Ok or EcuScanStatus.Faults && e.Note is null)
            {
                var faults = scanner.Describe(await backend.ReadFaultsAsync(e.Ecu), e.Ecu, e.Identity);
                updated.Add(e with { Faults = faults, Status = faults.Count > 0 ? EcuScanStatus.Faults : EcuScanStatus.Ok });
            }
            else updated.Add(e);
        }
        _session.PublishScan(scan with { Ecus = updated });
    }
}
