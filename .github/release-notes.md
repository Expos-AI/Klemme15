Early alpha. Scan, faults, readings, live data, guided tests and experimental service functions.

**New in this release:**
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
