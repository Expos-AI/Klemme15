# Roadmap

## 0.1 — foundation (this release)
- Native protocol stack: DS2, KWP2000/BMW-FAST on D-CAN, OBD-II on K-line
- EDIABAS backend (Windows) using the user's own SGBDs
- Quick test, faults, live data, expert job runner, traffic log, reports
- Desktop app (Windows, macOS, Linux) and CLI
- Virtual E46 and E90 for development and demos

## 0.2 — proven on real cars
- Verify module addresses on E46 and E90 cars from community test reports
- Replay real captured traffic in tests
- Ident layouts per module family (part number, HW/SW, coding index)
- More vehicle families: E39, E53, E60/E61, E70, E8x

## 0.3 — deeper diagnostics
- DS2 fault-memory decoders for common E46 modules
- Freeze-frame / environment data for faults
- Guided checks: "lean codes" walkthrough using live fuel trims, misfire counters, VANOS checks
- Service functions with explicit confirmation: battery registration, service interval reset

## Later
- F-series and G-series over ENET (HSFZ / UDS)
- Coding with automatic backup and verified restore
- Android build
- Community knowledge base of fixes linked to fault codes
