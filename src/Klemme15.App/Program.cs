using Avalonia;
using Avalonia.Media;

namespace Klemme15.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new FontManagerOptions { DefaultFamilyName = "avares://Klemme15/Assets/Fonts#Barlow" })
            .LogToTrace();
}
