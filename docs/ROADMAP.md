# Roadmap

What's done, what's next, and what Klemme15 will never do. The [release notes](https://github.com/Expos-AI/Klemme15/releases) list every change.

## Done

- **Foundation**: native DS2, KWP2000 on D-CAN and OBD-II stacks; optional EDIABAS mode with your own BMW files; desktop app for Windows, macOS and Linux; command line; demo cars.
- **Diagnose**: one-broadcast quick test of every module; faults sorted by priority; researched meanings with sources; BMW's own texts from your files or bmw-unified-db; Shadow / Info memory read and cleared; dash messages (Check Control) and their history; the car's story; readings for every module; live data, recording and analysis.
- **Confirmed on a real E90 325i**: module identification, faults, clearing, dash messages, live data, fuel trim test, battery registration.
- **Over time**: health history with drift trends, Your cars, test results as trends.
- **Test and fix**: guided tests with live graphs (fuel trims, VANOS, roughness, rough idle and air conditioning, no-start, cooling bleed, water pump), multimeter checks, troubleshooting and how-to guides, 75 service functions (experimental).
- **Coding**: backup, compare, restore; quick codes verified against NCS Expert, learned from another tool, or from shared coding maps; FRM3 protection.
- **Share**: reports, forum posts without the VIN, snapshots (.k15) others open read-only, headless mode with a phone dashboard and MQTT, network cables.
- **Reasoning**: community diagnostic chains (Vacuum leak hunter, Charging system check), retrofit checker, pre-purchase check.
- **Trust**: CodeQL on every change, Dependabot, release checksums and signed build provenance, a protected main branch, DCO sign-off for contributions.
- **Safety, legal and transparency**: the whole page in the app and in docs/LEGAL.md, and a short "Before you start" notice confirmed once.
- **Polish for testers**: a real Mac app with its icon, first-launch steps for Windows and Mac, the fault database offered on the Faults page when codes only have a general meaning.
- **Ready for testers**: cable check before connecting (FTDI or clone, latency, port in use), recovery when a job is cut off (a note on disk, the water pump test's pump switched off on reconnect, the computer kept awake), and the community baseline pipeline (share from the app, checked and built on GitHub).
- **Battery**: the battery drain finder (the car's own clues, days until it won't start, the fuse-pull hunt).
- **Support**: an optional tip link (PayPal) in the app, the website and the README; Klemme15 stays free with nothing locked.
- **E36 beta (1996-1999)**: in the Garage with five models, the engine over OBD-II, and a [tester guide](E36-BETA.md) to map its other modules.
- **Catch and compare**: the Ghost Hunter (trigger-based captures with a minute before and after), known good baselines on the Analyse page, live values on the car drawing.

## Next

- **The beta**: testers with E46, E60 and E90-family cars and different cables, running the [tester guide](BETA.md), before 1.0.
- **E36, round two**: add each module an E36 tester's car answers at its real address, then confirm them on a second test. Pre-1996 E36s (older protocols, the round 20-pin plug) after that.
- Confirm the water pump tests, VANOS test and more service functions on real cars, from test-drive reports.
- Deeper E46, E39 and E53 support without BMW files: every module's DS2 fault memory and BMW's own engine values (VANOS, cylinder roughness), once they can be confirmed on real cars. Health history and the OBD-based tests already work.
- The first community coding maps, checks and chains in the library, and the first real baselines (three or more healthy cars per engine, starting with the N52).
- Wheel speeds on the car drawing, from the stability control module, once the value names are confirmed on real cars.
- More chains: thermostat, misfire triage, boost leaks on the N54/N55.

## Later

- Code-signed Windows builds, so a direct download shows no SmartScreen prompt (free for open source via SignPath Foundation), and a notarised Mac app (needs a paid Apple developer account), so neither system asks before the first launch.
- F- and G-series over Ethernet (ENET), once someone can test on a car.
- BLE adapters; an Android build.
- Avalonia 12 (the UI framework's next major version), as a planned upgrade.

## Not planned, on purpose

- Immobiliser work of any kind (DME/CAS alignment, key programming, ISN): it is what makes a stolen car start.
- Flashing module software, and coding the vehicle order or VIN (for example a battery type change): the riskiest writes there are. Comfort coding stays limited to comfort modules, with an automatic backup, a map known for the exact module and read-back.
- Coding the key module (CAS): comfort closing with the remote, auto-lock and key settings live there, next to the immobiliser data. Its only service job in Klemme15 is the steering lock counter reset, which clears event counters and never touches keys or immobiliser data.
- Bundling BMW's wiring diagrams (WDS): they are BMW's documents. Guides can name connectors and pins in our own words.
- Closed wireless adapters (MHD, Thor): their protocols aren't public. Open bridges (RFC 2217/TCP) work today; BLE adapters are planned.
