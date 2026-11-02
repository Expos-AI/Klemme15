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

## Code

- .NET 10, C# latest, nullable enabled. Run `dotnet test tests/Klemme15.Tests` before opening a PR.
- Protocol changes need a test against `VirtualCar`, ideally replaying traffic captured from a real car.
- Keep the layering in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): Core and Protocols must not reference UI or BMW files.
- Anything that writes to a module (coding, adaptations, flashing) needs an issue and a design discussion first.

## Data

See [docs/CONTRIBUTING-DATA.md](docs/CONTRIBUTING-DATA.md). Never paste text from BMW software or documents.

## Licensing

By contributing you agree your work is released under GPL-3.0-or-later.
