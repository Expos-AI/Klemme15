# Known good baselines

A baseline is the healthy range of one value across the engine's operating range: total fuel trim across engine speed, air mass across engine speed, load across engine speed. The Analyse page draws it behind your scatter plot, so you see at a glance where your engine behaves like a healthy one and where it doesn't.

## What you see

- A **shaded band**: where the middle 80 % of healthy readings fall at each engine speed (the 10th to the 90th percentile).
- A **dashed line**: the middle (median) of the healthy readings.
- Your points on top, and a line under the plot: how much of your log sits inside the band, and where it leaves it the most. For example, *"62 % of your readings sit inside the healthy range. Your total trim bank 1 reads above the healthy range around 750 rpm: 13.4 % where healthy engines read 0.5 %."* That pattern, lean at idle and fine above it, is the classic sign of a vacuum leak.

Outside the band is a clue, not a verdict. It tells you where to look; the guided tests, chains and multimeter checks tell you what's wrong.

## Why by engine speed, not by time

Two drives never line up in time: a baseline drawn against time would compare a red light with a motorway. What compares fairly is behaviour **at the same operating point**. So every reading in your log is placed by its engine speed, grouped into 250 rpm steps, and compared with the healthy range at that step. When you were, and how long you spent there, drops out.

Only readings with a **warm engine** count (coolant above 70 °C, when the log has coolant temperature), because a cold engine runs richer on purpose.

## Where baselines come from

- **Shipped with Klemme15** (`src/Klemme15.Data/db/baselines.json`). For now this holds two profiles, neither measured on a car:
  - **N52 physics model (not measured).** Worked out from the engine's size and how full its cylinders get (see [the model](#the-n52-physics-model)). Its bands are wide on purpose and it says so in the app. It catches big problems, like a car running far lean at idle; landing inside it doesn't prove an engine is healthy.
  - A clearly labelled **example** for the demo car, to show how overlays work.
- **Made by you.** On the Analyse page, **Make a baseline from this log** builds one from the open log (engine speed against total trim per bank, air mass and load, whichever the log has). Do it when your car runs well, and compare later drives against it: your own car is the best baseline for your own car.
- **Added from a file** someone shares with you (**Add a baseline file...**).

Your own baselines are saved in `Klemme15/baselines` under your application data folder. They contain only the curves: no VIN.

## Limits

- **One car is a hint, not an answer key.** Every baseline says how many cars it's built from, and one built from fewer than three says "treat it as a hint".
- Baselines are per engine (N52, N54...). One for another engine isn't offered.
- Modified cars (intake, exhaust, tune) read differently and are kept apart from stock ones.
- A model is labelled as a model. Klemme15 won't present anything as a measured "healthy N52" until real healthy N52 logs from at least three cars are in: a made-up one would send people chasing problems that aren't there. When they are, the measured baseline replaces the model.

## The N52 physics model

`tools/baseline-model/n52_model.py` writes the model profile and a 60-second reference log (`n52_model_log.csv`: warm idle at 660 rpm, a no-load hold at 2,500 rpm, a full-throttle pull to 6,500 rpm).

- **Air mass** = engine size (2,996 cc) × rpm ÷ 2 (each cylinder fills once every two turns) × air density (1.184 kg/m³, sea level at 25 °C) × how full the cylinders get: about 22 % at idle and no load, where Valvetronic barely opens the valves, and 88-95 % at full throttle. That gives about 4.3 g/s at idle, 15 g/s at a no-load 2,500 rpm and 170 g/s at 6,500 rpm on full throttle.
- **Bands**: ±30 % around the model at idle and no load, ±25 % at full throttle, and ±10 % total fuel trim. Altitude alone takes about 10 % of the air away per 1,000 m, and the 190 and 218 hp versions, air temperature and each air mass meter differ too.
- **Split by load**: a no-load 2,500 rpm and a full-throttle 2,500 rpm are very different, so air mass has an idle / no-load curve (load below 40 %) and a full-throttle curve (load above 80 %). These curves need engine load in your log; without it they aren't offered.
- Logs from other tools work: columns like `STFT_Bank1(%)`, `CoolantTemp(C)` and `MAF(kg/h)` are recognised, and kg/h is shown as g/s.

## Share yours

If your car is healthy, share a log of a warm drive:

1. Open the log on the **Analyse** page (record one on the Live data page first: idle for half a minute, then drive through a range of engine speeds past 3,000 rpm).
2. Check the engine code next to **Share as a healthy baseline** (Klemme15 fills it in from your car, for example `N52B30`; a 2.5 L N52 is `N52B25`) and press the button.
3. Klemme15 checks the log is warm and covers idle and a range of engine speeds, saves a small `.csv` with only the engine values (no VIN, no date, no location) and opens the [Share a healthy baseline](https://github.com/Expos-AI/Klemme15/issues/new?template=healthy-baseline.yml) form. Attach the file and submit.

A workflow on GitHub (**Baseline intake**) then checks the file again with `scripts/baseline_intake.py`, stores it in [`data/baselines/contrib`](../data/baselines/README.md), rebuilds that engine's community baseline with the same maths Klemme15 uses, comments the result on your issue and opens a pull request. Once it's merged, the next release offers the baseline to every owner of that engine, with the number of cars shown. Logs from several cars of the same engine are pooled: the percentiles are taken over all of their readings.

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
