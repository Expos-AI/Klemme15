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
        Faults.OpenReadings = ecu =>
        {
            CurrentPage = Readings;
            Readings.ShowModule(ecu.Id, "");
        };
        Faults.Purchase = new PurchaseCheckViewModel(Session, dialogs);
        Faults.Drain = new DrainFinderViewModel(Session);
        Faults.Share = new SnapshotShareViewModel(Session, dialogs, typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "");
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
        Tests = new TestsViewModel(Session, url => _ = dialogs.OpenUrlAsync(url));
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
        PrivacyCommand = new RelayCommand(() => _ = dialogs.OpenUrlAsync(Services.Links.Privacy));
        CoffeeCommand = new RelayCommand(() => _ = dialogs.OpenUrlAsync(Services.Links.Coffee));

        DismissInterruptedCommand = new RelayCommand(Session.DismissInterrupted);
        Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionState.Interrupted))
                OnUi(() => { Raise(nameof(HasInterrupted)); Raise(nameof(InterruptedAdvice)); });
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
    public RelayCommand DismissInterruptedCommand { get; }
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
        Live.Stop();
        Readings.OnHidden();
        Tests.OnHidden();
        Settings.Save();
        _ = Session.DisconnectAsync();
    }
}
