# v1.0 architecture: Ghost Hunter, Known Good baselines, Live topology

Design for the three v1.0 features. They read and present data only: no flashing, no bootloader access, no writes to the key module (CAS), and nothing here writes to any module at all. Each one is built from parts that already exist (live buffer, snapshots, chain expressions, `DataLog`, the car drawing), so most of the work is wiring and UI.

Names in `code` are real types in the codebase today unless marked **new**.

---

## 1. Trigger-based datalogging: the Ghost Hunter

**Goal:** catch an intermittent fault on a normal drive. The owner sets a rule before driving; Klemme15 watches in the background and saves a snapshot around the moment the rule fires, with nobody touching the laptop.

### Rules reuse the chain expression language

A rule is a condition in the same safe expression language diagnostic chains use (`ChainExpression`): numbers, signal names, `+ - * /`, comparisons, `and or not`, `abs min max`. Nothing new to learn, nothing new to secure, and rules can be shared like chains.

```
rpm > 4000 and rough3 > 2.5          misfire-like roughness on cylinder 3 at high rpm
voltage < 11.5                        the voltage dip behind random dash warnings
coolant > 108                         overheating on a hill
abs(stft1 - stft2) > 15               one bank suddenly far off the other
```

**About misfires:** these engine computers don't report a live misfire counter over the cable; misfires are counted internally and show up as faults. The live signal that matters is per-cylinder **roughness** (`rough1`…`rough8`, already read by `EngineSignals`), which rises before and during a misfire. The rule editor offers only signals the connected car actually reports.

### Data structures (Core, **new** file `GhostHunter.cs`)

```csharp
/// <summary>One condition to watch for while driving.</summary>
public sealed record TriggerRule
{
    public required string Id { get; init; }
    public required string Title { get; init; }          // "Cylinder 3 rough at high rpm"
    public required string When { get; init; }          // a ChainExpression over live signals
    public int HoldSamples { get; init; } = 3;           // must be true this many readings in a row (debounce)
    public TimeSpan Before { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan After { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMinutes(5);  // no burst of captures for one event
    public int MaxCaptures { get; init; } = 5;           // per drive, so a stuck condition can't fill the disk

    /// <summary>Signal names the expression uses: what the watcher has to poll.</summary>
    public IReadOnlySet<string> Signals => ChainExpression.Parse(When).Names();   // new: Names() lists the variables
}

/// <summary>A rule firing, written into the snapshot's manifest.</summary>
public sealed record TriggerEvent(string RuleId, string Title, string When, DateTimeOffset At,
    IReadOnlyDictionary<string, double> ValuesAtTrigger);

public enum WatchState { Idle, Watching, Capturing, CoolingDown, Stopped }
```

The snapshot format gains one optional manifest field, so older Klemme15 versions still open these files:

```csharp
public sealed record SnapshotManifest
{
    // ...existing fields...
    public TriggerEvent? Trigger { get; init; }          // new: why this snapshot was taken
}
```

### The watcher

```csharp
/// <summary>
/// Polls the rule's signals (plus a fixed set for context: rpm, speed, coolant, voltage, trims), keeps a rolling
/// two-minute window, and when a rule holds for HoldSamples readings, records After seconds more and writes a snapshot.
/// </summary>
public sealed class GhostHunter(ISignalSource signals, IReadOnlyList<TriggerRule> rules,
    Func<CarSnapshot> buildBase, Func<CarSnapshot, Task> save, TimeProvider clock)
{
    public WatchState State { get; private set; }
    public event Action<TriggerEvent>? Fired;          // the UI shows a big, glanceable "Caught: …" banner
    public event Action<WatchStatus>? Status;          // readings/s, rules armed, captures so far

    public async Task RunAsync(CancellationToken ct)
    {
        // 1. signals = union of every rule's Signals + context set; read them together each tick (~2-5 Hz on D-CAN).
        // 2. Push each reading into a LiveBuffer sized max(Before)+max(After).
        // 3. Evaluate each armed rule; count consecutive trues; on HoldSamples → Capturing.
        // 4. Keep reading for rule.After, then: csv = buffer slice [t-Before, t+After];
        //    snapshot = buildBase() with LiveCsv = csv, Manifest.Trigger = event; await save(snapshot).
        // 5. Cooldown, then re-arm. Stop arming a rule after MaxCaptures.
        // A failed read is logged and skipped, never fatal: a long drive must survive a bus hiccup.
    }
}
```

`LiveBuffer` (exists) gets a `Window` parameter (exists) and a new `Slice(from, to)` that returns CSV for a time range. `save` writes to `Klemme15/captures/` in the user's folder and adds the file to a **Captures** list.

The base snapshot is built once when watching starts (`SessionState.BuildSnapshot`, exists), so a capture doesn't need a fresh quick test mid-drive. Optionally, after a capture, the engine computer's fault memory is re-read (read-only) and attached, to show whether the event also set a fault.

### UI: Live data page, **Ghost Hunter** panel

- **Rules**: a list with presets (misfire-like roughness, voltage dip, overheating, bank mismatch, lean spike) and "Add rule" with a text box for the condition. Typing is validated live with `ChainExpression.Parse` and the car's available signals, and shows "Fires when …" in plain words.
- **Start watching**: one big button. While watching, the page shows only large status text ("Watching 3 rules · 4.2 readings/s · 0 captures") and a full-width banner when something is caught. Nothing needs a tap while driving; the panel says so.
- **Captures**: each one opens like any snapshot (read-only, live data on the Analyse page with the trigger moment marked), can be shared, or deleted.
- Headless: `klemme15 serve --watch rules.json` runs the same watcher on a Raspberry Pi left on the OBD port, with captures shown on the phone dashboard.

### Safety and limits

- Read-only: the watcher only reads values; quick test, coding and service functions are disabled while it runs, because there's one diagnostic bus.
- It keeps the car's modules awake while the ignition is on, like any connected tool. It stops when the engine stops and reminds the owner to switch the ignition off.
- Polling rate falls with the number of signals; K-line cars (E46 and older) manage about 1-2 readings per second, enough for slow faults but not for a single misfire. The panel shows the real rate.

### Steps

1. `ChainExpression.Names()`; `LiveBuffer.Slice()`; `SnapshotManifest.Trigger`.
2. `GhostHunter` with a fake `ISignalSource` and `TimeProvider` in tests: fires on hold, respects cooldown and max captures, slices the right window, survives read errors.
3. Ghost Hunter panel, captures list, trigger marker on the Analyse page.
4. Preset rules in `db/triggers.json`; `serve --watch`.

---

## 2. Known Good baselines

**Goal:** an answer key behind the owner's own data, so "is this normal?" becomes a picture.

### Overlay by operating point, not by time

Two drives never line up in time: a baseline drawn behind a time graph would compare a stop light with a motorway. What's comparable is behaviour **at the same operating point**: air mass at a given rpm and load, fuel trims across rpm, coolant against time since start. So a baseline is a **band** over an operating axis (median with a healthy range), drawn behind the Analyse page's scatter and map views, where the owner's points then fall inside or outside it.

### Format: a baseline pack (`.k15b`, a zip like snapshots)

```
manifest.json      engine, variant, conditions, contributors, sources, licence (CC BY 4.0)
bands.json         the bands Klemme15 draws (computed from the logs)
logs/*.csv         the healthy logs it was built from (optional, for re-computing and checking)
```

```csharp
/// <summary>A healthy range of one value across one operating axis (e.g. air mass across rpm, at part load).</summary>
public sealed record BaselineBand
{
    public required string Y { get; init; }               // "maf"
    public required string X { get; init; }               // "rpm"
    public string? Filter { get; init; }                  // ChainExpression on each sample: "load > 20 and load < 40"
    public string? NormaliseBy { get; init; }             // e.g. "displacement" or "baro/101.3" (altitude)
    public required IReadOnlyList<BandPoint> Points { get; init; }   // one per X bin
}

public sealed record BandPoint(double X, double P10, double Median, double P90, int Samples);

public sealed record BaselinePack
{
    public required string Id { get; init; }              // "n52-msv70-part-load"
    public required string Title { get; init; }
    public required IReadOnlyList<string> Engines { get; init; }     // "N52B30" etc.
    public IReadOnlyList<string> Variants { get; init; } = [];       // "MSV70": MSV80 cars differ
    public string Conditions { get; init; } = "";        // "warm engine, 15-30 °C intake air, below 500 m"
    public int Cars { get; init; }                        // how many different healthy cars it's built from
    public required IReadOnlyList<BaselineBand> Bands { get; init; }
    public IReadOnlyList<string> Contributors { get; init; } = [];
    public IReadOnlyList<string> Sources { get; init; } = [];
}

/// <summary>Builds bands from healthy logs: filter samples, bin by X, take percentiles. Pure function, testable.</summary>
public static class BaselineBuilder
{
    public static BaselineBand Build(IReadOnlyList<DataLog> logs, string y, string x, string? filter, double binWidth, int minSamples = 30);
}

/// <summary>How far the owner's log sits from a band: share of points outside, and where (e.g. "below P10 above 3,000 rpm").</summary>
public sealed record BandComparison(double ShareOutside, double MedianOffsetPercent, string Summary);
```

### Honest baselines

- **One car isn't a baseline.** A pack shows how many cars it comes from, and the overlay says "built from 1 car, treat as a hint" until there are at least 3.
- Engine variant matters (MSV70 vs MSV80, N52 vs N52K), and so do altitude and intake temperature. Packs say what they cover; Klemme15 offers a pack only when the engine matches and warns when the log's conditions are outside the pack's.
- Contributed logs are CSV files of engine values: no VIN, no location. Contributors give permission under CC BY 4.0, through the same pull request or issue-form route as coding maps.

### UI: Analyse page

- A **Baseline** dropdown lists packs for the engine in the open log (shipped in `db/baselines/`, or imported).
- The scatter view draws the band behind the points: a translucent fill between P10 and P90 and a thin median line, in a neutral colour so the owner's points stay readable. `ScatterPlot` (exists) gets a `Band` property rendered before the points.
- Under the graph, one line from `BandComparison`: "82 % of your points are inside the healthy range; above 3,000 rpm your air mass reads about 12 % below it."

### Steps

1. `BaselineBuilder`, `BaselinePack` reader (same strict rules as snapshots), tests with synthetic logs.
2. Band rendering in `ScatterPlot`, dropdown and comparison line on the Analyse page.
3. A contributor guide (`docs/BASELINES.md`) and an issue form for healthy logs; ship packs once three or more cars per engine are in.

---

## 3. Live topology view

**Goal:** the top-view car drawing shows the car alive: coolant over the engine, voltage over the battery, wheel speeds at each corner, each value where it physically happens.

### Anchors are data

The drawing already places modules (`EcuDefinition.Position`) and components (`components.json`: `wheel_fl`, `battery`, `thermostat`, `cyl1`…). A new `db/topology.json` maps live values to those anchors:

```csharp
/// <summary>A live value drawn on the car.</summary>
public sealed record TopologyGauge
{
    public required string Signal { get; init; }          // "coolant", or a BMW measurement value "E9x.DSC:<result name>" (names confirmed per module version)
    public required string Anchor { get; init; }          // "component:thermostat", "module:E9x.FRM", "component:wheel_fl"
    public required string Label { get; init; }           // "Coolant"
    public string Format { get; init; } = "0";            // "0.0" for volts
    public string Unit { get; init; } = "";
    public double? WarnBelow { get; init; }               // the tile turns amber or red outside these
    public double? WarnAbove { get; init; }
    public double? AlarmAbove { get; init; }
    public double RateHz { get; init; } = 2;              // how often it's worth reading
}
```

Examples: coolant at the thermostat (warn above 108 °C), voltage at the battery (warn below 12.2 V engine off and below 13.4 V running), the four wheel speeds at their corners, intake air at the intake, fuel trims per bank over each cylinder bank, each cylinder's roughness over that cylinder.

### Polling: one scheduler, fair to the bus

```csharp
/// <summary>
/// Reads every gauge at its own rate, grouped by module so one request fetches several values, round-robin across
/// modules, and backs off when the bus is slow. Runs only while the Vehicle page shows the live view.
/// </summary>
public sealed class TopologyPoller(IDiagnosticBackend backend, IMeasurementReader? measurements, IReadOnlyList<TopologyGauge> gauges)
{
    public event Action<IReadOnlyDictionary<string, GaugeReading>>? Updated;
    public Task RunAsync(CancellationToken ct);
}

public readonly record struct GaugeReading(double? Value, DateTimeOffset At, bool Stale);
```

- Engine values come through `EngineSignals` (exists). Values from other modules (wheel speeds from DSC, interior temperature from the climate module) come through BMW's measurement values (`IMeasurementReader`, exists), with the downloaded tables or the owner's BMW files. A gauge whose value isn't available is simply not drawn, so the view works on every connection and gets richer with more data.
- Wheel speeds read 0 when parked; the view says "Drive slowly to see wheel speeds" rather than showing four zeros.
- The poller pauses during a quick test, coding or a service function, and stops when the page is hidden (the Live page already does this, for the same reason: on a real E90, background polling doubled the quick test's time).

### UI: Vehicle page, **Live** toggle on the drawing

- `MarkerViewModel` (exists) gets a `GaugeText` and `GaugeLevel`; the drawing shows a small value tile next to each anchor, coloured by its warn and alarm limits.
- Tapping a tile pins the value to the Live data page and starts recording it, so the topology view is also the quickest way into a log.
- The fault markers stay: a module with an active fault and a value out of range is the first place to look.

### Steps

1. `db/topology.json` for the E90 family, and a test that every anchor exists on the drawing and every signal is known.
2. `TopologyPoller` with grouping and rates; tests with the virtual car.
3. Tiles on `CarDrawing`, Live toggle, tap-to-pin.
4. E46 and the other families' anchors.

---

## Order for v1.0

1. **Ghost Hunter** first: it's mostly wiring of existing parts (live buffer, snapshots, expressions), and it finds faults no one-off scan can.
2. **Live topology** next: visible, satisfying, and reuses the drawing and the live values.
3. **Baselines** last: the code is small, but they need real healthy logs from several cars per engine, so start collecting now (issue form) while 1 and 2 are built.
