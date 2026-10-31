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
var layout = new List<string>();   // clipped or overlapping text, found by walking the visual tree
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
vm.Live.RecordCommand.Execute(null);
Pump(4000);
Shot("08-live");
vm.Live.Stop();
Pump(200);
// Analyse the recording just made.
vm.Live.AnalyseCommand.Execute(null);
Pump(200);
vm.Analyse.Position = vm.Analyse.MaxPosition / 2;
Shot("22-analyse");
vm.CurrentPage = vm.Tests;
vm.Tests.Speed = 10;
vm.Tests.StartCommand.Execute(null);
WaitUntil(() => vm.Tests.HasResult && !vm.Tests.IsRunning, 60000);
Shot("19-tests");
vm.Tests.Guides.First(g => g.Guide.Id == "where-is-it").SelectCommand!.Execute(null);
Shot("21-guide");
vm.CurrentPage = vm.Service;
vm.Service.Experimental = true;
vm.Service.CheckCommand.Execute(null);
WaitUntil(() => vm.Service.HasChecks);
foreach (var c in vm.Service.Confirms) c.Ticked = true;
vm.Service.RunCommand.Execute(null);
WaitUntil(() => vm.Service.HasResult);
Shot("18-service");
vm.Service.Experimental = false;
vm.CurrentPage = vm.Coding;
Pump(100);
if (vm.Coding.Modules.FirstOrDefault(m => m.Ecu.Id.EndsWith(".FRM", StringComparison.Ordinal)) is { } footwell)
{
    footwell.SelectCommand.Execute(null);
    vm.Coding.BackupCommand.Execute(null);
    WaitUntil(() => vm.Coding.HasBackups);
    vm.Coding.Backups[0].CompareCommand.Execute(null);
    WaitUntil(() => vm.Coding.HasDiff);
    Shot("20-coding");
}
else problems.Add("20-coding: no codable module answered on the demo car");
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

// Every page again at a 1080p laptop with 150 % scaling (1280 x 720 logical pixels), dark theme.
window.Width = 1280; window.Height = 720;
vm.Garage.SelectModel("E9x-325i");
Connect();
ScanAndWait();
foreach (var page in vm.Pages)
{
    vm.CurrentPage = page;
    Pump(200);
    Shot($"s-{page.Title.ToLowerInvariant()}");
}
vm.CurrentPage = vm.Vehicle;
if (vm.Vehicle.Markers.FirstOrDefault(m => m.IsFaultSource) is { } src) { vm.Vehicle.Select(src); Shot("s-vehicle-selected"); vm.Vehicle.Select(null); }

File.WriteAllLines(Path.Combine(outDir, "ui-layout.txt"), layout.Distinct());
problems.AddRange(layout.Distinct().Select(l => "[Layout] " + l));
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
    foreach (var issue in LayoutCheck.Find(window)) layout.Add($"{name} ({window.Width:0}x{window.Height:0}): {issue}");
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
    public Task<bool> CopyTextAsync(string text) => Task.FromResult(false);
    public Task<bool> SaveTextAsync(string title, string suggestedName, string extension, string content) => Task.FromResult(false);
    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    public Task<string?> OpenTextAsync(string title, string description, IReadOnlyList<string> patterns) => Task.FromResult<string?>(null);
    public Task OpenUrlAsync(string url) => Task.CompletedTask;
    public Task ShowAboutAsync(string version) => Task.CompletedTask;
}

/// <summary>
/// Finds text that doesn't fit: a single-line TextBlock cut off by its parent, or two visible TextBlocks drawn on top
/// of each other. Uses the real layout of the real views, so it catches what a person would see as jumbled text.
/// </summary>
static class LayoutCheck
{
    public static IEnumerable<string> Find(Avalonia.Controls.Window window)
    {
        var texts = new List<(Avalonia.Controls.TextBlock Tb, Rect Box)>();
        foreach (var v in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window))
        {
            if (v is not Avalonia.Controls.TextBlock tb || !tb.IsEffectivelyVisible || string.IsNullOrWhiteSpace(tb.Text)) continue;
            if (Hidden(tb)) continue;
            if (tb.TransformToVisual(window) is not { } m) continue;
            var box = new Rect(tb.Bounds.Size).TransformToAABB(m);
            if (box.Width < 1 || box.Height < 1) continue;
            if (!new Rect(0, 0, window.Width, window.Height).Intersects(box)) continue;
            // Inside a scroller, only what's in its viewport is visible.
            if (Viewport(tb, window) is { } vp) { if (!vp.Intersects(box)) continue; box = box.Intersect(vp); }
            texts.Add((tb, box));
            if (tb.TextWrapping == Avalonia.Media.TextWrapping.NoWrap && tb.TextTrimming == Avalonia.Media.TextTrimming.None)
            {
                var needed = tb.TextLayout.WidthIncludingTrailingWhitespace + tb.Padding.Left + tb.Padding.Right;
                if (needed > tb.Bounds.Width + 2)
                    yield return $"cut off: \"{Short(tb.Text)}\" needs {needed:0}px, has {tb.Bounds.Width:0}px";
            }
        }
        for (var i = 0; i < texts.Count; i++)
            for (var j = i + 1; j < texts.Count; j++)
            {
                var o = texts[i].Box.Intersect(texts[j].Box);
                if (o.Width > 3 && o.Height > 3)
                    yield return $"overlap: \"{Short(texts[i].Tb.Text)}\" and \"{Short(texts[j].Tb.Text)}\"";
            }
    }

    private static bool Hidden(Avalonia.Visual v)
    {
        for (Avalonia.Visual? p = v; p is not null; p = Avalonia.VisualTree.VisualExtensions.GetVisualParent(p))
            if (p.Opacity <= 0.01) return true;
        return false;
    }

    private static Rect? Viewport(Avalonia.Visual v, Avalonia.Visual root)
    {
        Rect? clip = null;
        for (var p = Avalonia.VisualTree.VisualExtensions.GetVisualParent(v); p is not null && p != root; p = Avalonia.VisualTree.VisualExtensions.GetVisualParent(p))
        {
            if (p is not Avalonia.Controls.Presenters.ScrollContentPresenter && !p.ClipToBounds) continue;
            if (p.TransformToVisual(root) is not { } m) continue;
            var r = new Rect(p.Bounds.Size).TransformToAABB(m);
            clip = clip is { } c ? c.Intersect(r) : r;
        }
        return clip;
    }

    private static string Short(string? s) => s is null ? "" : s.Length > 40 ? s[..40] + "..." : s;
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
