# Snapshots (.k15): share your whole car

A snapshot is your car as it was at one moment, in one file. Anyone with Klemme15 can open it and explore it read-only, as if they were plugged into your OBD port. Attach it to a forum post or a GitHub issue instead of pasting codes.

## Making one

1. Run a quick test. For more detail, run Live data for a minute and any tests that matter (they're included with their graphs).
2. On the **Faults** page, press **Share snapshot**.
3. Write what's wrong and what you've tried. Whoever opens it sees your note first.
4. Pick what goes in, then **Save snapshot...**

Command line: `klemme15 scan --port COM3 --snapshot mycar --note "Rough idle when cold"`.

## What's in it

| Always | If you tick it | Never |
|---|---|---|
| Every module with its part number and versions | The last minute of live data | The VIN (removed everywhere, including inside coding bytes and logged messages) |
| Every fault with its details | Test results and their graphs | Your name, account or location |
| Dash messages, the car's story | Coding of modules you read this session (view only) | |
| Health trends, battery voltage | The traffic log | |

A `.k15` file is a plain zip: unzip it to see exactly what's inside.

## Opening one

In the **Garage**, press **Open a snapshot...** (or double-click a `.k15` file once it's associated with Klemme15). A banner shows whose car it is and the owner's note. Faults, the car's story, health trends, modules, test results and graphs all work as usual; the live data replays on the **Analyse** page. Press **Close snapshot** on the banner when you're done. Command line: `klemme15 open mycar.k15` (add `--report out.html` or `--forum`).

### In a browser, without Klemme15

Whoever you send a snapshot to doesn't need to install anything: they can open it at **[expos-ai.github.io/Klemme15/viewer.html](https://expos-ai.github.io/Klemme15/viewer.html)**. The page shows the owner's note, the modules, faults with their details, dash messages, the car's story, test results and the last minute of live data. The file is read on their own device and never uploaded, and the page checks every part against its checksum, like the app does. Health trends, graphs, live data replay and coding open in the app only.

## Comparing two snapshots

Take a snapshot before a repair and another after it, then press **Compare two snapshots...** in the Garage and pick the two files, earlier one first. Klemme15 shows:

- faults that are **gone**, **new**, **changed** (stored before, active now) or **still there**, module by module
- modules that stopped answering, started answering, or carry a different part number (replaced or reprogrammed)
- dash messages that appeared or went away
- whether coding differs, and by how many bytes, when both snapshots include coding

A fault in a module that no longer answers is marked as such: a silent module isn't a fixed fault. Background entries (info and shadow memory) are left out. The comparison works on the two files alone, so no car needs to be connected, and it's saved as a page you can send along. It also works for two different cars of the same model, to see how yours differs from a healthy one.

Command line: `klemme15 diff before.k15 after.k15 --report comparison.html`.

## Safety

- Nothing can be written from a snapshot: clearing, coding, service functions and tests are all refused. A snapshot's coding can be looked at, never written to a car, so a snapshot can't be used to copy one car's coding onto another.
- Klemme15 is built to open strangers' files safely: it checks every part against its checksum, refuses damaged or oversized files, ignores anything it doesn't expect and never runs anything from the file.
- The VIN is scrubbed in every form Klemme15 knows (full, short, as bytes in coding and in the traffic log). A VIN split across two logged messages can slip through, so the traffic log is off by default.
