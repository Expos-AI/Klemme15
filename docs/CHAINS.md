# Diagnostic chains: write down how you find a fault

A diagnostic chain is a mechanic's reasoning written as data: what to measure, what to ask, how to compare, what it points to. Klemme15 runs it step by step with live graphs, and anyone can contribute one by editing `src/Klemme15.Data/db/chains.json`.

Chains are read-only by design. They can read live values, ask a question, wait for a condition, calculate and conclude. They can't switch anything on or write anything; when a part needs driving, a chain points to the existing test, which has its own checks.

## Shape

```json
{
  "id": "vacuum-leak-hunter",
  "title": "Vacuum leak hunter (lean codes)",
  "summary": "What it does, in one paragraph.",
  "requirements": "What the car must be doing before it starts.",
  "duration": "About 2 minutes",
  "faults": ["29E0", "P0171"],
  "sources": ["https://..."],
  "steps": [ ... ],
  "verdicts": [ ... ],
  "otherwise": { ... }
}
```

`faults` lists the codes the chain is offered for: a **Run the chain** button appears under those faults.

## Steps

| type | does | fields |
|---|---|---|
| `require` | reads one value; stops with a message if it's out of range | `signal`, `min`, `max`, `stop`, `ifAvailable` |
| `measure` | averages values while a condition holds (e.g. rpm 2,200-2,900) | `id`, `say`, `signals`, `optional`, `while`, `min`, `max`, `samples`, `timeoutSeconds` |
| `ask` | asks a question with buttons | `id`, `say`, `choices` (`value`, `label`) |
| `compute` | stores the result of an expression | `id`, `expr` |
| `suggest` | adds a "next step" note | `say`, `test` |

Any step can have `when`: it only runs when that expression is true.

Signals: `rpm`, `coolant`, `voltage`, `stft1`, `ltft1`, `stft2`, `ltft2`, `maf`, `load`, `iat`, `map`, `throttle`, `timing`, `baro`, `o2b1s1`, `o2b1s2`, `oiltemp`, `ambient`, and sums like `stft1+ltft1`. A measured value is stored as `id.signal` (`idle.stft1_ltft1`, with `.min`, `.max` and `id.count`). An answer is stored as `id` (the choice's position) and `id.value` (1 for the chosen one). `displacement` is the engine size in litres.

## Expressions

Numbers, names, `+ - * /`, `< <= > >= == !=`, `and or not`, brackets, and `abs()`, `min()`, `max()`. That's all: an expression can't reach files, the car or the app. A value that wasn't measured makes comparisons false, so a chain still works on engines that don't report everything. Klemme15 checks every name is measured, asked or computed before it's used, and refuses a chain that doesn't add up.

## Verdicts

Checked in order; the first `maxVerdicts` (default 2) that hold are shown. `severity` is `ok`, `watch` or `problem`. In `text`, `{idle.stft1_ltft1:+0.0;-0.0}` inserts a value.

## Rules for contributors

- Your own words, with sources. Thresholds are rules of thumb: say so, and say "points to", never "definitely".
- Nothing that needs the engine held at a speed by the computer, or a part switched on: the person does the driving and the pedal.
- Add a test in `tests/Klemme15.Tests/ChainTests.cs` with a made-up car showing the pattern your chain finds.
