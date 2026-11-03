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

In the **Garage**, press **Open a snapshot...** (or double-click a `.k15` file once it's associated with Klemme15). A banner shows whose car it is and the owner's note. Faults, the car's story, health trends, modules, test results and graphs all work as usual; the live data replays on the **Analyse** page. Command line: `klemme15 open mycar.k15` (add `--report out.html` or `--forum`).

## Safety

- Nothing can be written from a snapshot: clearing, coding, service functions and tests are all refused. A snapshot's coding can be looked at, never written to a car, so a snapshot can't be used to copy one car's coding onto another.
- Klemme15 is built to open strangers' files safely: it checks every part against its checksum, refuses damaged or oversized files, ignores anything it doesn't expect and never runs anything from the file.
- The VIN is scrubbed in every form Klemme15 knows (full, short, as bytes in coding and in the traffic log). A VIN split across two logged messages can slip through, so the traffic log is off by default.
