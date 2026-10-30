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
                progress.Report(("loading vehicle database", 0.15));
                database = await Task.Run(() => VehicleDatabase.Load());
                progress.Report(("building the interface", 0.80));
                await Task.Delay(40); // let the status line paint before the UI thread gets busy
                OpenMainWindow(desktop, settings, database, show: false);
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

    private static void OpenMainWindow(IClassicDesktopStyleApplicationLifetime desktop, AppSettings settings, VehicleDatabase? database, bool show = true)
    {
        var window = new MainWindow();
        var main = new MainViewModel(new DialogService(window), settings, database);
        main.ApplyTheme();
        window.DataContext = main;
        window.Closing += (_, _) => main.Shutdown();
        desktop.MainWindow = window;
        if (show) window.Show();
    }
}
