using OpenHood.App.Services;
using OpenHood.Data;

namespace OpenHood.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private PageViewModel _currentPage;

    public MainViewModel(IDialogService dialogs, AppSettings settings)
    {
        Settings = settings;
        Session = new SessionState(VehicleDatabase.Load());
        Connect = new ConnectViewModel(Session, dialogs, settings, () => CurrentPage = Scan!);
        Scan = new ScanViewModel(Session, dialogs, () => CurrentPage = Faults!);
        Faults = new FaultsViewModel(Session);
        Live = new LiveViewModel(Session, dialogs);
        Expert = new ExpertViewModel(Session, settings);
        Log = new LogViewModel(Session, dialogs);
        Pages = [Connect, Scan, Faults, Live, Expert, Log];
        _currentPage = Connect;
    }

    public AppSettings Settings { get; }
    public SessionState Session { get; }
    public ConnectViewModel Connect { get; }
    public ScanViewModel Scan { get; }
    public FaultsViewModel Faults { get; }
    public LiveViewModel Live { get; }
    public ExpertViewModel Expert { get; }
    public LogViewModel Log { get; }
    public IReadOnlyList<PageViewModel> Pages { get; }

    public PageViewModel CurrentPage
    {
        get => _currentPage;
        set
        {
            if (value is not null && Set(ref _currentPage, value)) value.OnShown();
        }
    }

    public string Version => $"OpenHood {typeof(MainViewModel).Assembly.GetName().Version?.ToString(3)}";

    public void Shutdown()
    {
        Live.Stop();
        Settings.Save();
        _ = Session.DisconnectAsync();
    }
}
