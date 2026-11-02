# Known good baselines

A baseline is the healthy range of one value across the engine's operating range: total fuel trim across engine speed, air mass across engine speed, load across engine speed. The Analyse page draws it behind your scatter plot, so you see at a glance where your engine behaves like a healthy one and where it doesn't.

## What you see

- A **shaded band**: where the middle 80 % of healthy readings fall at each engine speed (the 10th to the 90th percentile).
- A **dashed line**: the middle (median) of the healthy readings.
- Your points on top, and a line under the plot: how much of your log sits inside the band, and where it leaves it the most. For example, *"62 % of your readings sit inside the healthy range. Your total trim bank 1 reads above the healthy range around 750 rpm (about 14 % above the median)."* That pattern, lean at idle and fine above it, is the classic sign of a vacuum leak.

Outside the band is a clue, not a verdict. It tells you where to look; the guided tests, chains and multimeter checks tell you what's wrong.

## Why by engine speed, not by time

Two drives never line up in time: a baseline drawn against time would compare a red light with a motorway. What compares fairly is behaviour **at the same operating point**. So every reading in your log is placed by its engine speed, grouped into 250 rpm steps, and compared with the healthy range at that step. When you were, and how long you spent there, drops out.

Only readings with a **warm engine** count (coolant above 70 °C, when the log has coolant temperature), because a cold engine runs richer on purpose.

## Where baselines come from

- **Shipped with Klemme15** (`src/Klemme15.Data/db/baselines.json`). For now this holds only a clearly labelled **example** made up for the demo car, to show how overlays work. It's never shown as a real engine.
- **Made by you.** On the Analyse page, **Make a baseline from this log** builds one from the open log (engine speed against total trim per bank, air mass and load, whichever the log has). Do it when your car runs well, and compare later drives against it: your own car is the best baseline for your own car.
- **Added from a file** someone shares with you (**Add a baseline file…**).

Your own baselines are saved in `Klemme15/baselines` under your application data folder. They contain only the curves: no VIN.

## Honest baselines

- **One car is a hint, not an answer key.** Every baseline says how many cars it's built from, and one built from fewer than three says "treat it as a hint".
- Baselines are per engine (N52, N54...). One for another engine isn't offered.
- Modified cars (intake, exhaust, tune) read differently and are kept apart from stock ones.
- Klemme15 won't ship a "healthy N52" baseline until real healthy N52 logs from at least three cars are in. A made-up one would send people chasing problems that aren't there.

## Share yours

If your car is healthy, make a baseline and share it with the [Share a healthy baseline](https://github.com/Expos-AI/Klemme15/issues/new?template=healthy-baseline.yml) form. Baselines from several cars of the same engine are combined (the percentiles are taken over all of their readings) and shipped with the next release, with the number of cars shown.

## The file

A baseline is a small JSON file:

```json
{
  "id": "mine-n52b30-20261102-1412",
  "title": "My N52B30 on 2 Nov 2026",
  "engines": ["N52B30"],
  "conditions": "Warm engine, idle and steady part-throttle.",
  "cars": 1,
  "curves": [
    {
      "y": "stft1+ltft1",
      "x": "rpm",
      "filter": "coolant > 70",
      "binWidth": 250,
      "points": [ { "x": 750, "p10": -4.1, "median": 0.6, "p90": 4.8, "samples": 412 } ]
    }
  ]
}
```

- `y` and `x` are Klemme15 signal names (`rpm`, `load`, `maf`, `coolant`, `stft1`, `ltft1`...). `stft1+ltft1` is the total trim of bank 1. Logs from other tools work too: columns are matched by name ("Engine speed", "RPM").
- `filter` uses the same small expression language as [diagnostic chains](CHAINS.md).
- A file whose percentiles are out of order, or that has no curve with at least two points, is refused.
