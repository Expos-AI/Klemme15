# Contributing

Thanks for helping make car diagnostics open.

## Report a test drive

The most valuable contribution right now is confirming Klemme15 on real cars.

1. Connect, run a quick test, and if you can, read live data with the engine running.
2. Save the report (Quick test page > Save report) and the traffic log (Log page > Save log).
3. Open an issue titled `Test: <chassis> <engine> <year>` (e.g. `Test: E90 N52 2007`) and attach both files. Say which cable you used and anything that looked wrong.

Traffic logs contain your VIN. Remove it if you prefer.

## Code

- .NET 10, C# latest, nullable enabled. Run `dotnet test tests/Klemme15.Tests` before opening a PR.
- Protocol changes need a test against `VirtualCar`, ideally replaying traffic captured from a real car.
- Keep the layering in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): Core and Protocols must not reference UI or BMW files.
- Anything that writes to a module (coding, adaptations, flashing) needs an issue and a design discussion first.

## Data

See [docs/CONTRIBUTING-DATA.md](docs/CONTRIBUTING-DATA.md). Never paste text from BMW software or documents.

## Licensing

By contributing you agree your work is released under GPL-3.0-or-later.
