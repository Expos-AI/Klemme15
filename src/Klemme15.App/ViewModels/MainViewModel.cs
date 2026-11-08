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

using Avalonia;
using Avalonia.Styling;
using Klemme15.App.Services;
using Klemme15.Data;

using Klemme15.Core;

namespace Klemme15.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private PageViewModel _currentPage;
    private bool _isDark;

    /// <param name="database">An already-loaded database (the splash loads it in the background); loaded here if omitted.</param>
    /// <param name="startup">What the splash already found (fault texts, BMW files, ports); null to look on the Garage page.</param>
    public MainViewModel(IDialogService dialogs, AppSettings settings, VehicleDatabase? database = null, StartupInfo? startup = null)
    {
        Settings = settings;
        Session = new SessionState(database ?? VehicleDatabase.Load());
        Garage = new GarageViewModel(Session, dialogs, settings, () => CurrentPage = Vehicle!, startup);
        Vehicle = new VehicleViewModel(Session, dialogs);
        Faults = new FaultsViewModel(Session, (fault, ecu) =>
        {
            CurrentPage = Vehicle;
            Vehicle.ShowFault(fault, ecu);
        }, url => _ = dialogs.OpenUrlAsync(url));
        Readings = new ReadingsViewModel(Session, settings);
        Faults.FaultData = Garage;
        Faults.OpenReadings = ecu =>
        {
            CurrentPage = Readings;
            Readings.ShowModule(ecu.Id, "");
        };
        Faults.Purchase = new PurchaseCheckViewModel(Session, dialogs);
        Faults.Drain = new DrainFinderViewModel(Session);
        Faults.Share = new SnapshotShareViewModel(Session, dialogs, typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "");
        // One panel open at a time above the faults list.
        Faults.Share.PropertyChanged += (_, e) => { if (e.PropertyName == "IsOpen" && Faults.Share.IsOpen) { Faults.Purchase!.IsOpen = false; Faults.Drain!.IsOpen = false; } };
        Faults.Purchase!.PropertyChanged += (_, e) => { if (e.PropertyName == "IsOpen" && Faults.Purchase.IsOpen) { Faults.Share.IsOpen = false; Faults.Drain!.IsOpen = false; } };
        Faults.Drain!.PropertyChanged += (_, e) => { if (e.PropertyName == "IsOpen" && Faults.Drain.IsOpen) { Faults.Share.IsOpen = false; Faults.Purchase.IsOpen = false; } };
        Faults.GuideActions = new GuideActions(
            (ecuId, filter) => { CurrentPage = Readings; Readings.ShowModule(ecuId, filter); },
            () => CurrentPage = Live!,
            id => { CurrentPage = Tests!; Tests!.SelectById(id); });
        Live = new LiveViewModel(Session, dialogs);
        Service = new ServiceViewModel(Session, settings);
        Coding = new CodingViewModel(Session, settings, dialogs);
        Analyse = new AnalyseViewModel(dialogs)
        {
            ShippedBaselines = () => Session.Database.Baselines,
            EngineCode = () => Session.Model?.FullEngineCode,
            ChassisName = () => Session.Vehicle?.Id,
            ModelName = () => Session.Model?.Name,
            Store = new BaselineStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.Create), "Klemme15", "baselines")),
        };
        Live.AnalyseRecording = csv =>
        {
            Analyse.Load(Klemme15.Core.DataLog.Parse(csv), "this recording");
            CurrentPage = Analyse;
        };
        Tests = new TestsViewModel(Session, url => _ = dialogs.OpenUrlAsync(url)) { ExperimentalOn = () => settings.ExperimentalService };
        Expert = new ExpertViewModel(Session, settings);
        Ghost = new TriggerViewModel(Session, typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "")
        {
            OpenSnapshot = data => Garage.OpenSnapshotAsync(data),
        };
        Log = new LogViewModel(Session, dialogs);
        Session.SnapshotOpened += snapshot =>
        {
            if (snapshot.HasLive) Analyse.Load(Klemme15.Core.DataLog.Parse(snapshot.LiveCsv), "the snapshot's live data");
            CurrentPage = Faults;
        };
        Pages = [Garage, Vehicle, Faults, Readings, Tests, Live, Ghost, Service, Coding, Analyse, Expert, Log];
        _currentPage = Garage;
        _isDark = settings.Theme != "Light";
        ToggleThemeCommand = new RelayCommand(() => IsDark = !IsDark);
        AboutCommand = new RelayCommand(() => _ = dialogs.ShowAboutAsync(Version));
        SupportCommand = new RelayCommand(() => _ = dialogs.OpenUrlAsync(Services.Links.Support));
        PrivacyCommand = new RelayCommand(() => _ = ShowNotices is { } show ? show() : dialogs.OpenUrlAsync(Services.Links.Legal));
        CoffeeCommand = new RelayCommand(() => _ = dialogs.OpenUrlAsync(Services.Links.Coffee));

        DismissInterruptedCommand = new RelayCommand(Session.DismissInterrupted);
        CloseSnapshotCommand = new AsyncCommand(async () => { await Session.DisconnectAsync(); CurrentPage = Garage; }, () => !Session.JobRunning);
        Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionState.BusyWith) or nameof(SessionState.IsWatching))
                OnUi(RaiseCableBusy);
            if (e.PropertyName is nameof(SessionState.Interrupted))
                OnUi(() => { Raise(nameof(HasInterrupted)); Raise(nameof(InterruptedAdvice)); Raise(nameof(InterruptedHeadline)); });
            if (e.PropertyName is nameof(SessionState.Backend) or nameof(SessionState.Model) or nameof(SessionState.Connection) or nameof(SessionState.Voltage) or nameof(SessionState.Snapshot))
                foreach (var p in new[] { nameof(CarTitle), nameof(CarSubtitle), nameof(Connection), nameof(IsConnected), nameof(HasCar), nameof(IsSnapshot), nameof(SnapshotBanner), nameof(SnapshotNote), nameof(HasSnapshotNote) })
                    Raise(p);
        };
    }

    public AppSettings Settings { get; }
    public SessionState Session { get; }
    public GarageViewModel Garage { get; }
    public VehicleViewModel Vehicle { get; }
    public FaultsViewModel Faults { get; }
    public ReadingsViewModel Readings { get; }
    public LiveViewModel Live { get; }
    public ServiceViewModel Service { get; }
    public CodingViewModel Coding { get; }
    public AnalyseViewModel Analyse { get; }
    public TestsViewModel Tests { get; }
    public ExpertViewModel Expert { get; }
    public TriggerViewModel Ghost { get; }
    public LogViewModel Log { get; }
    public IReadOnlyList<PageViewModel> Pages { get; }

    public PageViewModel CurrentPage
    {
        get => _currentPage;
        set
        {
            var previous = _currentPage;
            if (value is not null && Set(ref _currentPage, value))
            {
                previous?.OnHidden();
                value.OnShown();
                RaiseCableBusy();
            }
        }
    }

    public bool HasCar => Session.Model is not null && Session.IsConnected;
    public string CarTitle => Session.Model is { } m && Session.IsConnected ? $"{Session.Vehicle?.Id} {m.Name}" : "No car connected";
    public string CarSubtitle => Session.Model is { } m && Session.IsConnected ? m.Engine : "Pick your car in the garage";
    public string Connection => Session.VoltageText.Length > 0 ? $"{Session.Connection} · {Session.VoltageText}" : Session.Connection;
    public bool IsConnected => Session.IsConnected;
    public bool IsSnapshot => Session.IsSnapshot;
    public bool HasInterrupted => Session.HasInterrupted;
    public string InterruptedAdvice => Session.InterruptedAdvice;
    public string InterruptedHeadline => Session.InterruptedHeadline;
    public RelayCommand DismissInterruptedCommand { get; }

    // ---------------------------------------------------------------- who is using the cable

    /// <summary>The page running what holds the cable, when it isn't the page on screen.</summary>
    private PageViewModel? BusyPage => Session.IsWatching ? Ghost : Session.BusyWith switch
    {
        "Live data" => Live, "coding" => Coding, "The quick test" or "Clearing faults" => Vehicle, _ => null,
    };

    /// <summary>
    /// Why buttons that talk to the car are greyed out: something else holds the cable (the Ghost Hunter watching, a live
    /// recording, a test or service function still running). Hidden on the page that's running it.
    /// </summary>
    public string? CableBusy
    {
        get
        {
            var what = Session.IsWatching ? "The Ghost Hunter is watching the car"
                : Session.BusyWith == "Live data" ? "Live data is reading the car"
                : Session.BusyWith == "coding" ? "Coding is talking to the car"
                : Session.BusyWith is { } b ? $"\"{b}\" is running" : null;
            if (what is null || BusyPage is { } page && page == _currentPage) return null;
            return $"{what}. Buttons that talk to the car wait until it stops: one cable carries one conversation at a time.";
        }
    }

    public bool HasCableBusy => CableBusy is not null;
    public bool CanGoToBusy => BusyPage is not null;
    private RelayCommand? _goToBusy;
    public RelayCommand GoToBusyCommand => _goToBusy ??= new(() => { if (BusyPage is { } p) CurrentPage = p; });

    private void RaiseCableBusy()
    {
        Raise(nameof(CableBusy)); Raise(nameof(HasCableBusy)); Raise(nameof(CanGoToBusy));
    }

    private string? _problem;

    /// <summary>An unexpected error, in plain words, shown in a banner until dismissed.</summary>
    public string? Problem { get => _problem; private set { if (Set(ref _problem, value)) Raise(nameof(HasProblem)); } }
    public bool HasProblem => !string.IsNullOrEmpty(_problem);
    private RelayCommand? _dismissProblem;
    public RelayCommand DismissProblemCommand => _dismissProblem ??= new(() => Problem = null);

    public void ShowProblem(Exception e)
    {
        try { Session.Log.Note($"Error: {e}"); } catch (Exception) { }
        OnUi(() => Problem = Problems.Describe(e));
    }
    public AsyncCommand CloseSnapshotCommand { get; }
    public string SnapshotBanner => Session.SnapshotBanner;
    public string SnapshotNote => Session.Snapshot?.Manifest.Note is { Length: > 0 } n ? $"The owner says: \u201c{n}\u201d" : "";
    public bool HasSnapshotNote => SnapshotNote.Length > 0;

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
    public RelayCommand AboutCommand { get; }
    public RelayCommand SupportCommand { get; }
    public RelayCommand PrivacyCommand { get; }

    /// <summary>Opens "Safety, legal and transparency" in the app (set by the desktop app; elsewhere the web page opens).</summary>
    public Func<Task>? ShowNotices { get; set; }
    public RelayCommand CoffeeCommand { get; }
    public bool HasCoffee => !string.IsNullOrWhiteSpace(Services.Links.Coffee);

    public void ApplyTheme()
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = _isDark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    public string Version => $"Klemme15 {typeof(MainViewModel).Assembly.GetName().Version?.ToString(3)}";

    public void Shutdown()
    {
        Ghost.StopWatching();
        Live.Stop();
        Readings.OnHidden();
        Tests.OnHidden();
        Settings.Save();
        _ = Session.DisconnectAsync();
    }
}
