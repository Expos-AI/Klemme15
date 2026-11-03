# Contributing

Thanks for helping make car diagnostics open.

## Report a test drive

The most valuable contribution right now is confirming Klemme15 on real cars.

1. Connect, run a quick test, and if you can, read live data with the engine running.
2. Save the report (Quick test page > Save report) and the traffic log (Log page > Save log).
3. Open a [test drive report](https://github.com/Expos-AI/Klemme15/issues/new?template=test-drive.yml) and attach both files. Say which cable you used and anything that looked wrong.

Traffic logs contain your VIN. Remove it if you prefer.

## Share what you know

- **Coding maps**: after verifying or teaching an option on the Coding page, press **Share this map**. It opens a pre-filled issue.
- **Checks and pin notes**: how you tested a part or found a connector, in your own words: [share a check](https://github.com/Expos-AI/Klemme15/issues/new?template=pin-note.yml).
- **Dash messages**: if Klemme15 shows a Check Control message as unknown, tell us the text your dash showed.

## Test the beta

Testers with different cars and cables help most right now: see the [tester guide](docs/BETA.md) and send a [Beta report](https://github.com/Expos-AI/Klemme15/issues/new?template=beta-report.yml).

## Share a healthy baseline

Car running well? On the Analyse page, **Share as a healthy baseline** prepares a log of a warm drive and opens the form; a workflow checks it and builds your engine's baseline. [How it works](docs/BASELINES.md#share-yours).

## Code

- .NET 10, C# latest, nullable enabled. Run `dotnet test tests/Klemme15.Tests` before opening a PR.
- Protocol changes need a test against `VirtualCar`, ideally replaying traffic captured from a real car.
- Keep the layering in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): Core and Protocols must not reference UI or BMW files.
- Anything that writes to a module (coding, adaptations, flashing) needs an issue and a design discussion first.
- New `.cs` and `.axaml` files start with the license header: run `python3 scripts/license-headers.py` to add it (CI checks).

## Data

See [docs/CONTRIBUTING-DATA.md](docs/CONTRIBUTING-DATA.md). Never paste text from BMW software or documents.

## Sign off your commits (DCO)

Every commit in a pull request needs a `Signed-off-by:` line with your real name and email, certifying the
[Developer Certificate of Origin 1.1](https://developercertificate.org/): in short, that you wrote the change or have
the right to submit it under this project's open-source license, and that you understand the contribution is public.
It keeps code copied from somewhere it shouldn't come from (BMW software, a closed tool, someone else's project with
an incompatible license) out of Klemme15.

Add it with `-s`:

```sh
git commit -s -m "Add a check for the DISA valve"
```

Forgot? `git commit --amend -s` for the last commit, or `git rebase --signoff main` for all of them, then force-push
your branch. A check on every pull request tells you which commits are missing it.

## Licensing

By contributing you agree your work is released under GPL-3.0-or-later. You keep the copyright in what you wrote;
the project as a whole is Copyright (C) 2026 Kindric Castro and contributors (see [COPYRIGHT](COPYRIGHT)).
