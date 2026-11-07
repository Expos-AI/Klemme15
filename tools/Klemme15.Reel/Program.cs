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
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Klemme15.App;
using Klemme15.App.Services;
using Klemme15.App.ViewModels;
using Klemme15.App.Views;

// Records the real app, driven against the demo cars, one scene per feature. Each scene becomes a video
// (raw/<scene>.mp4, 30 fps, the window at 1.5x) and a list of where the action is and where the "clicks" land
// (raw/<scene>.json). compose.py turns those into vertical clips that follow the action. No display needed.
var outDir = Path.GetFullPath(args.Length > 0 ? args[0] : "reel-raw");
// An empty list (the workflow's default) means every scene.
var only = args.Length > 1 && args[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is { Length: > 0 } picked
    ? picked.ToHashSet() : null;
Directory.CreateDirectory(outDir);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .With(new FontManagerOptions { DefaultFamilyName = "avares://Klemme15/Assets/Fonts#Barlow" })
    .SetupWithoutStarting();
AvaloniaSynchronizationContext.InstallIfNeeded();

// The app is laid out in a tall 1120 x 1400 window (the 4:5 shape of the clip's picture) and drawn at 1.5x, so even
// the full window is close to phone proportions and a zoomed-in crop stays sharp on a 1080-wide video.
const int W = 1120, H = 1400;
const double Scale = 1.5;
var settings = new AppSettings { Theme = "Dark" };
var vm = new MainViewModel(new NoDialogs(), settings);
vm.ApplyTheme();
var window = new MainWindow { DataContext = vm, Width = W, Height = H };
var root = (Control)window.Content!;
window.Content = null;
window.Content = new LayoutTransformControl { LayoutTransform = new ScaleTransform(Scale, Scale), Child = root };
window.Width = W * Scale;
window.Height = H * Scale;
window.Show();
var reel = new Reel(window, outDir);
reel.Idle(300);

var scenes = new List<(string Id, string Title, string? Note, Action Play)>
{
    ("01-quick-test", "Every module, one button", "demo car shown", QuickTest),
    ("02-plain-english", "Codes in plain English", "demo car shown", PlainEnglish),
    ("03-car-story", "The car's own history", "demo car shown", CarStory),
    ("04-buying-check", "Check before you buy", "demo car shown", BuyingCheck),
    ("05-live-data", "Live data, right on the car", "demo car shown", LiveData),
    ("06-tests", "Tests that find the part", "demo car shown · sped up", Tests),
    ("07-ghost-hunter", "Catches problems that come and go", "demo car shown · sped up", GhostHunter),
    ("08-battery-drain", "Battery drain finder", "demo car shown", BatteryDrain),
    ("09-service-coding", "Service resets and comfort coding", "demo car shown · beta, experimental", ServiceCoding),
    ("10-share", "One file for the forum", "demo car shown", Share),
    ("11-older-cars", "Older E-series too", "demo car shown", OlderCars),
    ("12-step-by-step", "Diagnosis that asks you questions", "demo car shown · sped up", StepByStep),
    ("13-compare-healthy", "Compare with healthy engines", "demo car and demo drive shown", CompareHealthy),
};
var failed = new List<string>();
foreach (var (id, title, note, play) in scenes)
{
    if (only is not null && !only.Contains(id)) continue;
    Console.WriteLine($"Recording {id}...");
    reel.Begin(id, title, note);
    try { play(); }
    catch (Exception e) { failed.Add($"{id}: {e.Message}"); Console.WriteLine($"  {id} failed: {e}"); }
    finally { reel.End(); }
}
File.WriteAllLines(Path.Combine(outDir, "failed.txt"), failed);
Console.WriteLine(failed.Count == 0 ? "All scenes recorded." : $"{failed.Count} scene(s) failed:\n  " + string.Join("\n  ", failed));
return failed.Count == 0 ? 0 : 1;

// ---------------------------------------------------------------- scenes

void EnsureDemo(string model = "E9x-335i", bool scan = true)
{
    if (vm.Session.IsConnected && vm.Session.Model?.Id == model && (!scan || vm.Session.LastScan is not null)) return;
    vm.CurrentPage = vm.Garage;
    vm.Garage.SelectModel(model);
    vm.Garage.OpenDemoCommand.Execute(null);
    reel.WaitQuietly(() => vm.Session.IsConnected && vm.CurrentPage == vm.Vehicle);
    if (!scan) return;
    vm.Vehicle.RunCommand.Execute(null);
    reel.WaitQuietly(() => vm.Vehicle.IsScanning);
    reel.WaitQuietly(() => !vm.Vehicle.IsScanning && vm.Vehicle.HasScan, 30000);
    reel.Idle(300);
}

// Waits for a test, noting each instruction it shows (they say what the graph is doing: "AC on", and so on).
void UntilMarkingInstructions(Func<bool> done, int timeoutMs)
{
    var last = "";
    reel.Until(() =>
    {
        var now = vm.Tests.Instruction;
        if (now.Length > 0 && now != last) { last = now; reel.Mark($"The test says: \"{now}\""); }
        return done();
    }, timeoutMs);
}

void Go(PageViewModel page)
{
    var label = reel.Find<TextBlock>(t => t.Classes.Contains("raillabel") && t.Text == page.Title);
    if (label is null) { vm.CurrentPage = page; reel.Run(400); return; }
    reel.Click(label, () => vm.CurrentPage = page);
    reel.Run(500);
}

void QuickTest()
{
    vm.CurrentPage = vm.Garage;
    vm.Garage.SelectModel("E9x-335i");
    reel.Run(200);
    reel.Overview();
    reel.Mark("The Garage, E90 335i picked");
    reel.Run(700);
    reel.Click(reel.Button("Open demo car"), () => vm.Garage.OpenDemoCommand.Execute(null));
    reel.Until(() => vm.Session.IsConnected && vm.CurrentPage == vm.Vehicle);
    reel.Mark("The car's page, before the test");
    reel.Run(600);
    reel.Click(reel.Button("Run quick test"), () => vm.Vehicle.RunCommand.Execute(null));
    reel.Until(() => vm.Vehicle.IsScanning);
    reel.Mark("Quick test running: the modules light up one by one");
    // The car drawing (it sits in a Viewbox): watch the modules light up one by one.
    if (CarDrawing() is { } drawing) reel.Focus(drawing, pad: 20); else reel.Overview();
    reel.Speed = 1.5;
    reel.Until(() => !vm.Vehicle.IsScanning && vm.Vehicle.HasScan, 30000);
    reel.Speed = 1;
    reel.Mark("Done: every module's result");
    reel.Overview();
    reel.Run(1800);
    if (CarDrawing() is { } car) reel.Focus(car, pad: 20);
    reel.Run(600);
    var marker = vm.Vehicle.Markers.First(m => m.IsFaultSource);
    if (reel.Find<Button>(b => ReferenceEquals(b.DataContext, marker)) is { } dot)
        reel.Click(dot, () => vm.Vehicle.Select(marker));
    else vm.Vehicle.Select(marker);
    reel.Mark("A module with a fault clicked: its fault shows");
    reel.Run(600);
    reel.Overview();
    reel.Run(2200);
    vm.Vehicle.Select(null);
}

// The car drawing: the Viewbox holding the markers (the rail's icons are Viewboxes too).
Viewbox? CarDrawing() => reel.Find<Viewbox>(v => v.GetVisualDescendants().OfType<ItemsControl>().Any(i => ReferenceEquals(i.ItemsSource, vm.Vehicle.Markers)));

void PlainEnglish()
{
    EnsureDemo();
    Go(vm.Faults);
    reel.Mark("The Faults page");
    reel.Overview();
    reel.Run(1200);
    var item = vm.Faults.Groups.SelectMany(g => g.Faults).First(f => f.HasGuide);
    var card = reel.Card(reel.Find<Control>(c => ReferenceEquals(c.DataContext, item) && c is Border)!);
    reel.ScrollTo(card, 800, top: 160);
    reel.Focus(card, width: 0.72);
    reel.Mark("One fault card: what it means, how urgent");
    reel.Run(3000);
    var button = reel.Find<Button>(b => ReferenceEquals(b.DataContext, item) && (b.Content as string) == "Troubleshoot");
    if (button is not null) reel.Click(button, () => item.GuideOpen = true); else item.GuideOpen = true;
    reel.Mark("Troubleshoot: the checks, step by step");
    reel.Run(400);
    reel.Focus(card, width: 0.72);
    reel.Run(4000);
    item.GuideOpen = false;
}

void CarStory()
{
    EnsureDemo();
    Go(vm.Faults);
    var story = reel.Card(reel.Text("Every dash message your cluster has shown"));
    reel.ScrollTo(story, 1400, top: 120);
    reel.Focus(story, width: 0.75);
    reel.Mark("Every dash message the cluster has shown");
    reel.Run(4500);
}

void BuyingCheck()
{
    EnsureDemo();
    Go(vm.Faults);
    reel.ScrollTop(500);
    var purchase = vm.Faults.Purchase!;
    reel.Click(reel.Button("Buying this car?"), () => purchase.IsOpen = true);
    reel.Mark("Buying this car? opens");
    reel.Run(400);
    var panel = reel.Card(reel.Find<StackPanel>(s => s.DataContext is PurchaseCheckViewModel)!);
    reel.Focus(panel);
    if (reel.Find<TextBox>(t => t.DataContext is PurchaseCheckViewModel) is { } box)
        reel.Click(box, () => { });
    reel.Mark("The odometer reading is typed");
    reel.Type("152400", s => purchase.Odometer = s);
    reel.Mark("The verdict and what it checked");
    reel.Run(600);
    reel.Focus(reel.Card(reel.Find<StackPanel>(s => s.DataContext is PurchaseCheckViewModel)!), width: 0.75);
    reel.Run(4500);
    purchase.IsOpen = false;
}

void LiveData()
{
    EnsureDemo();
    Go(vm.Vehicle);
    reel.Overview();
    var toggle = reel.Find<Button>(b => ReferenceEquals(b.Command, vm.Vehicle.Topology.ToggleCommand));
    if (toggle is not null) reel.Click(toggle, () => vm.Vehicle.Topology.Start()); else vm.Vehicle.Topology.Start();
    reel.Until(() => vm.Vehicle.Topology.Tiles.Any(t => t.HasValue), 10000);
    reel.Mark("Live values on the car drawing");
    reel.Run(3500);
    var stopping = vm.Vehicle.Topology.StopAndWaitAsync();
    reel.WaitQuietly(() => stopping.IsCompleted);
    Go(vm.Live);
    reel.Click(reel.Button("Start"), () => vm.Live.StartCommand.Execute(null));
    reel.Mark("The Live data page: values moving");
    reel.Run(800);
    // The value cards: the ItemsControl around the first card.
    if (reel.Find<Border>(b => b.DataContext is LiveParameterViewModel && b.Classes.Contains("panel"))?.GetVisualAncestors().OfType<ItemsControl>().FirstOrDefault() is { } tiles)
        reel.Focus(tiles);
    reel.Run(5000);
    vm.Live.Stop();
    reel.Run(300);
}

void Tests()
{
    EnsureDemo();
    Go(vm.Tests);
    vm.Tests.SelectById("idle-ac");
    reel.Run(600);
    reel.Overview();
    reel.Mark("The idle and AC test picked");
    vm.Tests.Speed = 3;
    reel.Click(reel.Button("Start"), () => vm.Tests.StartCommand.Execute(null));
    reel.Mark("Test running: idle graph, AC off");
    reel.Run(800);
    if (reel.Find<ItemsControl>(i => ReferenceEquals(i.ItemsSource, vm.Tests.Charts)) is { } charts) reel.Focus(charts, pad: 60);
    reel.Speed = 2;
    UntilMarkingInstructions(() => vm.Tests.HasResult && !vm.Tests.IsRunning, 120000);
    reel.Speed = 1;
    reel.Mark("Test done");
    reel.Run(600);
    if (reel.Find<ItemsControl>(i => ReferenceEquals(i.ItemsSource, vm.Tests.Findings)) is { } findings)
    {
        reel.ScrollTo(findings, 900, top: 200);
        reel.Focus(findings, pad: 40);
    }
    reel.Mark("The verdict: what it found and where to look");
    reel.Run(4500);
}

void GhostHunter()
{
    EnsureDemo();
    Go(vm.Ghost);
    reel.WaitQuietly(() => vm.Ghost.Signals.Count > 0, 30000);
    reel.Overview();
    reel.Mark("The Ghost Hunter page: ready-made rules");
    reel.Run(1500);
    // The demo car's values are healthy, so a rule on the demo "driver" revving shows a real catch.
    var builder = reel.Card(reel.Text("Make your own rule"));
    reel.ScrollTo(builder, 900, top: 80);
    reel.Focus(builder);
    reel.Mark("Making a rule: engine speed above 2,000 rpm");
    reel.Type("Revving above 2,000 rpm", s => vm.Ghost.NewTitle = s);
    vm.Ghost.NewConditions[0].Signal = vm.Ghost.Signals.First(x => x.Id == "rpm");
    vm.Ghost.NewConditions[0].Operator = TriggerViewModel.Operators[0];
    reel.Run(300);
    reel.Type("2000", s => vm.Ghost.NewConditions[0].Threshold = s);
    vm.Ghost.NewConditions[0].Hold = "1";
    vm.Ghost.Before = "5";
    vm.Ghost.After = "3";
    reel.Run(500);
    reel.Click(reel.Button("Save rule"), () => vm.Ghost.SaveRuleCommand.Execute(null));
    reel.Run(600);
    reel.Overview();
    reel.Click(reel.Button("Start watching"), () => vm.Ghost.StartCommand.Execute(null));
    reel.Mark("Start watching clicked");
    reel.Run(1500);
    if (reel.Find<TextBlock>(t => t.Text == "Right now") is { } now) reel.Focus(reel.Card(now.GetVisualParent<Control>()!), pad: 30);
    reel.Mark("Right now: what each rule is waiting for");
    reel.Run(2500);
    reel.Speed = 4;
    reel.Until(() => vm.Ghost.HasCaptures, 120000);
    reel.Speed = 1;
    reel.Mark("Caught: the moment is saved");
    reel.Run(500);
    var right = reel.Find<TextBlock>(t => t.Text == "Captures")?.GetVisualParent<Control>();
    if (right is not null) reel.Focus(right, pad: 20);
    reel.Run(4000);
    vm.Ghost.StopCommand.Execute(null);
    reel.WaitQuietly(() => !vm.Ghost.IsWatching);
}

void BatteryDrain()
{
    EnsureDemo();
    Go(vm.Faults);
    reel.ScrollTop(500);
    var drain = vm.Faults.Drain!;
    reel.Click(reel.Button("Battery going flat?"), () => drain.IsOpen = true);
    reel.Run(500);
    var panel = reel.Card(reel.Find<StackPanel>(s => s.DataContext is DrainFinderViewModel)!);
    reel.Focus(panel, width: 0.75);
    reel.Mark("Battery drain finder: clues from this car");
    reel.Run(2500);
    if (reel.Find<TextBox>(t => t.DataContext is DrainFinderViewModel) is { } box) reel.Click(box, () => { });
    reel.Type("85", s => drain.Milliamps = s);
    reel.Mark("85 mA typed: too much, and how long until it won't start");
    reel.Run(2500);
    reel.Mark("The hunt, step by step");
    reel.ScrollBy(panel, 700, 2500);
    reel.Run(1500);
    drain.IsOpen = false;
}

void ServiceCoding()
{
    EnsureDemo();
    Go(vm.Service);
    reel.Overview();
    reel.Mark("Service functions: the list");
    reel.Run(1200);
    if (reel.Find<ItemsControl>(i => i.ItemsSource is not null && ReferenceEquals(i.ItemsSource, vm.Service.Groups)) is { } list)
    {
        reel.Focus(reel.Card(list), pad: 20);
        reel.ScrollBy(list, 900, 3500);
    }
    reel.Run(800);
    Go(vm.Coding);
    reel.Mark("The Coding page");
    reel.Run(300);
    if (vm.Coding.Modules.FirstOrDefault(m => m.Ecu.Id.EndsWith(".FRM", StringComparison.Ordinal)) is { } frm)
    {
        if (reel.Find<Control>(c => ReferenceEquals(c.DataContext, frm) && c is Button) is { } pick) reel.Click(pick, () => frm.SelectCommand.Execute(null));
        else frm.SelectCommand.Execute(null);
        reel.Run(600);
        if (reel.Find<Button>(b => (b.Content as string) == "Read coding") is { } read && read.IsEffectivelyEnabled)
            reel.Click(read, () => vm.Coding.ReadCommand.Execute(null));
        reel.Mark("Footwell module's coding read: the comfort options");
        reel.Run(3000);
        reel.Overview();
        reel.Run(3500);
    }
}

void Share()
{
    EnsureDemo();
    Go(vm.Faults);
    reel.ScrollTop(500);
    var share = vm.Faults.Share!;
    reel.Click(reel.Button("Share snapshot"), () => share.IsOpen = true);
    reel.Mark("Share snapshot opens");
    reel.Run(500);
    var panel = reel.Card(reel.Find<StackPanel>(s => s.DataContext is SnapshotShareViewModel)!);
    reel.Focus(panel);
    if (reel.Find<TextBox>(t => t.DataContext is SnapshotShareViewModel) is { } box) reel.Click(box, () => { });
    reel.Mark("A note for the forum is typed");
    reel.Type("Rough idle when warm, chugs with the AC on. Already replaced the oil filler cap.", s => share.Note = s, perChar: 35);
    reel.Run(3500);
    share.IsOpen = false;
}

void OlderCars()
{
    vm.CurrentPage = vm.Garage;
    vm.Garage.SelectModel("E9x-335i");
    reel.Run(200);
    reel.Overview();
    reel.Mark("The Garage");
    reel.Run(600);
    if (reel.Find<ItemsControl>(i => ReferenceEquals(i.ItemsSource, vm.Garage.Families)) is { } families) reel.Focus(families, pad: 30);
    reel.Mark("Every E-series family it knows");
    reel.Run(2200);
    var e46 = vm.Garage.Families.First(f => f.Family.Id == "E46");
    if (reel.Find<Button>(b => ReferenceEquals(b.DataContext, e46)) is { } family) reel.Click(family, () => e46.SelectCommand!.Execute(null));
    else e46.SelectCommand!.Execute(null);
    reel.Mark("E46 picked: its models");
    reel.Run(900);
    var model = vm.Garage.Models.First(m => m.Id == "E46-330i");
    if (reel.Find<ListBoxItem>(i => ReferenceEquals(i.DataContext, model)) is { } row) reel.Click(row, () => vm.Garage.SelectModel(model.Id));
    else vm.Garage.SelectModel(model.Id);
    reel.Overview();
    reel.Mark("E46 330i picked");
    reel.Run(900);
    reel.Click(reel.Button("Open demo car"), () => vm.Garage.OpenDemoCommand.Execute(null));
    reel.Until(() => vm.Session.IsConnected && vm.CurrentPage == vm.Vehicle);
    reel.Mark("The E46's page");
    reel.Run(600);
    reel.Click(reel.Button("Run quick test"), () => vm.Vehicle.RunCommand.Execute(null));
    reel.Until(() => vm.Vehicle.IsScanning);
    if (CarDrawing() is { } drawing) reel.Focus(drawing, pad: 20);
    reel.Mark("Quick test on the E46");
    reel.Speed = 1.5;
    reel.Until(() => !vm.Vehicle.IsScanning && vm.Vehicle.HasScan, 30000);
    reel.Speed = 1;
    reel.Mark("Done: the E46's result");
    reel.Overview();
    reel.Run(3500);
}

void StepByStep()
{
    EnsureDemo();
    Go(vm.Tests);
    vm.Tests.SelectById("vacuum-leak-hunter");
    reel.Run(600);
    reel.Overview();
    reel.Mark("Vacuum leak hunter picked: what it needs and how long");
    reel.Run(2000);
    vm.Tests.Speed = 10;
    reel.Click(reel.Button("Start"), () => vm.Tests.StartCommand.Execute(null));
    reel.Run(600);
    if (reel.Find<ItemsControl>(i => ReferenceEquals(i.ItemsSource, vm.Tests.Charts)) is { } charts) reel.Focus(charts, pad: 60);
    reel.Speed = 3;
    UntilMarkingInstructions(() => vm.Tests.IsAsking, 120000);
    reel.Speed = 1;
    if (reel.Find<TextBlock>(t => t.Text == vm.Tests.Question) is { } question && question.GetVisualAncestors().OfType<Border>().FirstOrDefault() is { } box)
        reel.Focus(box, pad: 40);
    reel.Mark($"It asks: \"{vm.Tests.Question}\"");
    reel.Run(3500);
    var choice = vm.Tests.Choices[0];
    if (reel.Find<Button>(b => ReferenceEquals(b.DataContext, choice)) is { } pick) reel.Click(pick, () => choice.PickCommand.Execute(null));
    else choice.PickCommand.Execute(null);
    reel.Mark($"Answer picked: \"{choice.Label}\"");
    reel.Speed = 3;
    UntilMarkingInstructions(() => vm.Tests.HasResult && !vm.Tests.IsRunning, 120000);
    reel.Speed = 1;
    reel.Run(400);
    if (reel.Find<ItemsControl>(i => ReferenceEquals(i.ItemsSource, vm.Tests.Findings)) is { } findings)
    {
        reel.ScrollTo(findings, 900, top: 200);
        reel.Focus(findings, pad: 40);
    }
    reel.Mark("The verdict");
    reel.Run(4500);
}

void CompareHealthy()
{
    EnsureDemo();
    Go(vm.Live);
    reel.Click(reel.Button("Start"), () => vm.Live.StartCommand.Execute(null));
    reel.Run(500);
    reel.Click(reel.Button("Record"), () => vm.Live.RecordCommand.Execute(null));
    reel.Mark("Live data, recording");
    reel.Run(3500);
    reel.Click(reel.Button("Stop"), () => vm.Live.Stop());
    reel.Run(300);
    reel.Click(reel.Button("Analyse"), () => vm.Live.AnalyseCommand.Execute(null));
    reel.Until(() => vm.CurrentPage == vm.Analyse);
    reel.Overview();
    reel.Mark("The recording as graphs, played back");
    if (reel.Find<Button>(b => ReferenceEquals(b.Command, vm.Analyse.PlayCommand)) is { } play) reel.Click(play, () => vm.Analyse.PlayCommand.Execute(null));
    reel.Run(3000);
    if (vm.Analyse.Playing) vm.Analyse.PlayCommand.Execute(null);
    // A warm demo drive with a small vacuum leak (lean at idle), against the example healthy baseline: the same
    // made-up drive the screenshots use.
    var rng = new Random(15);
    var csv = new System.Text.StringBuilder("time,\"Engine speed (rpm)\",\"Coolant temperature (°C)\",\"Short-term fuel trim bank 1 (%)\",\"Long-term fuel trim bank 1 (%)\",\"Engine load (%)\"\n");
    for (var i = 0; i < 400; i++)
    {
        var rpm = i < 120 ? 760 + rng.Next(-20, 20) : 900 + (i - 120) * 14 + rng.Next(-60, 60);
        var lean = Math.Max(0, 13 - (rpm - 760) / 120.0);
        csv.AppendLine(System.FormattableString.Invariant($"{i * 0.25:0.00},{rpm},91,{lean * 0.3 + rng.NextDouble() * 3 - 1.5:0.0},{lean * 0.7 + 1:0.0},{18 + rpm / 220.0:0}"));
    }
    vm.Analyse.Load(Klemme15.Core.DataLog.Parse(csv.ToString()), "a demo drive");
    reel.Run(300);
    var baseline = vm.Analyse.Baselines.FirstOrDefault(b => b.Curve?.Y == "stft1+ltft1") ?? vm.Analyse.Baselines.Skip(1).FirstOrDefault()
        ?? throw new InvalidOperationException("no baseline fits the demo drive");
    var combo = reel.Find<ComboBox>(c => ReferenceEquals(c.ItemsSource, vm.Analyse.Baselines));
    if (combo is not null) reel.Click(combo, () => vm.Analyse.Baseline = baseline); else vm.Analyse.Baseline = baseline;
    reel.Mark("A demo drive against the healthy baseline");
    reel.Run(400);
    if (combo is not null) reel.Focus(reel.Card(combo), pad: 20);
    reel.Mark($"The band is healthy engines; it says: \"{vm.Analyse.BaselineSummary}\"");
    reel.Run(5000);
}

// ---------------------------------------------------------------- the recorder

sealed class Reel(Window window, string dir)
{
    public const int Fps = 30;
    private Process? _ffmpeg;
    private Stream? _stdin;
    private byte[] _buffer = [];
    private bool _hasFrame;
    private int _frame;
    private double _virtual;
    private readonly Stopwatch _clock = new();
    private TimeSpan _last;
    private readonly List<Dictionary<string, object>> _events = [];
    private string _id = "", _title = "";
    private string? _note;
    private int _width, _height;

    /// <summary>Playback speed for what's recorded next (2 = twice as fast in the video).</summary>
    public double Speed { get; set; } = 1;

    public void Begin(string id, string title, string? note)
    {
        _id = id; _title = title; _note = note;
        _frame = 0; _virtual = 0; _hasFrame = false; Speed = 1;
        _events.Clear();
        _clock.Restart();
        _last = TimeSpan.Zero;
    }

    public void End()
    {
        if (_ffmpeg is not null)
        {
            _stdin!.Flush();
            _stdin.Dispose();
            _ffmpeg.WaitForExit();
            _ffmpeg = null;
        }
        File.WriteAllText(Path.Combine(dir, _id + ".json"), JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = _id, ["title"] = _title, ["note"] = _note, ["fps"] = Fps, ["frames"] = _frame,
            ["width"] = _width, ["height"] = _height, ["events"] = _events,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"  {_id}: {_frame} frames ({_frame / (double)Fps:0.0} s)");
    }

    /// <summary>Records for <paramref name="ms"/> milliseconds of real time.</summary>
    public void Run(int ms)
    {
        var until = _clock.Elapsed + TimeSpan.FromMilliseconds(ms);
        do Capture(); while (_clock.Elapsed < until);
    }

    public void Until(Func<bool> condition, int timeoutMs = 20000)
    {
        var start = _clock.Elapsed;
        while (!condition())
        {
            if ((_clock.Elapsed - start).TotalMilliseconds > timeoutMs) throw new TimeoutException($"{_id}: the app didn't reach the expected state");
            Capture();
        }
    }

    /// <summary>Waits without recording (setting up a scene).</summary>
    public void WaitQuietly(Func<bool> condition, int timeoutMs = 20000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException($"{_id}: setup didn't finish");
            Idle(20);
        }
        // Real time that passed while not recording isn't in the video.
        _last = _clock.Elapsed;
    }

    public void Idle(int ms)
    {
        var sw = Stopwatch.StartNew();
        do { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(10); }
        while (sw.ElapsedMilliseconds < ms);
    }

    private void Capture()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var now = _clock.Elapsed;
        _virtual += (now - _last).TotalSeconds / Speed;
        _last = now;
        var due = Math.Max(_hasFrame ? 0 : 1, (int)Math.Floor(_virtual * Fps) - _frame);
        if (due == 0) { Thread.Sleep(5); return; }
        var bitmap = window.CaptureRenderedFrame();
        if (bitmap is null) return;
        using (var fb = bitmap.Lock())
        {
            if (_ffmpeg is null) Start(fb.Size.Width, fb.Size.Height, fb.Format == PixelFormat.Rgba8888 ? "rgba" : "bgra");
            var row = fb.Size.Width * 4;
            for (var y = 0; y < fb.Size.Height; y++)
                Marshal.Copy(fb.Address + y * fb.RowBytes, _buffer, y * row, row);
        }
        for (var i = 0; i < due; i++) _stdin!.Write(_buffer);
        _frame += due;
        _hasFrame = true;
    }

    private void Start(int width, int height, string pixelFormat)
    {
        _width = width; _height = height;
        _buffer = new byte[width * height * 4];
        _ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt {pixelFormat} -s {width}x{height} -r {Fps} -i - -c:v libx264 -preset veryfast -crf 12 -pix_fmt yuv420p \"{Path.Combine(dir, _id + ".mp4")}\"")
        { RedirectStandardInput = true, UseShellExecute = false })!;
        _stdin = _ffmpeg.StandardInput.BaseStream;
    }

    // ------------------------------------------------------------ where the action is

    private Rect Box(Control c) =>
        c.TransformToVisual(window) is { } m ? new Rect(c.Bounds.Size).TransformToAABB(m) : new Rect(0, 0, window.Width, window.Height);

    private void Event(string type, Rect r) => _events.Add(new()
    {
        ["t"] = type, ["f"] = _frame, ["x"] = Math.Round(r.X), ["y"] = Math.Round(r.Y), ["w"] = Math.Round(r.Width), ["h"] = Math.Round(r.Height),
    });

    /// <summary>
    /// Points the camera at a control (with some room around it). <paramref name="width"/> below 1 keeps only the left
    /// part of a wide card, where its text is, so the text comes out big enough to read on a phone.
    /// </summary>
    public void Focus(Control c, double pad = 36, double width = 1)
    {
        Idle(30);
        var box = Box(c);
        var b = new Rect(box.X, box.Y, box.Width * width, box.Height).Inflate(pad);
        Event("focus", b.Intersect(new Rect(0, 0, window.Width, window.Height)));
    }

    public void Overview() => Event("focus", new Rect(0, 0, window.Width, window.Height));

    /// <summary>Notes what the video shows from this moment on, for the script (timelines/&lt;scene&gt;.txt).</summary>
    public void Mark(string what) => _events.Add(new() { ["t"] = "mark", ["f"] = _frame, ["label"] = what });

    /// <summary>The cursor travels to a control, clicks, and <paramref name="action"/> runs.</summary>
    public void Click(Control c, Action action)
    {
        var b = Box(c);
        Event("move", b);
        Run(700);
        Event("click", b);
        action();
        Run(250);
    }

    public void Type(string text, Action<string> set, int perChar = 90)
    {
        for (var i = 1; i <= text.Length; i++) { set(text[..i]); Run(perChar); }
    }

    // ------------------------------------------------------------ finding things and scrolling

    public T? Find<T>(Func<T, bool>? match = null) where T : Control =>
        window.GetVisualDescendants().OfType<T>().FirstOrDefault(x => x.IsEffectivelyVisible && (match?.Invoke(x) ?? true));

    public Button Button(string content) =>
        Find<Button>(b => (b.Content as string) == content) ?? throw new InvalidOperationException($"{_id}: no button \"{content}\"");

    public TextBlock Text(string start) =>
        Find<TextBlock>(t => t.Text?.StartsWith(start, StringComparison.Ordinal) == true) ?? throw new InvalidOperationException($"{_id}: no text \"{start}\"");

    /// <summary>The card or panel around a control, if there is one.</summary>
    public Control Card(Control c)
    {
        for (Visual? v = c; v is not null; v = v.GetVisualParent())
            if (v is Border b && (b.Classes.Contains("card") || b.Classes.Contains("panel"))) return b;
        return c;
    }

    private ScrollViewer? Scroller(Control c) => c.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();

    private void Animate(ScrollViewer s, double to, int ms)
    {
        var from = s.Offset.Y;
        to = Math.Clamp(to, 0, Math.Max(0, s.Extent.Height - s.Viewport.Height));
        var start = _clock.Elapsed;
        while (true)
        {
            var t = Math.Min(1, (_clock.Elapsed - start).TotalMilliseconds / ms);
            var e = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
            s.Offset = new Vector(s.Offset.X, from + (to - from) * e);
            Capture();
            if (t >= 1) break;
        }
    }

    /// <summary>Scrolls smoothly until the control sits <paramref name="top"/> (layout pixels) below the top of its scroller.</summary>
    public void ScrollTo(Control c, int ms, double top = 120)
    {
        if (Scroller(c) is not { } s || s.Content is not Visual content) return;
        if (c.TranslatePoint(new Point(0, 0), content) is not { } at) return;
        Animate(s, at.Y - top, ms);
    }

    public void ScrollBy(Control inside, double by, int ms)
    {
        if (Scroller(inside) is { } s) Animate(s, s.Offset.Y + by, ms);
    }

    public void ScrollTop(int ms)
    {
        foreach (var s in window.GetVisualDescendants().OfType<ScrollViewer>().Where(x => x.IsEffectivelyVisible && x.Offset.Y > 0))
            Animate(s, 0, ms);
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
