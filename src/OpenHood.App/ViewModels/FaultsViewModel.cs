using System.Collections.ObjectModel;
using Avalonia.Media;
using OpenHood.Core;

namespace OpenHood.App.ViewModels;

public sealed class FaultRowViewModel(Fault fault) : ViewModelBase
{
    public Fault Fault { get; } = fault;
    public string Code => Fault.Code;
    public string Source => Fault.Source == FaultSource.Obd2 ? "OBD-II" : "BMW";
    public string State => Fault.State switch
    {
        FaultState.Present => "Present now",
        FaultState.Stored => "Stored",
        FaultState.Intermittent => "Intermittent",
        _ => "",
    };
    public IBrush StateBrush => Fault.State == FaultState.Present ? Brush.Parse("#D6463B") : Brush.Parse("#D98A10");
    public string Description => Fault.DisplayDescription;
    public string Details => string.Join("   ", Fault.Details.Select(kv => $"{kv.Key}: {kv.Value}"));
    public bool HasDetails => Fault.Details.Count > 0;
}

public sealed class FaultGroupViewModel : ViewModelBase
{
    private readonly SessionState _session;
    private bool _confirming;
    private string? _message;

    public FaultGroupViewModel(SessionState session, EcuDefinition ecu, IReadOnlyList<Fault> faults)
    {
        _session = session;
        Ecu = ecu;
        Faults = new ObservableCollection<FaultRowViewModel>(faults.Select(f => new FaultRowViewModel(f)));
        AskClearCommand = new RelayCommand(() => Confirming = true);
        CancelClearCommand = new RelayCommand(() => Confirming = false);
        ClearCommand = new AsyncCommand(ClearAsync, null, e => Message = e.Message);
    }

    public EcuDefinition Ecu { get; }
    public string Name => Ecu.Name;
    public string Address => Ecu.AddressHex;
    public ObservableCollection<FaultRowViewModel> Faults { get; }
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

    private async Task ClearAsync()
    {
        Confirming = false;
        if (_session.Backend is not { } backend) return;
        await backend.ClearFaultsAsync(Ecu);
        var remaining = await backend.ReadFaultsAsync(Ecu);
        Faults.Clear();
        foreach (var f in remaining) Faults.Add(new FaultRowViewModel(f));
        Raise(nameof(CountText));
        Message = remaining.Count == 0
            ? "Cleared. Drive the car and read again to see which faults come back."
            : "Cleared, but these faults returned immediately: they are still present.";
    }
}

public sealed class FaultsViewModel : PageViewModel
{
    private readonly SessionState _session;
    private string? _error;

    public FaultsViewModel(SessionState session)
    {
        _session = session;
        RefreshCommand = new AsyncCommand(RefreshAsync, () => _session.IsConnected && _session.LastScan is not null, e => Error = e.Message);
        _session.ScanUpdated += Rebuild;
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionState.IsConnected) or nameof(SessionState.LastScan)) RefreshCommand.RaiseCanExecuteChanged();
        };
    }

    public override string Title => "Faults";
    public override Geometry Icon => Icons.Faults;

    public ObservableCollection<FaultGroupViewModel> Groups { get; } = [];
    public bool IsEmpty => Groups.Count == 0;

    public string EmptyText => _session.LastScan is null
        ? "Run a quick test first. Faults from every module will appear here."
        : "No faults stored in any module that answered.";

    public string? Error
    {
        get => _error;
        set { if (Set(ref _error, value)) Raise(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);
    public AsyncCommand RefreshCommand { get; }

    private void Rebuild()
    {
        OnUi(() =>
        {
            Groups.Clear();
            if (_session.LastScan is { } scan)
                foreach (var e in scan.Ecus.Where(e => e.Faults.Count > 0))
                    Groups.Add(new FaultGroupViewModel(_session, e.Ecu, e.Faults));
            Raise(nameof(IsEmpty));
            Raise(nameof(EmptyText));
        });
    }

    /// <summary>Re-read fault memory of every module that answered in the last scan.</summary>
    private async Task RefreshAsync()
    {
        Error = null;
        if (_session.Backend is not { } backend || _session.LastScan is not { } scan) return;
        var scanner = new VehicleScanner(backend, _session.Database);
        var updated = new List<EcuScanResult>();
        foreach (var e in scan.Ecus)
        {
            if (e.Status is EcuScanStatus.Ok or EcuScanStatus.Faults && e.Note is null)
            {
                var faults = scanner.Describe(await backend.ReadFaultsAsync(e.Ecu));
                updated.Add(e with { Faults = faults, Status = faults.Count > 0 ? EcuScanStatus.Faults : EcuScanStatus.Ok });
            }
            else updated.Add(e);
        }
        _session.PublishScan(scan with { Ecus = updated });
    }
}
