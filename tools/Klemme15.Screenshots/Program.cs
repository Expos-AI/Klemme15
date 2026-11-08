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
vm.Vehicle.Select(null);
vm.Vehicle.Topology.Start();
WaitUntil(() => vm.Vehicle.Topology.Tiles.Any(t => t.HasValue), 10000);
Pump(600);
Shot("31-vehicle-live-values");
if (!vm.Vehicle.Topology.Tiles.Any(t => t.HasValue)) problems.Add("31-vehicle-live-values: no live values on the drawing");
var stopping = vm.Vehicle.Topology.StopAndWaitAsync();
WaitUntil(() => stopping.IsCompleted);
vm.CurrentPage = vm.Faults;
Shot("07-faults");
// The battery drain finder on the demo car: its clues, a parked current and the hunt.
vm.Faults.Drain!.IsOpen = true;
vm.Faults.Drain.Milliamps = "85";
Pump(200);
Shot("33-drain-finder");
if (!vm.Faults.Drain.HasClues) problems.Add("33-drain-finder: no clues on the demo car");
vm.Faults.Drain.IsOpen = false;
// What a cut-off job looks like: the banner that says what happened and what to do.
vm.Session.Journal.Begin(new Klemme15.Core.RunningOperation
{
    Kind = Klemme15.Core.OperationKind.ServiceFunction, Title = "Learn the Valvetronic end stops", ModuleId = "E9x.DME",
    ModuleName = "engine computer (DME)", Started = DateTimeOffset.Now.AddMinutes(-3),
});
vm.Session.OperationCutOff();
Pump(200);
Shot("34-job-cut-off");
if (!vm.HasInterrupted) problems.Add("34-job-cut-off: no banner");
vm.Session.DismissInterrupted();
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
// The "known good" overlay: the example baseline behind a made-up warm drive with a small vacuum leak (lean at idle).
{
    var rng = new Random(15);
    var csv = new System.Text.StringBuilder("time,\"Engine speed (rpm)\",\"Coolant temperature (°C)\",\"Short-term fuel trim bank 1 (%)\",\"Long-term fuel trim bank 1 (%)\",\"Engine load (%)\"\n");
    for (var i = 0; i < 400; i++)
    {
        var rpm = i < 120 ? 760 + rng.Next(-20, 20) : 900 + (i - 120) * 14 + rng.Next(-60, 60);
        var lean = Math.Max(0, 13 - (rpm - 760) / 120.0);
        csv.AppendLine(System.FormattableString.Invariant($"{i * 0.25:0.00},{rpm},91,{lean * 0.3 + rng.NextDouble() * 3 - 1.5:0.0},{lean * 0.7 + 1:0.0},{18 + rpm / 220.0:0}"));
    }
    vm.Analyse.Load(Klemme15.Core.DataLog.Parse(csv.ToString()), "a made-up warm drive");
    Pump(200);
}
if ((vm.Analyse.Baselines.FirstOrDefault(b => b.Curve?.Y == "stft1+ltft1") ?? vm.Analyse.Baselines.Skip(1).FirstOrDefault()) is { } baseline)
{
    vm.Analyse.Baseline = baseline;
    Pump(200);
    Shot("30-analyse-baseline");
    Console.WriteLine("Baseline: " + vm.Analyse.BaselineSummary);
}
else Console.WriteLine("Baseline: no baseline fits the demo recording (channels: " + string.Join(", ", vm.Analyse.Channels.Select(c => c.Label)) + ")");
vm.CurrentPage = vm.Tests;
vm.Tests.Speed = 10;
vm.Tests.StartCommand.Execute(null);
WaitUntil(() => vm.Tests.HasResult && !vm.Tests.IsRunning, 60000);
Shot("19-tests");
vm.Tests.Tests.First(t => t.Info.Id == "water-pump-sweep").SelectCommand!.Execute(null);
vm.Tests.Speed = 20;
vm.Tests.StartCommand.Execute(null);
WaitUntil(() => vm.Tests.HasResult && !vm.Tests.IsRunning, 60000);
if (!vm.Tests.HasCharts) problems.Add("23-pump-sweep: no graph");
Shot("23-pump-sweep");
// A community chain, stopped at its question.
vm.Tests.SelectById("vacuum-leak-hunter");
vm.Tests.Speed = 20;
vm.Tests.StartCommand.Execute(null);
WaitUntil(() => vm.Tests.IsAsking, 60000);
Shot("25-chain-question");
vm.Tests.Choices[0].PickCommand.Execute(null);
WaitUntil(() => vm.Tests.HasResult && !vm.Tests.IsRunning, 60000);
Shot("26-chain-verdict");
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
// The Ghost Hunter on the demo car: a rule that fires when the demo "driver" revs, caught and saved by itself.
vm.CurrentPage = vm.Ghost;
WaitUntil(() => vm.Ghost.Signals.Count > 0, 30000);
vm.Ghost.NewTitle = "Revving above 2,000 rpm (demo)";
vm.Ghost.NewConditions[0].Signal = vm.Ghost.Signals.First(x => x.Id == "rpm");
vm.Ghost.NewConditions[0].Operator = TriggerViewModel.Operators[0];
vm.Ghost.NewConditions[0].Threshold = "2000";
vm.Ghost.NewConditions[0].Hold = "1";
vm.Ghost.Before = "5";
vm.Ghost.After = "3";
vm.Ghost.SaveRuleCommand.Execute(null);
if (vm.Ghost.HasBuilderError) problems.Add("29-ghost-hunter: " + vm.Ghost.BuilderError);
vm.Ghost.StartCommand.Execute(null);
WaitUntil(() => vm.Ghost.HasCaptures, 90000);
Shot("29-ghost-hunter");
vm.Ghost.StopCommand.Execute(null);
WaitUntil(() => !vm.Ghost.IsWatching);
// A template fills the rule builder: plain words, numbers ready to adjust.
vm.Ghost.SelectedTemplate = vm.Ghost.Templates.First(t => t.Rule.Id == "lean-idle");
Pump(200);
if (Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.ComboBox>()
        .FirstOrDefault(c => ReferenceEquals(c.ItemsSource, vm.Ghost.Templates)) is { } templates)
{
    // Scroll the page so the builder is at the top.
    var scroller = Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(templates).OfType<Avalonia.Controls.ScrollViewer>().FirstOrDefault();
    if (scroller?.Content is Avalonia.Visual content && templates.TranslatePoint(new Avalonia.Point(0, 0), content) is { } at)
        scroller.Offset = new Avalonia.Vector(0, Math.Max(0, at.Y - 120));
}
Pump(300);
Shot("32-ghost-template");
if (vm.Ghost.NewConditions.Count != 3) problems.Add($"32-ghost-template: expected 3 conditions, got {vm.Ghost.NewConditions.Count}");

vm.CurrentPage = vm.Expert;
Shot("09-expert");
vm.CurrentPage = vm.Log;
Shot("10-log");

// The Digital Twin: share the demo car as a snapshot, then open it read-only.
vm.CurrentPage = vm.Faults;
vm.Faults.Share!.Note = "Rough idle when cold, lean codes after a long drive. Already replaced the oil filler cap.";
vm.Faults.Share.IsOpen = true;
Shot("27-share-snapshot");
vm.Faults.Share.IsOpen = false;
var snapshotFile = Klemme15.Core.SnapshotFile.Write(vm.Session.BuildSnapshot(vm.Faults.Share.Note, true, true, true, false, "screenshots"));
vm.CurrentPage = vm.Garage;
var opening = vm.Garage.OpenSnapshotAsync(snapshotFile);
WaitUntil(() => opening.IsCompleted && vm.Session.IsSnapshot);
if (vm.Garage.HasError) problems.Add("24-snapshot: " + vm.Garage.Error);
Shot("24-snapshot-faults");
vm.CurrentPage = vm.Analyse;
Shot("28-snapshot-analyse");

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

// The "Before you start" notice shown on first launch, and the full safety and legal page.
foreach (var (name, notice) in new (string, Avalonia.Controls.Window)[] { ("35-before-you-start", new WelcomeNoticeWindow()), ("36-safety-legal", new NoticesWindow()) })
{
    notice.Show();
    Pump(300);
    if (notice.CaptureRenderedFrame() is { } shot) { shot.Save(Path.Combine(outDir, name + ".png")); Console.WriteLine($"  {name}.png"); }
    else problems.Add($"{name}: no frame rendered");
    foreach (var issue in LayoutCheck.Find(notice)) layout.Add($"{name}: {issue}");
    notice.Close();
}

// Every page again at a 1080p laptop with 150 % scaling (1280 x 720 logical pixels), dark theme.
window.Width = 1280; window.Height = 720;
vm.Garage.SelectModel("E9x-325i");
Connect();
ScanAndWait();
foreach (var page in vm.Pages)
{
    vm.CurrentPage = page;
    Pump(200);
    Shot($"s-{page.Title.ToLowerInvariant().Replace(' ', '-')}");
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
    public Task<string?> SaveBytesAsync(string title, string suggestedName, string extension, string description, byte[] content) => Task.FromResult<string?>(null);
    public Task<(byte[] Data, string Name)?> OpenBytesAsync(string title, string description, IReadOnlyList<string> patterns) => Task.FromResult<(byte[] Data, string Name)?>(null);
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
