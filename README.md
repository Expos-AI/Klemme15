# OpenHood

**Open-source BMW diagnostics for everyone.** Scan every module, read and clear faults, watch live engine data and export shareable reports. Use a cheap K+DCAN cable on Windows, macOS or Linux.

> **Status: early alpha.** The protocol stack is tested against a built-in virtual car but has **not yet been confirmed on a real vehicle**. Read-only functions (scan, faults, live data) are the focus. Please test on your car and [report what you see](CONTRIBUTING.md#report-a-test-drive).

OpenHood is an independent project. It is not affiliated with or endorsed by BMW AG. BMW, ISTA, INPA and EDIABAS are trademarks of BMW AG.

---

## Why OpenHood

| | Dealer-style tools | OpenHood |
|---|---|---|
| Cost | Licensed or grey-market | Free, GPLv3 |
| Platforms | Windows only, heavy install | Windows, macOS, Linux; one small app |
| Quick test speed | Slow, many steps | Built for speed: modules that aren't fitted are skipped after a short timeout (under 1 s for a full E90 scan in the simulator; real-car timing still to be measured) |
| Without BMW files | — | Works out of the box with OpenHood's own protocol stack |
| With your BMW files | Yes | Optional **EDIABAS mode** runs your own SGBDs for full coverage (Windows) |
| Reports | Printouts | HTML / Markdown / JSON you can post on a forum |
| Transparency | Closed | Every byte on the wire visible in the traffic log |
| No car handy | — | Built-in **demo car** (E46 and E90) |

## Features

- **Quick test**: identifies every module on the car and reads its fault memory, with progress and cancel.
- **Faults**: BMW and OBD-II codes, present vs stored, plain-English descriptions and practical hints; clear per module with confirmation.
- **Live data**: RPM, temperatures, fuel trims, load, MAF, voltage and more, with sparklines, min/max and CSV recording.
- **Expert mode** (EDIABAS): Tool32-style job runner; pin any numeric result to the live data page.
- **Reports**: one click to save a scan as HTML, Markdown or JSON.
- **Traffic log**: every byte sent and received, savable for bug reports.
- **Command line** for scripting and headless use.

## Supported cars (v0.1)

| Family | Cable switch | Native mode | EDIABAS mode |
|---|---|---|---|
| E46 3 Series (1998-2006): 320i, 325i, 330i... | K-line | Module ident (DS2), engine OBD-II faults and live data | Full fault memory of every module |
| E90/E91/E92/E93 3 Series (2005-2013) | D-CAN | Ident and fault memory of every module (KWP2000), engine OBD-II live data | Full, with BMW fault texts |

Other E-series cars that share these protocols (E39, E53, E60, E70, E87...) mostly need only a module list in the [open database](src/OpenHood.Data/db). F- and G-series (ENET cable) are on the [roadmap](docs/ROADMAP.md).

## Quick start

1. Install the cable's FTDI driver (Windows usually does this automatically).
2. Plug the cable into the OBD port under the dashboard and set its switch: **K-line** for E46 and older, **D-CAN** for E90 and newer.
3. Ignition on (key position 2, engine off) for scans; engine running for live data.
4. Start OpenHood, choose your car and port, press **Connect**, then **Run quick test**.

No car nearby? Tick **Demo car**.

### Command line

```bash
openhood ports                                   # find the cable
openhood scan --vehicle E9x --port COM3 --report scan.html
openhood live --vehicle E46 --port /dev/ttyUSB0 --pids rpm,coolant,stft1,ltft1 --csv drive.csv
openhood faults --module DME --port COM3
openhood clear --module DME --port COM3
openhood scan --demo                             # try it without a car
```

On Windows with EDIABAS mode: add `--ediabas C:\EDIABAS\Ecu`, and run any job with `openhood job D_0012 IDENT --ediabas C:\EDIABAS\Ecu --port COM3`.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/Expos-AI/OpenHood.git
cd OpenHood
dotnet test tests/OpenHood.Tests              # protocol + simulator tests
dotnet run --project src/OpenHood.App          # desktop app
dotnet run --project src/OpenHood.Cli -- scan --demo
```

For EDIABAS mode on Windows, run `scripts\get-ediabaslib.ps1` once (it downloads EdiabasLib into `external/ediabaslib`); the next build includes EDIABAS mode automatically. On macOS and Linux (or Windows without EdiabasLib) build `OpenHood.CrossPlatform.slnf` rather than `OpenHood.sln`, because the full solution includes the Windows-only EDIABAS project.

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

- OpenHood **contains no BMW software, SGBD files, fault texts, documents or wiring diagrams**, and contributions must not add any.
- The native protocol stack is written from public standards (ISO 14230, ISO 9141-2, SAE J1979) and community knowledge.
- EDIABAS mode runs files **you already have** from your own installation. They are never bundled or uploaded.
- Fault descriptions in the database are written by contributors in their own words.

## Safety

Diagnostics are read-only by default. Clearing faults asks first. Coding and flashing are **not** included: a failed write can disable a module. Never use OpenHood while driving; have a passenger watch live data or record it to CSV.

## Contributing

The fastest way to help is to **test on your car** and send the saved report and traffic log. See [CONTRIBUTING.md](CONTRIBUTING.md) and [docs/CONTRIBUTING-DATA.md](docs/CONTRIBUTING-DATA.md).

## Credits

- [EdiabasLib](https://github.com/uholeschak/ediabaslib) by Ulrich Holeschak (GPLv3), used for EDIABAS mode.
- [Avalonia UI](https://avaloniaui.net) for the cross-platform interface.

## License

GPL-3.0-or-later. See [LICENSE](LICENSE).
