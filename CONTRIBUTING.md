# Contributing

Klemme15 gets better with every car it's tried on. Here's how to help, from easiest to most involved.

## Test it on your car

Field validation on owners' cars is the most valuable contribution right now. The [tester guide](docs/BETA.md) says what to try (E36 owners: the [E36 tester guide](docs/E36-BETA.md) and its [report form](https://github.com/Expos-AI/Klemme15/issues/new?template=e36-beta.yml)). When you're done:

1. On the Faults page, press **Share snapshot**, tick **the traffic log**, and save the `.k15` file. It holds your car's modules, faults and the traffic between Klemme15 and the car, with the VIN removed.
2. Put it in a `.zip` and attach it to a [test report](https://github.com/Expos-AI/Klemme15/issues/new?template=beta-report.yml), with your car, your cable and what looked wrong.

## Share what you know

- **Coding maps**: after verifying or learning an option on the Coding page, press **Share this map**. It opens a pre-filled issue, so owners of the identical module can use it too.
- **Checks and connector notes**: how you tested a part or where you found a connector: [share a check](https://github.com/Expos-AI/Klemme15/issues/new?template=pin-note.yml).
- **A healthy baseline**: car running well? On the Analyse page, **Share as a healthy baseline** prepares a log of a warm drive and opens the form; a workflow checks it and builds your engine's baseline ([how](docs/BASELINES.md#share-yours)).
- **Dash messages**: if Klemme15 shows a dash message as unknown, tell us the text your dash showed.
- **Fault meanings**: see [docs/CONTRIBUTING-DATA.md](docs/CONTRIBUTING-DATA.md). Never paste text from BMW software or documents.

## Code

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/Expos-AI/Klemme15.git
cd Klemme15
dotnet test tests/Klemme15.Tests              # protocol and simulator tests
dotnet run --project src/Klemme15.App          # the app
dotnet run --project src/Klemme15.Cli -- scan --demo
```

For EDIABAS mode on Windows, run `scripts\get-ediabaslib.ps1` once (it downloads EdiabasLib into `external/ediabaslib`); the next build includes EDIABAS mode. On macOS and Linux, build `Klemme15.CrossPlatform.slnf` rather than `Klemme15.sln`, which includes the Windows-only EDIABAS project.

- Keep the layering in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): Core and Protocols must not reference the UI or BMW files.
- Protocol changes need a test against `VirtualCar`, ideally replaying traffic captured from a real car.
- Anything that writes to a module (coding, adaptations) needs an issue and a design discussion first. Flashing, keys, the immobiliser and mileage are out of scope ([why](docs/ROADMAP.md#not-planned-on-purpose)).
- New `.cs` and `.axaml` files start with the license header: `python3 scripts/license-headers.py` adds it (CI checks).

### Where things are

| Folder | What's in it |
|---|---|
| `src/` | The app, the command-line tool, protocols, data and the phone dashboard server ([architecture](docs/ARCHITECTURE.md)) |
| `tests/` | Unit and simulator tests |
| `docs/` | Guides, and the website (`index.html`, served by GitHub Pages) |
| `data/baselines/` | Healthy-engine logs shared by owners |
| `web/dashboard/` | Phone dashboard source; its build is committed to `src/Klemme15.Telemetry/wwwroot/` |
| `tools/` | Developer tools: screenshots, logo, the N52 baseline model, research data |
| `scripts/` | EdiabasLib download, license headers, packaging, baseline intake |
| `bucket/`, `packaging/winget/` | Scoop and winget recipes, updated by CI on each release (winget isn't submitted yet: see docs/PACKAGING.md) |
| `build/` | MSBuild settings for the optional EDIABAS mode |
| `external/` | Not committed: EdiabasLib, downloaded by `scripts/get-ediabaslib.*` |

## Sign off your commits (DCO)

Every commit in a pull request needs a `Signed-off-by:` line with your real name and email, certifying the [Developer Certificate of Origin 1.1](https://developercertificate.org/): that you wrote the change or have the right to submit it under this project's license, and that you understand the contribution is public. It keeps code from places it shouldn't come from (BMW software, a closed tool, a project with an incompatible license) out of Klemme15.

```sh
git commit -s -m "Add a check for the DISA valve"
```

Forgot? `git commit --amend -s` for the last commit, or `git rebase --signoff main` for all of them, then force-push your branch. A check on every pull request lists the commits missing it.

## Licensing

By contributing you agree your work is released under GPL-3.0-or-later. You keep the copyright in what you wrote; the project as a whole is Copyright (C) 2026 Kindric Castro and contributors (see [COPYRIGHT](COPYRIGHT)).
