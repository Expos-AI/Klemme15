using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Threading;
using Klemme15.App;
using Klemme15.App.Services;
using Klemme15.App.ViewModels;
using Klemme15.App.Views;

// Drives the real app against demo cars and saves what it looks like. No display needed.
var outDir = Path.GetFullPath(args.Length > 0 ? args[0] : "screenshots");
Directory.CreateDirectory(outDir);
var problems = new List<string>();
Logger.Sink = new CollectingSink(problems);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .With(new FontManagerOptions { DefaultFamilyName = "avares://Klemme15/Assets/Fonts#Barlow" })
    .SetupWithoutStarting();
AvaloniaSynchronizationContext.InstallIfNeeded();

var settings = new AppSettings { Theme = "Dark" };
var vm = new MainViewModel(new NoDialogs(), settings);
vm.ApplyTheme();
var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
window.Show();

Shot("01-garage");
vm.Garage.SelectModel("E9x-335i");
Shot("02-garage-335i");
Connect();
Shot("03-vehicle-before-scan");
vm.Vehicle.RunCommand.Execute(null);
WaitUntil(() => vm.Vehicle.IsScanning);
Pump(120);
Shot("04-vehicle-scanning");
WaitUntil(() => !vm.Vehicle.IsScanning && vm.Vehicle.HasScan);
Pump(300);
Shot("05-vehicle-scanned");
vm.Vehicle.Select(vm.Vehicle.Markers.First(m => m.IsFaultSource));
Shot("06-vehicle-fault-selected");
vm.CurrentPage = vm.Faults;
Shot("07-faults");
if (vm.Faults.Groups.SelectMany(g => g.Faults).FirstOrDefault(f => f.HasGuide) is { } guided)
{
    guided.GuideOpen = true;
    Shot("17-faults-guide");
    guided.GuideOpen = false;
}
else problems.Add("17-faults-guide: no fault on the demo car has a guide");
vm.CurrentPage = vm.Readings;
WaitUntil(() => vm.Readings.Groups.Count > 0);
vm.Readings.ReadAllCommand.Execute(null);
WaitUntil(() => vm.Readings.Groups.SelectMany(g => g.Items).All(i => i.HasValues || i.HasError));
Shot("16-readings");
vm.CurrentPage = vm.Live;
vm.Live.StartCommand.Execute(null);
Pump(3000);
Shot("08-live");
vm.Live.Stop();
Pump(200);
vm.CurrentPage = vm.Tests;
vm.Tests.Speed = 10;
vm.Tests.StartCommand.Execute(null);
WaitUntil(() => vm.Tests.HasResult && !vm.Tests.IsRunning, 60000);
Shot("19-tests");
vm.CurrentPage = vm.Service;
vm.Service.Experimental = true;
vm.Service.CheckCommand.Execute(null);
WaitUntil(() => vm.Service.HasChecks);
foreach (var c in vm.Service.Confirms) c.Ticked = true;
vm.Service.RunCommand.Execute(null);
WaitUntil(() => vm.Service.HasResult);
Shot("18-service");
vm.Service.Experimental = false;
vm.CurrentPage = vm.Expert;
Shot("09-expert");
vm.CurrentPage = vm.Log;
Shot("10-log");

// Another model with an active fault, and the light theme.
vm.CurrentPage = vm.Garage;
vm.Garage.SelectModel("E9x-325i");
Connect();
ScanAndWait();
Shot("11-e90-325i-vehicle");
vm.IsDark = false;
Shot("12-e90-325i-vehicle-light");
vm.CurrentPage = vm.Faults;
Shot("13-faults-light");
vm.CurrentPage = vm.Garage;
Shot("14-garage-light");
vm.IsDark = true;
vm.Garage.SelectModel("E46-330i");
Connect();
ScanAndWait();
Shot("15-e46-330i-vehicle");

File.WriteAllLines(Path.Combine(outDir, "ui-warnings.txt"), problems.Distinct());
Console.WriteLine($"Saved screenshots to {outDir}; {problems.Distinct().Count()} distinct UI warnings.");
return 0;

void Connect()
{
    vm.Garage.OpenDemoCommand.Execute(null);
    WaitUntil(() => vm.Session.IsConnected && vm.CurrentPage == vm.Vehicle);
    Pump(200);
}

void ScanAndWait()
{
    vm.Vehicle.RunCommand.Execute(null);
    WaitUntil(() => vm.Vehicle.IsScanning);
    WaitUntil(() => !vm.Vehicle.IsScanning && vm.Vehicle.HasScan);
    Pump(300);
}

void Shot(string name)
{
    Pump(250);
    var frame = window.CaptureRenderedFrame();
    if (frame is null) { problems.Add($"{name}: no frame rendered"); return; }
    frame.Save(Path.Combine(outDir, name + ".png"));
    Console.WriteLine($"  {name}.png");
}

static void Pump(int ms)
{
    var sw = Stopwatch.StartNew();
    do
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(15);
    } while (sw.ElapsedMilliseconds < ms);
}

static void WaitUntil(Func<bool> condition, int timeoutMs = 20000)
{
    var sw = Stopwatch.StartNew();
    while (!condition())
    {
        if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("UI did not reach the expected state");
        Pump(20);
    }
}

sealed class NoDialogs : IDialogService
{
    public Task<bool> SaveTextAsync(string title, string suggestedName, string extension, string content) => Task.FromResult(false);
    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    public Task OpenUrlAsync(string url) => Task.CompletedTask;
}

/// <summary>Collects binding errors and other warnings so CI can report them.</summary>
sealed class CollectingSink(List<string> into) : ILogSink
{
    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Add(level, area, source, messageTemplate, []);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
        Add(level, area, source, messageTemplate, propertyValues);

    private void Add(LogEventLevel level, string area, object? source, string template, object?[] values)
    {
        var text = template;
        foreach (var v in values)
        {
            var start = text.IndexOf('{');
            var end = start < 0 ? -1 : text.IndexOf('}', start);
            if (end < 0) break;
            text = text[..start] + (v?.ToString() ?? "null") + text[(end + 1)..];
        }
        lock (into) into.Add($"[{level}] {area}: {text} ({source?.GetType().Name})");
    }
}
