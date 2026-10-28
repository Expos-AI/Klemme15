# Service functions

The **Service** page (and `klemme15 service`) runs functions that change something in the car: registrations, calibrations, adaptation resets and component tests. All of them are listed in [`src/Klemme15.Data/db/service.json`](../src/Klemme15.Data/db/service.json), written by contributors in their own words.

## How a function runs

1. **Availability.** Klemme15 finds the module the function talks to and its version (the name the module reports in EDIABAS mode, otherwise the most likely version from its part number and engine). A function is offered only for versions it lists.
2. **Checks.** Klemme15 reads the battery voltage, engine speed and road speed, plus the ignition state where the cable reports it. Anything it can't measure, you confirm by hand. Failed checks block the function.
3. **Confirmations.** Each function lists things only you can check ("the wheels point straight ahead"). Each one must be ticked.
4. **Run.** The function runs once:
   - **With your BMW files** (EDIABAS mode): Klemme15 runs BMW's own job for your module version from your files, and then reads back the result where BMW provides a result job.
   - **Without BMW files**: only where the exact request is documented for those module versions (currently battery registration on E90-family engines: KWP2000 StartRoutineByLocalIdentifier `31 30 00 10 00`, positive reply `71 30`).
5. **Result.** You see the module's answer. Every request and reply is in the traffic log.

Each run needs fresh checks and confirmations.

## Experimental

A function is marked `"verified": false` until someone has run it on a real car with Klemme15 and confirmed it worked. Unverified functions only run after you switch on **experimental functions** (bottom left of the Service page, or `--experimental` on the command line). If you run one, please [report what happened](../CONTRIBUTING.md#report-a-test-drive) with your traffic log, so it can be marked verified.

## What is not included, on purpose

No coding, no flashing, nothing that writes to the immobiliser (CAS/EWS) or changes the car's configuration. A battery of a different type or capacity needs the vehicle order changed (coding), which Klemme15 doesn't do. A test in `tests/Klemme15.Tests/ServiceTests.cs` rejects catalog entries that look like coding, flashing or immobiliser jobs.

## Adding a function

Add an entry to `service.json`:

- `id`, `title`, `category`, `module` (short module id, e.g. `DME`, `DSC`, `EGS`, `FRM`), `summary`, `when`, `risk` (`low`, `medium`, `high`), `verified: false`.
- `before` and `after`: steps in plain words.
- `checks`: `ignition`, `engineOff` or `engineRunning`, `minVoltage`, `stationary`, and `confirm` items.
- `implementations`: the module versions (lowercase variant names) and BMW's job and arguments for them. `resultJob`/`resultArgs` read the outcome afterwards. `native` (hex) and `expect` only when the request is documented for exactly those versions; native requests may only start a routine (`31`) or control an output (`30`).

Look up job names and arguments in the job lists of your own BMW files (`klemme15 jobs <variant> --ediabas <folder>`) or in bmw-unified-db; write descriptions in your own words.
