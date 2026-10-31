using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Klemme15.App.Services;
using Klemme15.App.ViewModels;
using Klemme15.App.Views;
using Klemme15.Data;

namespace Klemme15.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = AppSettings.Load();
            var splash = TryShowSplash();
            if (splash is null)
            {
                OpenMainWindow(desktop, settings, database: null);
            }
            else
            {
                // Closing the splash must not end the app; the main window takes over once it is up.
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Dispatcher.UIThread.Post(async () => await StartWithSplashAsync(desktop, splash, settings));
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    // The splash is decoration: if it cannot be shown, the app starts exactly as it always did.
    private static SplashWindow? TryShowSplash()
    {
        try
        {
            var splash = new SplashWindow();
            splash.Show();
            return splash;
        }
        catch (Exception e)
        {
            Trace.TraceError($"Splash screen unavailable, starting without it: {e}");
            return null;
        }
    }

    private static async Task StartWithSplashAsync(IClassicDesktopStyleApplicationLifetime desktop, SplashWindow splash, AppSettings settings)
    {
        VehicleDatabase? database = null;
        var opened = false;
        try
        {
            await splash.RunAsync(async progress =>
            {
                // Each step is real work the app would otherwise do after opening; doing it here means the
                // Garage opens ready. A step that fails is left for the Garage to retry, as before.
                progress.Report(("Loading vehicle database", 0.05));
                database = await Task.Run(() => VehicleDatabase.Load());

                SgbdFolder? found = null;
                var searched = false;
                var folder = settings.EcuFolder ?? settings.EdiabasPath;
                if (folder is null || EdiabasLocator.CountPrg(folder) == 0)
                {
                    progress.Report(("Looking for EDIABAS, INPA or ISTA files", 0.30));
                    try { found = await Task.Run(EdiabasLocator.Find); searched = true; }
                    catch (Exception e) { Trace.TraceError($"BMW file search failed: {e}"); }
                }

                progress.Report(("Loading official fault texts", 0.55));
                OfficialStatus? official = null;
                try { official = await Task.Run(() => OfficialSetup.Apply(database, folder ?? found?.Path, autoDetect: !searched)); }
                catch (Exception e) { Trace.TraceError($"Fault text setup failed: {e}"); }

                progress.Report(("Checking cable ports", 0.75));
                IReadOnlyList<Klemme15.Serial.PortInfo>? ports = null;
                try { ports = await Task.Run(Klemme15.Serial.PortDiscovery.List); }
                catch (Exception e) { Trace.TraceError($"Port listing failed: {e}"); }

                progress.Report(("Building the interface", 0.90));
                await Task.Delay(40); // let the status line paint before the UI thread gets busy
                OpenMainWindow(desktop, settings, database, show: false,
                    new StartupInfo { Official = official, EdiabasSearched = searched, Ediabas = found, Ports = ports });
                opened = true;
            });
            desktop.MainWindow!.Show();
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            splash.Close();
        }
        catch (Exception e)
        {
            // Never leave the user stuck on a splash: fall back to the plain start.
            Trace.TraceError($"Splash start failed, opening the main window directly: {e}");
            try { splash.Close(); } catch (Exception) { /* already closed */ }
            if (!opened) OpenMainWindow(desktop, settings, database);
            else desktop.MainWindow?.Show();
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
    }

    private static void OpenMainWindow(IClassicDesktopStyleApplicationLifetime desktop, AppSettings settings, VehicleDatabase? database, bool show = true, StartupInfo? startup = null)
    {
        var window = new MainWindow();
        var main = new MainViewModel(new DialogService(window), settings, database, startup);
        main.ApplyTheme();
        window.DataContext = main;
        window.Closing += (_, _) => main.Shutdown();
        desktop.MainWindow = window;
        if (show) window.Show();
    }
}
