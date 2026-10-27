using System.Collections.ObjectModel;
using Avalonia.Media;
using OpenHood.App.Services;
using OpenHood.Core;

namespace OpenHood.App.ViewModels;

public sealed class EcuRowViewModel(EcuScanResult result) : ViewModelBase
{
    public EcuScanResult Result { get; } = result;
    public string Name => Result.Ecu.Name;
    public string Address => Result.Ecu.AddressHex;
    public bool Unverified => !Result.Ecu.Verified;

    public string StatusText => Result.Status switch
    {
        EcuScanStatus.Ok => "OK",
        EcuScanStatus.Faults => Result.Faults.Count == 1 ? "1 fault" : $"{Result.Faults.Count} faults",
        EcuScanStatus.NoResponse => "Not fitted / no answer",
        EcuScanStatus.Error => "Error",
        _ => Result.Status.ToString(),
    };

    public IBrush StatusBrush => Result.Status switch
    {
        EcuScanStatus.Ok => Brush.Parse("#2E9E5B"),
        EcuScanStatus.Faults => Brush.Parse("#D98A10"),
        EcuScanStatus.Error => Brush.Parse("#D6463B"),
        _ => Brush.Parse("#8A8F98"),
    };

    public string Detail => string.Join("  ·  ", new[] { Result.Identity?.Summary, Result.Error, Result.Note }
        .Where(s => !string.IsNullOrWhiteSpace(s)));

    public bool Answered => Result.Status is EcuScanStatus.Ok or EcuScanStatus.Faults;
}

public sealed class ScanViewModel : PageViewModel
{
    private readonly SessionState _session;
    private readonly IDialogService _dialogs;
    private readonly Action _showFaults;
    private CancellationTokenSource? _cts;
    private double _progress;
    private string _progressText = "";
    private bool _isScanning;
    private bool _showAbsent;
    private string? _error;

    public ScanViewModel(SessionState session, IDialogService dialogs, Action showFaults)
    {
        _session = session;
        _dialogs = dialogs;
        _showFaults = showFaults;
        RunCommand = new AsyncCommand(RunAsync, () => _session.IsConnected && !_isScanning, e => Error = e.Message);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => _isScanning);
        ExportHtmlCommand = new AsyncCommand(() => ExportAsync("html"), () => _session.LastScan is not null);
        ExportMarkdownCommand = new AsyncCommand(() => ExportAsync("md"), () => _session.LastScan is not null);
        ExportJsonCommand = new AsyncCommand(() => ExportAsync("json"), () => _session.LastScan is not null);
        ShowFaultsCommand = new RelayCommand(_showFaults);
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionState.IsConnected)) RunCommand.RaiseCanExecuteChanged();
            if (e.PropertyName == nameof(SessionState.LastScan)) RefreshSummary();
        };
    }

    public override string Title => "Quick test";
    public override Geometry Icon => Icons.Scan;

    public ObservableCollection<EcuRowViewModel> Rows { get; } = [];
    public IEnumerable<EcuRowViewModel> VisibleRows => _showAbsent ? Rows : Rows.Where(r => r.Answered || r.Result.Status == EcuScanStatus.Error);

    public bool ShowAbsent
    {
        get => _showAbsent;
        set { if (Set(ref _showAbsent, value)) Raise(nameof(VisibleRows)); }
    }

    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (!Set(ref _isScanning, value)) return;
            RunCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
        }
    }

    public string? Error
    {
        get => _error;
        set { if (Set(ref _error, value)) Raise(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);
    public bool HasResult => _session.LastScan is not null;
    public bool NotConnected => !_session.IsConnected;

    public string Vin => _session.LastScan?.Vin ?? "not read";
    public string ModulesSummary => _session.LastScan is { } s ? $"{s.RespondingEcus} / {s.Ecus.Count}" : "-";
    public string FaultSummary => _session.LastScan?.TotalFaults.ToString() ?? "-";
    public string TimeSummary => _session.LastScan is { } s ? $"{s.Elapsed.TotalSeconds:0.0} s" : "-";

    public AsyncCommand RunCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncCommand ExportHtmlCommand { get; }
    public AsyncCommand ExportMarkdownCommand { get; }
    public AsyncCommand ExportJsonCommand { get; }
    public RelayCommand ShowFaultsCommand { get; }

    public override void OnShown()
    {
        Raise(nameof(NotConnected));
        RunCommand.RaiseCanExecuteChanged();
    }

    private async Task RunAsync()
    {
        var backend = _session.Backend;
        var vehicle = _session.Vehicle;
        if (backend is null || vehicle is null) return;

        Error = null;
        Rows.Clear();
        Raise(nameof(VisibleRows));
        IsScanning = true;
        _cts = new CancellationTokenSource();
        var progress = new Progress<ScanProgress>(p =>
        {
            Progress = p.Total == 0 ? 0 : 100.0 * p.Done / p.Total;
            ProgressText = p.Message;
            if (p.Latest is { } r)
            {
                Rows.Add(new EcuRowViewModel(r));
                Raise(nameof(VisibleRows));
            }
        });

        try
        {
            var scan = await new VehicleScanner(backend, _session.Database).ScanAsync(vehicle.Modules, vehicle.Id, progress, _cts.Token);
            _session.PublishScan(scan);
            ProgressText = scan.Cancelled ? "Scan cancelled" : $"Done: {scan.RespondingEcus} modules answered, {scan.TotalFaults} fault(s)";
            Progress = 100;
        }
        finally
        {
            IsScanning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void RefreshSummary()
    {
        foreach (var p in new[] { nameof(HasResult), nameof(Vin), nameof(ModulesSummary), nameof(FaultSummary), nameof(TimeSummary), nameof(NotConnected) })
            Raise(p);
        ExportHtmlCommand.RaiseCanExecuteChanged();
        ExportMarkdownCommand.RaiseCanExecuteChanged();
        ExportJsonCommand.RaiseCanExecuteChanged();
        if (_session.LastScan is null)
        {
            Rows.Clear();
            Raise(nameof(VisibleRows));
        }
    }

    private async Task ExportAsync(string ext)
    {
        if (_session.LastScan is not { } scan) return;
        var content = ext switch
        {
            "json" => ReportWriter.ToJson(scan),
            "md" => ReportWriter.ToMarkdown(scan),
            _ => ReportWriter.ToHtml(scan),
        };
        await _dialogs.SaveTextAsync("Save scan report", $"openhood-{scan.Chassis}-{scan.Started:yyyyMMdd-HHmm}.{ext}", ext, content);
    }
}
