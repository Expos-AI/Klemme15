<h1 align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/brand/klemme15-lockup-dark.svg">
    <img alt="Klemme15" src="docs/brand/klemme15-lockup-light.svg" width="440">
  </picture>
</h1>

<p align="center">
  <a href="https://github.com/Expos-AI/Klemme15/actions/workflows/build.yml"><img alt="Build" src="https://github.com/Expos-AI/Klemme15/actions/workflows/build.yml/badge.svg"></a>
  <a href="https://github.com/Expos-AI/Klemme15/releases"><img alt="Latest release" src="https://img.shields.io/github/v/release/Expos-AI/Klemme15?include_prereleases&label=download"></a>
  <a href="https://github.com/Expos-AI/Klemme15/actions/workflows/codeql.yml"><img alt="CodeQL" src="https://github.com/Expos-AI/Klemme15/actions/workflows/codeql.yml/badge.svg"></a>
  <a href="https://scorecard.dev/viewer/?uri=github.com/Expos-AI/Klemme15"><img alt="OpenSSF Scorecard" src="https://api.scorecard.dev/projects/github.com/Expos-AI/Klemme15/badge"></a>
  <a href="LICENSE"><img alt="License: GPL-3.0" src="https://img.shields.io/github/license/Expos-AI/Klemme15"></a>
  <a href="https://paypal.me/KindricC"><img alt="Support with PayPal" src="https://img.shields.io/badge/support-PayPal-0070BA?logo=paypal&logoColor=white"></a>
</p>

*Named after terminal 15 (Klemme 15), the ignition-on line every BMW diagnosis starts with.*

**Free, open-source, offline diagnostics for older BMWs.** Read every module's faults and dash warnings and find out what they mean, track your car's health, test a part before you buy it, run service functions, and back up or code comfort modules. You need a K+DCAN cable and a Windows, macOS or Linux computer. No account, no subscription, nothing sent anywhere.

> **Status: beta.** Validated on a 2006 E90 325i over D-CAN: module identification, faults read and cleared (Shadow / Info memory included), dash message history, live data, the fuel trim test and battery registration; and in EDIABAS mode (your own BMW files): the quick test with BMW's own fault texts and the mileage stored with each fault, and the brake fluid, spark plug and vehicle check service resets. Everything else is developed from published research on each module and verified against simulated vehicles, with field validation in progress. Functions that drive a part or write to a module, battery registration included, remain behind the **experimental** switch until validated. [Become a tester](docs/BETA.md).

| Faults, health and the car's story | Component tests with live graphs |
|---|---|
| ![Faults](https://github.com/Expos-AI/Klemme15/raw/screenshots/07-faults.png) | ![Tests](https://github.com/Expos-AI/Klemme15/raw/screenshots/19-tests.png) |
| **A shared snapshot, opened read-only** | **A diagnostic chain asking its question** |
| ![Snapshot](https://github.com/Expos-AI/Klemme15/raw/screenshots/24-snapshot-faults.png) | ![Chain](https://github.com/Expos-AI/Klemme15/raw/screenshots/25-chain-question.png) |

The screenshots are rendered from the demo cars on every change. [See them all](https://github.com/Expos-AI/Klemme15/tree/screenshots).

## Will it work on my car?

| Family | Cable switch | Without BMW files | With your BMW files (EDIABAS mode, Windows) |
|---|---|---|---|
| E90/E91/E92/E93 3 Series (2006-2013) | D-CAN | Every module's faults and dash messages, engine readings, live data, tests, coding backups, battery registration (most engine computers), Valvetronic learning (N52 engines only) | Every module's readings and service functions, BMW fault texts |
| E81/E82/E87/E88 1 Series, E60/E61 5 Series, E63/E64 6 Series (2007 on), E70 X5 / E71 X6, E84 X1, E89 Z4 | D-CAN | Like the E90 family | Like the E90 family |
| E46 3 Series (1999-2006), E39 5 Series, E53 X5, E38 7 Series, E85/E86 Z4 | K-line | Module identification, engine faults and live data (OBD-II), health history, the fuel trim test | Every module's faults, readings and service functions |
| **Beta:** E36 3 Series (1996-1999), E83 X3 (2004-2006) | K-line | Engine faults, live data and the fuel trim test (OBD-II); the other modules are being mapped with testers | Not tried yet |

All-wheel-drive models (325xi to 535xi, and the later xDrive names), the E46 coupes and convertibles (323Ci to 330Ci) and the 6 Series are listed under their own names. M cars are listed as their own models (E46 M3, E9x M3, E60 M5/M6, 1 Series M Coupe, X5 M/X6 M, Z4 M, E39 M5, E36 M3), and so are the diesels sold outside North America. Tips and tests written for one engine aren't offered on another: a diesel doesn't get the fuel trim test, and the water pump tests are only for engines with an electric pump (N51-N55).

The E90 family is validated on a vehicle. The other families are developed from BMW's module data and verified against simulated vehicles; field validation is in progress, and [test reports](docs/BETA.md) move each one to validated. The E36 and E83 are in development: their module addresses are being mapped from owner reports ([E36 tester guide](docs/E36-BETA.md)). Cars built before 1996 (E30, E32, E34, E31 and early E36) are not supported. E60 and E87-family cars built before March 2007 have no D-CAN and need EDIABAS mode; the 7 Series E65/E66, X3s built from late 2006 on and the Mini are not supported yet. F- and G-series cars use an Ethernet cable and are not supported.

## What you need

- **A K+DCAN USB cable with a K-line / D-CAN switch and an FTDI chip**, the kind sold for INPA and NCS Expert. Used for every real-car test so far: the [AntiBreak K+DCAN cable with switch](https://www.amazon.com/dp/B079KC43WG). Klemme15 checks the cable before it connects ([cable guide](docs/CABLE.md)).
- **A 64-bit computer**: Windows 10 or 11, macOS on Apple Silicon or Intel, or Linux. Nothing else to install.
- **Optional:** BMW's own files from an ISTA, INPA or EDIABAS install on a Windows computer. They add every module's readings, the service functions and BMW's fault texts. Klemme15 never includes them.

## Install

**Windows:** with [Scoop](https://scoop.sh):

```powershell
scoop bucket add klemme15 https://github.com/Expos-AI/Klemme15
scoop install klemme15
```

**Any system:** download the zip for your computer from [Releases](https://github.com/Expos-AI/Klemme15/releases) and unzip it. There's no installer. On Windows and Linux, run `Klemme15`; on a Mac, drag `Klemme15.app` to Applications and open it. The command-line tool is in `cli/`.

| Your computer | Download |
|---|---|
| Windows 10 or 11 | `Klemme15-v<version>-win-x64.zip` |
| Mac with an Apple chip (M1 or later) | `Klemme15-v<version>-osx-arm64.zip` |
| Mac with an Intel chip | `Klemme15-v<version>-osx-x64.zip` |
| Linux (64-bit) | `Klemme15-v<version>-linux-x64.zip` |

### The first time you open it

Klemme15 isn't signed with a paid Microsoft or Apple certificate yet, so both systems ask once whether you trust it:

- **Windows:** if a blue "Windows protected your PC" box appears, click **More info**, then **Run anyway**. (Installing with Scoop skips this.)
- **Mac:** double-click Klemme15. When macOS says it can't check the app, click **Done**, open **System Settings → Privacy & Security**, scroll down and click **Open Anyway** next to Klemme15, then confirm. On macOS 14 or older, you can instead right-click Klemme15 and choose **Open**.
- **Linux:** if it doesn't start, make it executable: `chmod +x Klemme15`.

After that it opens normally. Every release comes with `SHA256SUMS.txt` and a signed build provenance, so you can [check your download](SECURITY.md#verifying-your-download) before you run it, and the whole source is here to read.

### Updating

Press the **arrow button** at the bottom of the sidebar to check for a newer version. When there is one, **Download** gets the file for your computer: unzip it and use it in place of the old folder (your settings, cars and history are kept, since they live in your user folder). Klemme15 also asks once whether to check by itself when it starts, at most once a day. With Scoop, run `scoop update klemme15`.

## Quick start

1. Plug the cable into the OBD port under the dashboard and set its switch: **K-line** for the E46, E39, E53, E38, Z4 E85/E86, X3 E83 (to 2006) and E36, **D-CAN** for the E90 family and newer.
2. Switch the ignition on (key position 2, engine off).
3. In Klemme15, pick your car in the Garage (type the model and year, like "2011 328i", in the search box if you don't know the chassis code) and press **Connect to my car**. Choose the cable's port and press **Check cable**. Fix anything it flags.
4. Press **Connect**, then **Run quick test** on the Vehicle page.

No car nearby? Pick any model and press **Open demo car**.

## What it does

**Find out what's wrong**
- **Quick test**: finds every fitted module with one broadcast, reads its part number and fault memory, and checks the battery. Knows 45 E90-family modules and 23 E46 modules.
- **One cause, not many**: faults across modules that belong together are grouped: one low-voltage event, several modules that lost contact with the same one, or faults from the same mileage.
- **Faults sorted by what matters**: fix now, worth checking, and background entries, so ten codes don't turn into ten parts. Clear one module or all of them.
- **What each code means**: 203 BMW codes for the E90 family researched with sources (shown on the 1 Series, X1, Z4, 5 Series and X5 too, wherever the car has the same module version), all 2,066 standard OBD-II codes, and a meaning from its number range for any other code. BMW's own text for your exact module from the free [bmw-unified-db](https://github.com/gediz/bmw-unified-db) download or your own files ([how](docs/FAULT-DATA.md)).
- **Dash messages**: why the triangle is lit even with no fault code. 396 messages explained, with severity and what to do ([details](docs/CHECK-CONTROL.md)).
- **The car's story**: the cluster remembers its last few dozen dash warnings with the mileage. Klemme15 tells them as a timeline and spots patterns: five systems complaining at once is a voltage dip, not five faults.
- **See where it is**: a top view of the car with each module where it sits, each fault pinned to it, and live values (coolant, battery voltage, engine speed) where they happen.

**Test before you buy parts**
- **Guided tests with live graphs**: fuel trims at idle and 2,500 rpm, VANOS target against actual, cylinder roughness, a rough idle and air conditioning check (does the idle hunt, does it chug when the compressor kicks in, and is it the engine or the mounts?), "why won't it start?", the electric water pump, and a cooling system bleed assistant. Each ends with a verdict.
- **Start from a symptom**: pick what the car does (won't start, rough idle, overheating, battery goes flat, warning lights and more) and get what your car already reports about it, then what to do in order.
- **Diagnostic chains**: a mechanic's reasoning, run step by step. The Vacuum leak hunter, the Charging system check and the Warm-up check (thermostat) are offered under matching faults; anyone can write one ([how](docs/CHAINS.md)).
- **Multimeter and swap checks** under matching faults: connectors, grounds, coils, VANOS solenoids, the pump's power, oxygen sensor heaters.
- **What's due for service**: the car's service list read from the modules that keep it (cluster, engine, brakes, climate), most urgent first, read-only (cars with Condition Based Service, in EDIABAS mode; the cluster's part is validated on an E90).
- **Garage mode**: a high-contrast theme (black on white, larger small print) for working in daylight, next to dark and light.
- **Your units**: km and °C, or miles and °F, chosen in the Garage.
- **Live data and Analyse**: record values to CSV, then scatter and replay the log with a healthy range drawn behind it ([baselines](docs/BASELINES.md)).

**Catch what only happens sometimes**
- **Ghost Hunter**: pick what you're chasing (a misfire, a voltage dip, a lean idle, a chug at idle, overheating, a stall) and drive. Klemme15 saves the minute before and after each time it happens. While it watches, a *Right now* list says what each watch is waiting for ("coolant above 70 °C, now 58 °C"). Every watch is checked against readings from a real E90 325i.
- **Battery drain finder**: what the car recorded about its battery running down, how many days your parked current leaves you, and the fuse-pull hunt for the circuit.
- **Health over time**: each quick test leaves a small record on your computer, so a battery resting lower each week, fuel trims creeping lean or a fault that keeps coming back show up early.

**Do the job yourself** (experimental)
- **78 functions**: service reminder reset, battery registration, the steering lock counter reset for early E90s with a key slot, adaptation resets, Valvetronic and DISA learning, cylinder cut-out, DSC and gearbox functions and more. Battery registration (on most engine computers) and Valvetronic learning (N52 engines only) run without BMW files; the rest run BMW's own procedures from your files (EDIABAS mode, Windows). Each checks the car first and asks you to confirm every requirement ([list](docs/SERVICE-FUNCTIONS.md), [how they're kept safe](docs/SERVICE.md)).

**Code comfort options**
- **Back up, compare and restore** a comfort module's coding on D-CAN cars, without BMW files. Restoring writes to the module, so it's experimental.
- **Quick codes** (experimental): LED bulb warnings off, digital speed, mirror folding and more, only where Klemme15 knows exactly where the option lives on your module: checked against your NCS Expert reading, learned by watching another coding tool, or from a map another owner of the identical module shared. Every write is backed up, read back and undone if anything is off ([walkthrough](docs/QUICK-CODES.md)).
- **Retrofit checker**: notices replaced or added modules and explains what the job needs.
- **Footwell module (FRM3) protection**: the module that can corrupt itself on a voltage dip is only written to at 13.0 V or more.
- **Voltage rules for every write**: nothing is written to any module below 10.5 V, coding needs 12.5 V, and a reading that can't be right is never trusted.

**Share and automate**
- **Snapshots (.k15)**: your whole car in one file, opened read-only by whoever helps you, with the VIN removed ([how](docs/SNAPSHOTS.md)).
- **Snapshot fingerprints**: a short code that shows a shared snapshot file wasn't changed after it was saved.
- **Compare two snapshots**: before and after a repair, see which faults are gone, new or still there, and which modules changed.
- **Buying a used BMW?** The pre-purchase check reads the mileage the modules remember (the engine computer's and cluster's own counts, the mileage each module was last programmed at, and every fault and dash message), compares them with each other and the odometer, and flags a wound-back or swapped cluster. It also flags safety faults and freshly cleared memories. Read-only; every module's figures need your BMW files (EDIABAS mode).
- **Reports** in HTML, Markdown or JSON, a forum post summary, and a traffic log of every byte.
- **Command line** and **headless mode** with a phone dashboard and MQTT ([command line](docs/COMMAND-LINE.md), [headless](docs/HEADLESS.md)).

## Safety

Reading is the default; clearing faults asks first. Service functions and tests that switch a part on check the car, ask you to confirm each requirement, and hand the part back to the module when they finish, fail or you press Stop. Coding is limited to comfort modules, needs a healthy battery voltage and is backed up first. Klemme15 never flashes software, never touches keys or the immobiliser, never codes the key module (CAS), and never changes mileage. Its one job in the key module is clearing the steering lock's event counters ([why](docs/ROADMAP.md#not-planned-on-purpose)).

If a job is cut off (the cable comes out, the laptop sleeps, Windows restarts), Klemme15 remembers it and says what to do next, even after a restart. While a job runs, it keeps the computer awake.

Don't operate Klemme15 while driving: set up the Ghost Hunter or a recording before you set off, or have a passenger watch.

## Privacy

What Klemme15 reads from your car stays on your computer. There's no account, no telemetry and no server, and it only goes online when you ask (a download, a link, a network cable you set up). The [privacy policy](PRIVACY.md) lists every file it saves.

## Safety, legal and transparency

Klemme15 comes with no warranty: you use it at your own risk. [Safety, legal and transparency](docs/LEGAL.md) covers working on the car safely, what experimental means, what Klemme15 never does, copyright and BMW's software, trademarks, how good the fault meanings are, and how Klemme15 is made (AI coding tools help write parts of it). The app shows the short version once when you first start it, and the whole page under the shield button in the sidebar.

## Getting help

- **Cable or connection problems:** the [cable guide](docs/CABLE.md).
- **A bug or a question:** [open an issue](https://github.com/Expos-AI/Klemme15/issues/new/choose). A snapshot of your car (Faults page, **Share snapshot**) shows what happened.
- **A security problem:** report it privately, see [SECURITY.md](SECURITY.md).

## Contributing

Testing on your car helps most: the [tester guide](docs/BETA.md) says what to try. You can also share a coding map, a check for a part you've tested, a healthy baseline from your car's log, or a fault meaning with sources ([how](CONTRIBUTING.md)). Building from source and the architecture are in [CONTRIBUTING.md](CONTRIBUTING.md) and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Support Klemme15

Klemme15 is free and stays free: no paid version, nothing locked, no ads. If it saved you a trip to the shop or a part you didn't need, you can leave a tip with [PayPal](https://paypal.me/KindricC) or through [GitHub Sponsors](https://github.com/sponsors/Expos-AI). Tips are a thank-you, not a purchase: they don't come with support promises or extra features. Testing on your car and reporting how it went helps just as much.

## Legal

- Klemme15 contains no BMW software, SGBD files, fault texts, documents or wiring diagrams, and contributions must not add any. Its own fault meanings are written from public sources.
- The native protocol stack is written from public standards (ISO 14230, ISO 9141-2, SAE J1979) and community knowledge.
- EDIABAS mode runs files you already have from your own installation. They're never bundled or uploaded, and Klemme15 never unlocks BMW's protected ISTA databases ([fault data](docs/FAULT-DATA.md)).
- Klemme15 is an independent project, not affiliated with or endorsed by BMW AG. BMW, ISTA, INPA, NCS Expert, EDIABAS, TIS and WDS are trademarks of BMW AG.

## Credits

- [EdiabasLib](https://github.com/uholeschak/ediabaslib) by Ulrich Holeschak (GPLv3), used for EDIABAS mode.
- [bmw-unified-db](https://github.com/gediz/bmw-unified-db) for the optional BMW fault texts (downloaded by you, not bundled).
- [Avalonia UI](https://avaloniaui.net) for the interface.
- [python-OBD](https://github.com/brendan-w/python-OBD) for the standard OBD-II code titles (GPL-2.0-or-later).
- [Barlow](https://github.com/jpt/barlow) typeface by Jeremy Tribby (SIL Open Font License 1.1).

## License

Copyright (C) 2026 Kindric Castro. Created and lead developed by Kindric Castro.

GPL-3.0-or-later: see [LICENSE](LICENSE) and [COPYRIGHT](COPYRIGHT). Contributions are accepted under the same license with a [Developer Certificate of Origin](CONTRIBUTING.md#sign-off-your-commits-dco) sign-off.
