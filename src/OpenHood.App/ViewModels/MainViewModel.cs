using Avalonia;
using Avalonia.Styling;
using OpenHood.App.Services;
using OpenHood.Data;

namespace OpenHood.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private PageViewModel _currentPage;
    private bool _isDark;

    public MainViewModel(IDialogService dialogs, AppSettings settings)
    {
        Settings = settings;
        Session = new SessionState(VehicleDatabase.Load());
        Garage = new GarageViewModel(Session, dialogs, settings, () => CurrentPage = Vehicle!);
        Vehicle = new VehicleViewModel(Session, dialogs);
        Faults = new FaultsViewModel(Session, (fault, ecu) =>
        {
            CurrentPage = Vehicle;
            Vehicle.ShowFault(fault, ecu);
        });
        Live = new LiveViewModel(Session, dialogs);
        Expert = new ExpertViewModel(Session, settings);
        Log = new LogViewModel(Session, dialogs);
        Pages = [Garage, Vehicle, Faults, Live, Expert, Log];
        _currentPage = Garage;
        _isDark = settings.Theme != "Light";
        ToggleThemeCommand = new RelayCommand(() => IsDark = !IsDark);

        Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionState.Backend) or nameof(SessionState.Model) or nameof(SessionState.Connection))
                foreach (var p in new[] { nameof(CarTitle), nameof(CarSubtitle), nameof(Connection), nameof(IsConnected), nameof(HasCar) })
                    Raise(p);
        };
    }

    public AppSettings Settings { get; }
    public SessionState Session { get; }
    public GarageViewModel Garage { get; }
    public VehicleViewModel Vehicle { get; }
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

    public bool HasCar => Session.Model is not null && Session.IsConnected;
    public string CarTitle => Session.Model is { } m && Session.IsConnected ? $"{Session.Vehicle?.Id} {m.Name}" : "No car connected";
    public string CarSubtitle => Session.Model is { } m && Session.IsConnected ? m.Engine : "Pick your car in the garage";
    public string Connection => Session.Connection;
    public bool IsConnected => Session.IsConnected;

    public bool IsDark
    {
        get => _isDark;
        set
        {
            if (!Set(ref _isDark, value)) return;
            ApplyTheme();
            Settings.Theme = value ? "Dark" : "Light";
            Settings.Save();
        }
    }

    public RelayCommand ToggleThemeCommand { get; }

    public void ApplyTheme()
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = _isDark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    public string Version => $"OpenHood {typeof(MainViewModel).Assembly.GetName().Version?.ToString(3)}";

    public void Shutdown()
    {
        Live.Stop();
        Settings.Save();
        _ = Session.DisconnectAsync();
    }
}
