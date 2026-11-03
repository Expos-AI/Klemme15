<h1 align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/brand/klemme15-lockup-dark.svg">
    <img alt="Klemme15" src="docs/brand/klemme15-lockup-light.svg" width="440">
  </picture>
</h1>

<p align="center">
  <a href="https://github.com/Expos-AI/Klemme15/actions/workflows/build.yml"><img alt="Build" src="https://github.com/Expos-AI/Klemme15/actions/workflows/build.yml/badge.svg"></a>
  <a href="https://github.com/Expos-AI/Klemme15/releases"><img alt="Latest release" src="https://img.shields.io/github/v/release/Expos-AI/Klemme15?include_prereleases&label=download"></a>
  <a href="LICENSE"><img alt="License: GPL-3.0" src="https://img.shields.io/github/license/Expos-AI/Klemme15"></a>
</p>

*Named after terminal 15 (Klemme 15), the ignition-on line every BMW diagnosis starts with.*

**Free, open-source, offline diagnostics for older BMWs.** Read every module's faults and dash warnings and find out what they mean, track your car's health, test a part before you buy it, run service functions, and back up or code comfort modules. You need a K+DCAN cable and a Windows, macOS or Linux computer. No account, no subscription, nothing sent anywhere.

> **Status: beta.** Confirmed on a 2006 E90 325i over D-CAN: module identification, faults read and cleared (Shadow / Info memory included), dash message history, live data, the fuel trim test and battery registration. The rest is built from public research on each module and tested against demo cars. Anything that drives a part or writes to a module stays behind an **experimental** switch until it's confirmed on real cars. [Testers wanted](docs/BETA.md).

| Faults, health and the car's story | Component tests with live graphs |
|---|---|
| ![Faults](https://github.com/Expos-AI/Klemme15/raw/screenshots/07-faults.png) | ![Tests](https://github.com/Expos-AI/Klemme15/raw/screenshots/19-tests.png) |
| **A shared snapshot, opened read-only** | **A diagnostic chain asking its question** |
| ![Snapshot](https://github.com/Expos-AI/Klemme15/raw/screenshots/24-snapshot-faults.png) | ![Chain](https://github.com/Expos-AI/Klemme15/raw/screenshots/25-chain-question.png) |

The screenshots are rendered from the demo cars on every change. [See them all](https://github.com/Expos-AI/Klemme15/tree/screenshots).

## Will it work on my car?

| Family | Cable switch | Without BMW files | With your BMW files (EDIABAS mode, Windows) |
|---|---|---|---|
| E90/E91/E92/E93 3 Series (2006-2013) | D-CAN | Every module's faults and dash messages, engine readings, live data, tests, coding backups, battery registration, Valvetronic learning | Every module's readings and service functions, BMW fault texts |
| E81/E82/E87/E88 1 Series, E60/E61 5 Series, E63/E64 6 Series (2007 on), E70 X5 / E71 X6, E89 Z4 | D-CAN | Like the E90 family | Like the E90 family |
| E46 3 Series (1999-2006), E39 5 Series, E53 X5, E38 7 Series | K-line | Module identification, engine faults and live data (OBD-II), health history, the fuel trim test | Every module's faults, readings and service functions |

The E90 family is confirmed on a real car; the others are built from BMW's module data and tested against demo cars, so please [report how it goes](docs/BETA.md). E60 and E87-family cars built before March 2007 have no D-CAN and need EDIABAS mode. F- and G-series cars use an Ethernet cable and aren't supported yet.

## What you need

- **A K+DCAN USB cable with a K-line / D-CAN switch and an FTDI chip**, the kind sold for INPA and NCS Expert. Used for every real-car test so far: the [AntiBreak K+DCAN cable with switch](https://www.amazon.com/dp/B079KC43WG). Klemme15 checks the cable before it connects ([cable guide](docs/CABLE.md)).
- **A 64-bit computer**: Windows 10 or 11, macOS on Apple Silicon or Intel, or Linux. Nothing else to install.
- **Optional:** BMW's own files from an ISTA, INPA or EDIABAS install on a Windows computer. They add every module's readings, the service functions and BMW's fault texts. Klemme15 never includes them.

## Install

**Windows:**

```powershell
winget install Expos-AI.Klemme15
```

or with [Scoop](https://scoop.sh): `scoop bucket add klemme15 https://github.com/Expos-AI/Klemme15` then `scoop install klemme15`.

**Any system:** download the zip for your computer from [Releases](https://github.com/Expos-AI/Klemme15/releases), unzip it anywhere and run `Klemme15` (the command-line tool is in `cli/`). There's no installer.

| Your computer | Download |
|---|---|
| Windows 10 or 11 | `Klemme15-<version>-win-x64.zip` |
| Mac with an Apple chip (M1 or later) | `Klemme15-<version>-osx-arm64.zip` |
| Mac with an Intel chip | `Klemme15-<version>-osx-x64.zip` |
| Linux (64-bit) | `Klemme15-<version>-linux-x64.zip` |

The Windows build isn't code-signed yet, so a downloaded zip may show a SmartScreen prompt (winget and Scoop avoid it). Every release comes with `SHA256SUMS.txt` and a signed build provenance, so you can [check your download](SECURITY.md#verifying-your-download) before you run it.

## Quick start

1. Plug the cable into the OBD port under the dashboard and set its switch: **K-line** for the E46, E39, E53 and E38, **D-CAN** for the E90 family and newer.
2. Switch the ignition on (key position 2, engine off).
3. In Klemme15, pick your car in the Garage, choose the cable's port and press **Check cable**. Fix anything it flags.
4. Press **Connect**, then **Run quick test** on the Vehicle page.

No car nearby? Pick any model and press **Open demo car**.

## What it does

**Find out what's wrong**
- **Quick test**: finds every fitted module with one broadcast, reads its part number and fault memory, and checks the battery. Knows 45 E90-family modules and 23 E46 modules.
- **Faults sorted by what matters**: fix now, worth checking, and background entries, so ten codes don't turn into ten parts. Clear one module or all of them.
- **What each code means**: 203 BMW codes for the E90 family researched with sources, all 2,066 standard OBD-II codes, and a meaning from its number range for any other code. BMW's own text for your exact module from the free [bmw-unified-db](https://github.com/gediz/bmw-unified-db) download or your own files ([how](docs/FAULT-DATA.md)).
- **Dash messages**: why the triangle is lit even with no fault code. 396 messages explained, with severity and what to do ([details](docs/CHECK-CONTROL.md)).
- **The car's story**: the cluster remembers its last few dozen dash warnings with the mileage. Klemme15 tells them as a timeline and spots patterns: five systems complaining at once is a voltage dip, not five faults.
- **See where it is**: a top view of the car with each module where it sits, each fault pinned to it, and live values (coolant, battery voltage, engine speed) where they happen.

**Test before you buy parts**
- **Guided tests with live graphs**: fuel trims at idle and 2,500 rpm, VANOS target against actual, cylinder roughness, "why won't it start?", the electric water pump, and a cooling system bleed assistant. Each ends with a verdict.
- **Diagnostic chains**: a mechanic's reasoning, run step by step. The Vacuum leak hunter and the Charging system check are offered under matching faults; anyone can write one ([how](docs/CHAINS.md)).
- **Multimeter and swap checks** under matching faults: connectors, grounds, coils, VANOS solenoids, the pump's power, oxygen sensor heaters.
- **Live data and Analyse**: record values to CSV, then scatter and replay the log with a healthy range drawn behind it ([baselines](docs/BASELINES.md)).

**Catch what only happens sometimes**
- **Ghost Hunter**: pick what you're chasing (a misfire, a voltage dip, a lean idle, overheating, a stall) and drive. Klemme15 saves the minute before and after each time it happens.
- **Battery drain finder**: what the car recorded about its battery running down, how many days your parked current leaves you, and the fuse-pull hunt for the circuit.
- **Health over time**: each quick test leaves a small record on your computer, so a battery resting lower each week, fuel trims creeping lean or a fault that keeps coming back show up early.

**Do the job yourself** (experimental)
- **74 functions**: service reminder reset, battery registration, adaptation resets, Valvetronic and DISA learning, cylinder cut-out, DSC and gearbox functions and more. Battery registration and Valvetronic learning run without BMW files; the rest run BMW's own procedures from your files (EDIABAS mode, Windows). Each checks the car first and asks you to confirm every requirement ([list](docs/SERVICE-FUNCTIONS.md), [how they're kept safe](docs/SERVICE.md)).

**Code comfort options**
- **Back up, compare and restore** a comfort module's coding on D-CAN cars, without BMW files. Restoring writes to the module, so it's experimental.
- **Quick codes** (experimental): LED bulb warnings off, digital speed, mirror folding and more, only where Klemme15 knows exactly where the option lives on your module: checked against your NCS Expert reading, learned by watching another coding tool, or from a map another owner of the identical module shared. Every write is backed up, read back and undone if anything is off ([walkthrough](docs/QUICK-CODES.md)).
- **Retrofit checker**: notices replaced or added modules and explains what the job needs.
- **Footwell module (FRM3) protection**: the module that can corrupt itself on a voltage dip is only written to at 13.0 V or more.

**Share and automate**
- **Snapshots (.k15)**: your whole car in one file, opened read-only by whoever helps you, with the VIN removed ([how](docs/SNAPSHOTS.md)).
- **Buying a used BMW?** The pre-purchase check compares the mileage the modules recorded with the odometer and flags safety faults and freshly cleared memories.
- **Reports** in HTML, Markdown or JSON, a forum post summary, and a traffic log of every byte.
- **Command line** and **headless mode** with a phone dashboard and MQTT ([command line](docs/COMMAND-LINE.md), [headless](docs/HEADLESS.md)).

## Safety

Reading is the default; clearing faults asks first. Service functions and tests that switch a part on check the car, ask you to confirm each requirement, and hand the part back to the module when they finish, fail or you press Stop. Coding is limited to comfort modules, needs a healthy battery voltage and is backed up first. Klemme15 never flashes software, never touches keys, the immobiliser or the key module (CAS), and never changes mileage ([why](docs/ROADMAP.md#not-planned-on-purpose)).

If a job is cut off (the cable comes out, the laptop sleeps, Windows restarts), Klemme15 remembers it and says what to do next, even after a restart. While a job runs, it keeps the computer awake.

Don't operate Klemme15 while driving: set up the Ghost Hunter or a recording before you set off, or have a passenger watch.

## Privacy

What Klemme15 reads from your car stays on your computer. There's no account, no telemetry and no server, and it only goes online when you ask (a download, a link, a network cable you set up). The [privacy policy](PRIVACY.md) lists every file it saves.

## Getting help

- **Cable or connection problems:** the [cable guide](docs/CABLE.md).
- **A bug or a question:** [open an issue](https://github.com/Expos-AI/Klemme15/issues/new/choose). A snapshot of your car (Faults page, **Share snapshot**) shows exactly what happened.
- **A security problem:** report it privately, see [SECURITY.md](SECURITY.md).

## Contributing

Testing on your car helps most: the [tester guide](docs/BETA.md) says what to try. You can also share a coding map, a check for a part you've tested, a healthy baseline from your car's log, or a fault meaning with sources ([how](CONTRIBUTING.md)). Building from source and the architecture are in [CONTRIBUTING.md](CONTRIBUTING.md) and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

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
