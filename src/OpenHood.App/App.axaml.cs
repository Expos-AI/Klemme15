using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OpenHood.App.Services;
using OpenHood.App.ViewModels;
using OpenHood.App.Views;

namespace OpenHood.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var main = new MainViewModel(new DialogService(window), AppSettings.Load());
            main.ApplyTheme();
            window.DataContext = main;
            window.Closing += (_, _) => main.Shutdown();
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
