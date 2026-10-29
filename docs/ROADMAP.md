# Roadmap

## 0.1 — foundation
- Native protocol stack: DS2, KWP2000/BMW-FAST on D-CAN, OBD-II on K-line
- EDIABAS backend (Windows) using the user's own SGBDs
- Quick test, faults, live data, expert job runner, traffic log, reports
- Desktop app (Windows, macOS, Linux) and CLI
- Virtual E46 and E90 for development and demos

## 0.2 — see the car
- Garage with every E46 and E9x model, each with its own demo car
- Top-view car drawing: modules light up where they sit, faults pinned to the part they point at
- New visual design with dark and light themes
- Automatic UI screenshots in CI

## 0.3 — ISTA-grade results (this release)
- EDIABAS mode finds the user's own SGBD files automatically and reads fault details and info memory like ISTA
- 202 researched BMW codes with sources and confidence; all standard OBD-II codes
- Every known E90-family and E46 module address; one-broadcast module detection
- Battery check before scans, clear all faults

## 0.4 — proven on real cars
- Verify module addresses on E46 and E90 cars from community test reports
- Replay real captured traffic in tests
- Ident layouts per module family (part number, HW/SW, coding index)
- More vehicle families: E39, E53, E60/E61, E70, E8x, each with its own drawing

## 0.5 — deeper diagnostics
- DS2 fault-memory decoders for common E46 modules
- Freeze-frame / environment data for faults
- Guided checks: "lean codes" walkthrough using live fuel trims, misfire counters, VANOS checks
- Service functions with explicit confirmation: battery registration, service interval reset

## Later
- Code-signed Windows builds (Azure Trusted Signing) so there's no SmartScreen prompt on a direct download; winget and Scoop already avoid it.
- F-series and G-series over ENET (HSFZ / UDS)
- Coding: more comfort options, and a shared list of module files people have verified, so fewer users need NCS Expert's trace
- Android build
- Community knowledge base of fixes linked to fault codes

## Not planned, on purpose

- Immobiliser work of any kind (DME/CAS alignment, key programming, ISN): it is what makes a stolen car start.
- Flashing module software, and coding the vehicle order or VIN (for example a battery type change): the riskiest writes there are. Comfort coding (since 0.11) stays limited to comfort modules, with automatic backup, verification against NCS Expert and read-back.
- Coding the key module (CAS): comfort closing with the remote, auto-lock and key settings live there, next to the immobiliser data.
- Bundling BMW's wiring diagrams (WDS): they are BMW's documents. Guides can name connectors and pins in our own words.
- Closed wireless adapters (MHD, Thor): their protocols aren't public. Open bridges (RFC 2217/TCP) work today; BLE adapters are planned.
