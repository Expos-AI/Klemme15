Early alpha. Scan, faults, readings, live data, guided tests and experimental service functions.

**New in 0.12.2:**
- **Install via winget or Scoop** (`winget install Expos-AI.Klemme15`, or `scoop bucket add klemme15 https://github.com/Expos-AI/Klemme15; scoop install klemme15`), which skips the Windows SmartScreen prompt. Direct `.zip` downloads still work. The install recipes now update themselves on every release.

**0.12.1:**
- An **About** dialog (version, what Klemme15 is, links, licence) and small footer buttons in the sidebar for About, support, the privacy policy and a donation link.
- A written **privacy policy** (PRIVACY.md): everything stays on your computer; nothing is sent anywhere unless you ask.

**0.12:**
- **No-start triage.** "Why won't it start?" (Tests page) now reads the start-chain modules' fault memories (engine, key, steering lock) along with battery voltage and whether the engine cranked, and routes the result to a cause: battery, starter, steering lock, a crank/cam sensor, fuel, ignition, or a genuine immobiliser fault that belongs at a locksmith or dealer. It never touches keys or the immobiliser.
- **Contribute and share coding maps.** After verifying a module against NCS Expert, export its map (part number, coding index and each toggle's bit-operations, in our own numbers) and open a pull request. Once a map is shipped, an owner with the same module can use its comfort toggles with no NCS data — still with a backup, read-back and automatic restore. The shared database (db/coding-verified.json) starts empty and grows as real cars are verified.

**0.11.1:**
- **Analyse page (datalogging).** Load a CSV datalog (recorded by Klemme15 or any tool) and scatter one channel against another, see a value averaged over a tuning-style RPM/load map, and play the log back with a scrubber while every channel reads out in step. The Live page's recording opens straight in it with the new "Analyse" button.

**0.11:**
- **Coding (experimental).** Back up any comfort module's coding with no BMW data, compare it later to see exactly what any tool changed, and restore it in one click, with every block read back. With your own NCS Expert data you can see every setting by name and switch popular options: LED bulb-check off (indicators, brake, plate, parking, fog), digital speed, instant consumption, dial lighting, mirror fold, windows that keep closing when a door opens, wiper finish. Writing unlocks only after Klemme15 decodes your module exactly as NCS Expert does. It backs up first, changes only those bits, re-checks every setting and puts the original back if anything is off. The key module (CAS), engine and safety modules are never coded. See docs/CODING.md.
- **FRM3 protection.** The Vehicle page tells you which footwell module you have, from its part number: FRM1, FRM2, or the fragile FRM3. A fragile or unidentified FRM is only coded or put through service functions at 13.0 V (a charger), and its faults are only cleared at 12.5 V. A low battery now warns before you clear faults or code on cars with an FRM.
- **Valvetronic end-stop learning without BMW files** (N52, MSV70/MSV80).
- **Cooling system bleed assistant** (Tests page) for engines with an electric water pump. It walks you through BMW's 12-minute routine (engine off, pedal to the floor) and watches the pump run.
- **Phone dashboard for headless mode.** Open `klemme15 serve` on your phone for gauges, live tiles with sparklines, warnings and the engine's faults.
- **How-to guides** with sources (Tests page): where every module is on an E90, fuses and the B+ cable, a parasitic battery drain test, connector notes for common jobs, the trunk lid harness, and the FRM3 problem.
- The E60 family's 0x72 module is now correctly named the body module (KBM), not a footwell module.

**0.10.3:** practical advice for codes that are usually harmless. The junction box relay code A731 (common after an aftermarket radio install or a flat battery), the odometer backup mismatch 9315 and the "power supply interrupted" A738 now say when to ignore them and when to look further (including how to do a parasitic draw test), and are no longer listed as "fix now". DSC 5E40 on E90-family cars explains the steering angle calibration that usually clears it.

**0.10.2 (second real-car test):** engine readings confirmed working without BMW files (372 values read on a 2006 325i). "Read all" now reads 20 values per request: seconds instead of over a minute. Units are readable (°C, ° crank, mg/stroke, rpm). Live data pauses when you leave its page (unless recording), so the quick test runs at full speed: the module broadcast now works on the E90, so absent modules are skipped in 0.1 s.

**Fixed after the first real-car test (2006 E90 325i):**
- Module identification is decoded: part number, hardware and coding index and production date for every D-CAN module (the engine computer's part number now pins down its exact version, e.g. MSV70).
- Engine readings without BMW files now work: the engine computer refused the request Klemme15 used, so values are read the way BMW's own tools do (a temporary measurement record). This also fixes the VANOS and cylinder roughness tests.
- Battery voltage is read from BMW's own measurement when the engine computer ignores the standard OBD request; live data no longer stalls on values the car never answers.
- Live data CSV: columns could shift when values were ticked after starting; every row now lines up with its header.
- The cruise control module (LDM, 0x1C) is in the E90 list; the junction box and footwell module try the E90 versions first; the quick test is faster.
- Model data checked against published sources: the North American 325i/325xi have the 3.0 L N52, plus corrections to years and body styles across families.

**New in 0.10:**
- **More cars**: 1 Series E8x, 5/6 Series E60/E61/E63/E64 (2007 on), X5 E70 / X6 E71, Z4 E89, 5 Series E39, X5 E53, 7 Series E38, each with demo cars. Built from BMW's module data, not yet confirmed on real cars: please report.
- **Guided tests with a verdict** (Tests page): fuel trims at idle and 2,500 rpm, VANOS target vs actual, cylinder roughness, and "Why won't it start?" (read-only).
- **Faults sorted** into fix now / worth checking / background (info and shadow memory).
- **Restart a frozen module** (iDrive, Bluetooth, amplifier and more) without disconnecting the battery.
- **12.5 V floor** for every write, and a warning before writing to footwell modules known to corrupt.
- **Copy for a forum post**, with the VIN removed.
- **Wi-Fi/network cables** (RFC 2217 or raw TCP) and **headless mode** with HTTP/JSON and MQTT for a Raspberry Pi.
- **Steering lock counter reset** for early E90s with a key slot (counters only, never keys or immobiliser codes), and a **3-stage intake manifold check** for N52 retrofits. Confirmed on a real E90 over D-CAN; most other models are tested against the built-in demo cars. Please report results from your car: see CONTRIBUTING.md.

**Official BMW meanings for every code:** in the Garage, download the optional bmw-unified-db fault database (13 MB, third-party) or point Klemme15 at your own ISTA/INPA files. See docs/FAULT-DATA.md.

**Service functions (experimental): 75 of them.** Service reminder reset (oil, brakes, brake fluid, microfilter, spark plugs, vehicle check; E46 oil and inspection), battery registration, engine adaptation reset, Valvetronic and DISA learning, cylinder cut-out test, fuel tank leak test, secondary air, oxygen sensor and catalyst checks, VANOS flush, component tests (fan, fuel pump, thermostat, water pump, secondary air pump, throttle, DISA, purge valve), diesel service (particulate filter, glow plugs, EGR and swirl flaps, injector codes, cylinder balance, cooling bleed), DSC (steering angle and sensor calibration, ABS bleeding, flat tyre reset, wheel speed sensor test), gearbox (adaptation reset, SMG learning and bleeding, xDrive transfer case), body (windows, sunroof, climate flaps, rain sensor, parking sensors, gauge test). Klemme15 checks the car, asks you to confirm each requirement, runs BMW's own procedure for your module version and always switches tested parts off again. None has been confirmed on a real car yet, so they only run with experimental functions switched on. Battery registration also works without BMW files; the others need "Use my own BMW files". Full list: docs/SERVICE-FUNCTIONS.md. Please report how it went.

**Readings without ISTA:** in the Garage press "Add engine readings" (one-time ~130 MB download, keeps a few MB). The Readings page then shows hundreds of engine values for every E90-family petrol and diesel engine, read by Klemme15 itself: no ISTA, no BMW files, any Windows 10/11, Mac or Linux computer. Not yet confirmed on a real car: please test and send your traffic log.

**Readings with your BMW files:** with "Use my own BMW files" ticked, every module's status values come from BMW's own scripts (ISTA/INPA/EDIABAS).

**Troubleshooting guides:** press Troubleshoot under a fault for step-by-step checks (mixture, misfires, VANOS/Valvetronic, cooling, fuel pressure, boost, charging, bus faults, wheel speed, steering angle and lock, O2 heaters, secondary air, airbags), with shortcuts to the readings that confirm each step.

**Downloads:** unzip and run Klemme15 (Klemme15.exe on Windows). The command-line tool is in the cli folder. EDIABAS mode (your own BMW files) is included in every build.

macOS: the app is unsigned, so right-click > Open the first time. Linux: chmod +x Klemme15, and add yourself to the dialout group for serial access.
