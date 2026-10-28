<h1 align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/brand/klemme15-lockup-dark.svg">
    <img alt="Klemme15" src="docs/brand/klemme15-lockup-light.svg" width="440">
  </picture>
</h1>

*Named after terminal 15 (Klemme 15), the ignition-on line every BMW diagnosis starts with.*

**Open-source BMW diagnostics for everyone.** Scan every module, read and clear faults, watch live engine data and export shareable reports. Use a cheap K+DCAN cable on Windows, macOS or Linux.

> **Status: early alpha.** Confirmed on a real E90 over D-CAN (VIN and modules read); most other models are so far tested against the built-in demo cars. Read-only functions (scan, faults, readings, live data) are the focus; service functions are experimental until confirmed on real cars. Please test on your car and [report what you see](CONTRIBUTING.md#report-a-test-drive).

Klemme15 is an independent project. It is not affiliated with or endorsed by BMW AG. BMW, ISTA, INPA and EDIABAS are trademarks of BMW AG.

---

## Why Klemme15

| | Dealer-style tools | Klemme15 |
|---|---|---|
| Cost | Licensed or grey-market | Free, GPLv3 |
| Platforms | Windows only, heavy install | Windows, macOS, Linux; one small app |
| Quick test speed | Slow, many steps | Built for speed: modules that aren't fitted are skipped after a short timeout (under 1 s for a full E90 scan in the simulator; real-car timing still to be measured) |
| Without BMW files | — | Works out of the box with Klemme15's own protocol stack |
| With your BMW files | Yes | Optional **EDIABAS mode** runs your own SGBDs for full coverage (Windows) |
| Reports | Printouts | HTML / Markdown / JSON you can post on a forum |
| Transparency | Closed | Every byte on the wire visible in the traffic log |
| No car handy | — | Built-in **demo car** (E46 and E90) |

## Features

- **Garage**: pick your exact model (E46 323i through E90 335i). Every model has its own **demo car** with the modules it actually has and the faults it's known for, so you can try everything without a cable.
- **See where faults are**: a top-view drawing of the car. Each module lights up where it physically sits as the quick test reaches it, and every fault is pinned to the part it points at: a misfire on cylinder 3, lean codes at the intake, a thermostat at the front of the engine, a fuel pump fault at the tank.
- **Quick test**: checks battery voltage, finds every fitted module with one broadcast, identifies each and reads its fault memory. Covers all 44 known E90-family modules and 23 E46 modules, including options like xDrive, TPMS, alarm, memory seats and the convertible top.
- **ISTA-grade texts with your own files** (Windows): Klemme15 finds your EDIABAS, INPA or ISTA SGBD files automatically and reads faults the way ISTA does on these cars: official fault text for your exact module version, mileage, frequency, environment conditions, and the info memory.
- **Official BMW meaning for every code, on any OS**: one click in the Garage downloads the free community fault database [bmw-unified-db](https://github.com/gediz/bmw-unified-db) (13 MB, checksum-verified), or Klemme15 reads the `.prg` files from your own ISTA/INPA install. Klemme15 works out which version of each module your car has (the same code means different things on different versions), shows BMW's English where BMW wrote one, and puts the rest into English with its own glossary, with a Translate button for BMW's original German. See [where meanings come from](docs/FAULT-DATA.md).
- **Researched meanings without BMW files**: 202 BMW fault codes (106 high confidence) researched from owner forums, shop guides and BMW service bulletins, written in our own words, each with its sources and engine scope; plus all 2,066 standard OBD-II codes.
- **Faults**: BMW and OBD-II codes, active vs stored vs info memory, plain-English descriptions, practical tips, a "check here" location, and where each meaning comes from (official SGBD text, or community meaning with confidence and clickable sources). Clear per module or all at once.
- **Readings**: every status value each module reports, like INPA's module screens: oxygen sensor voltages and fuel trims per bank, VANOS positions, injectors, knock, throttle and pedal, every wheel speed, steering angle, battery sensor, temperatures, switch states and learned values. Grouped by system, labelled in English, searchable, with auto-refresh and one-click pinning to Live data. Values are computed by BMW's own scripts from your ISTA/INPA/EDIABAS files (Windows, macOS and Linux), and nothing is changed on the car. Try it on any demo car.
- **Troubleshooting guides**: for common problems (mixture, misfires, VANOS/Valvetronic, cooling, fuel pressure, boost, charging, bus faults, wheel speed sensors, steering angle, steering lock, oxygen sensor heaters, secondary air, airbags), a **Troubleshoot** button under the fault gives step-by-step checks, with shortcuts to the readings that confirm each step. Written by contributors in plain words; add your own in `src/Klemme15.Data/db/guides.json`.
- **Service functions** (experimental): register a new battery, calibrate the steering angle sensor, reset automatic gearbox adaptations, initialise the windows, and component tests (radiator fan, fuel pump, thermostat, electric water pump, secondary air pump, throttle, DISA flap). Each one explains what it does and when to use it, checks the car first (ignition, engine off, battery voltage, car stationary), asks you to confirm every step, runs once and shows the module's answer. They follow BMW's own procedure for your module version; battery registration also works without BMW files on E90-family engines. Off until you switch on experimental functions. See [docs/SERVICE.md](docs/SERVICE.md).
- **Live data**: RPM, temperatures, fuel trims, load, MAF, boost, voltage and more, with sparklines, min/max and CSV recording.
- **Expert mode** (EDIABAS): Tool32-style job runner; pin any numeric result to the live data page.
- **Reports**: one click to save a scan as HTML, Markdown or JSON.
- **Traffic log**: every byte sent and received, savable for bug reports.
- Dark and light themes. Command line for scripting and headless use.

| Vehicle | Faults |
|---|---|
| ![Vehicle view](https://github.com/Expos-AI/Klemme15/raw/screenshots/11-e90-325i-vehicle.png) | ![Faults](https://github.com/Expos-AI/Klemme15/raw/screenshots/07-faults.png) |
| ![Garage](https://github.com/Expos-AI/Klemme15/raw/screenshots/02-garage-335i.png) | ![Live data](https://github.com/Expos-AI/Klemme15/raw/screenshots/08-live.png) |

Screenshots are rendered automatically by CI from the demo cars on every push, so they always show the current build.

## Supported cars (v0.1)

| Family | Cable switch | Native mode | EDIABAS mode |
|---|---|---|---|
| E46 3 Series: 323i, 325i, 325xi, 328i, 330i, 330xi | K-line | Module ident (DS2), engine OBD-II faults and live data | Full fault memory of every module |
| E90/E91/E92/E93 3 Series: 323i, 325i, 325xi, 328i, 328xi, 330i, 335i | D-CAN | Ident and fault memory of every module (KWP2000), engine OBD-II live data | Full, with BMW fault texts |

Other E-series cars that share these protocols (E39, E53, E60, E70, E87...) mostly need only a module list in the [open database](src/Klemme15.Data/db). F- and G-series (ENET cable) are on the [roadmap](docs/ROADMAP.md).

## Quick start

1. Install the cable's FTDI driver (Windows usually does this automatically).
2. Plug the cable into the OBD port under the dashboard and set its switch: **K-line** for E46 and older, **D-CAN** for E90 and newer.
3. Ignition on (key position 2, engine off) for scans; engine running for live data.
4. Start Klemme15, choose your car and port, press **Connect**, then **Run quick test**.

No car nearby? Pick any model in the garage and press **Open demo car**.

### Command line

```bash
klemme15 ports                                   # find the cable
klemme15 scan --vehicle E9x --port COM3 --report scan.html
klemme15 live --vehicle E46 --port /dev/ttyUSB0 --pids rpm,coolant,stft1,ltft1 --csv drive.csv
klemme15 faults --module DME --port COM3
klemme15 readings --module DME --port COM3 --ediabas "C:\ISTA\Ediabas\Ecu" --filter lambda
klemme15 clear --module DME --port COM3
klemme15 service                                 # list service functions
klemme15 service battery-registration --port COM3 --experimental
klemme15 scan --demo --vehicle E9x --model 335i # try it without a car
```

Official BMW meanings for every code (optional, see [docs/FAULT-DATA.md](docs/FAULT-DATA.md)):

```bash
klemme15 db download                             # fetch bmw-unified-db from its GitHub release (13 MB)
klemme15 code E9x.DSC 5E1A --model 325i          # what does a code mean on a given model?
klemme15 scan --port COM3 --ecu "C:\ISTA\Ediabas\Ecu"   # or read texts from your own ISTA/INPA files
```

On Windows with EDIABAS mode: add `--ediabas C:\EDIABAS\Ecu`, and run any job with `klemme15 job D_0012 IDENT --ediabas C:\EDIABAS\Ecu --port COM3`.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/Expos-AI/Klemme15.git
cd Klemme15
dotnet test tests/Klemme15.Tests              # protocol + simulator tests
dotnet run --project src/Klemme15.App          # desktop app
dotnet run --project src/Klemme15.Cli -- scan --demo
```

For EDIABAS mode on Windows, run `scripts\get-ediabaslib.ps1` once (it downloads EdiabasLib into `external/ediabaslib`); the next build includes EDIABAS mode automatically. On macOS and Linux (or Windows without EdiabasLib) build `Klemme15.CrossPlatform.slnf` rather than `Klemme15.sln`, because the full solution includes the Windows-only EDIABAS project.

## How it works

```
 App (Avalonia)   CLI
        \         /
     Core: scanner, reports, models  <-- Data: open JSON database (modules, codes)
        |
  IDiagnosticBackend
     /            \
 Native backend       EDIABAS backend (Windows)
 DS2 / KWP2000 /      EdiabasLib running YOUR
 OBD-II, clean-room   own SGBD files
     |
 ITransport: K+DCAN serial port  |  virtual car (demo + tests)
```

Details in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Legal and data policy

- Klemme15 **contains no BMW software, SGBD files, fault texts, documents or wiring diagrams**, and contributions must not add any.
- The native protocol stack is written from public standards (ISO 14230, ISO 9141-2, SAE J1979) and community knowledge.
- EDIABAS mode runs files **you already have** from your own installation. They are never bundled or uploaded.
- Fault descriptions in the database are written by contributors in their own words, with sources. See `tools/research` for how they're researched and merged.
- BMW's own fault texts are only ever **read from data the user chooses to provide**: their own ISTA/INPA/EDIABAS files, or the third-party [bmw-unified-db](https://github.com/gediz/bmw-unified-db) download, which that project publishes and licenses itself (it states the texts are BMW-derived). Klemme15 links to it and never copies it into this repository. Details: [docs/FAULT-DATA.md](docs/FAULT-DATA.md).
- Klemme15 never unlocks or reads BMW's protected ISTA databases. The standard OBD-II titles come from python-OBD (GPL-2.0-or-later).

## Safety

Diagnostics are read-only by default. Clearing faults asks first. Service functions check the car, ask you to confirm each requirement and only run when you press Run; unverified ones need the experimental switch. Coding, flashing and anything touching the immobiliser are **not** included: a failed write can disable a module. Never use Klemme15 while driving; have a passenger watch live data or record it to CSV.

## Contributing

The fastest way to help is to **test on your car** and send the saved report and traffic log. See [CONTRIBUTING.md](CONTRIBUTING.md) and [docs/CONTRIBUTING-DATA.md](docs/CONTRIBUTING-DATA.md).

## Credits

- [EdiabasLib](https://github.com/uholeschak/ediabaslib) by Ulrich Holeschak (GPLv3), used for EDIABAS mode.
- [bmw-unified-db](https://github.com/gediz/bmw-unified-db) for the optional BMW fault dictionaries (downloaded by the user, not bundled).
- [Avalonia UI](https://avaloniaui.net) for the cross-platform interface.
- [python-OBD](https://github.com/brendan-w/python-OBD) for the standard OBD-II code titles.
- [Barlow](https://github.com/jpt/barlow) typeface by Jeremy Tribby (SIL Open Font License 1.1, see `src/Klemme15.App/Assets/Fonts/OFL.txt`).

## License

GPL-3.0-or-later. See [LICENSE](LICENSE).
