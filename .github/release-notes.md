Early alpha. Read-only diagnostics: scan, faults, readings and live data. Confirmed on a real E90 over D-CAN; most other models are tested against the built-in demo cars. Please report results from your car: see CONTRIBUTING.md.

**Official BMW meanings for every code:** in the Garage, download the optional bmw-unified-db fault database (13 MB, third-party) or point Klemme15 at your own ISTA/INPA files. See docs/FAULT-DATA.md.

**Readings without ISTA (new):** in the Garage press "Add engine readings" (one-time ~130 MB download, keeps a few MB). The Readings page then shows hundreds of engine values for every E90-family petrol and diesel engine, read by Klemme15 itself: no ISTA, no BMW files, any Windows 10/11, Mac or Linux computer. Not yet confirmed on a real car: please test and send your traffic log.

**Readings with your BMW files:** with "Use my own BMW files" ticked, every module's status values come from BMW's own scripts (ISTA/INPA/EDIABAS).

**Troubleshooting guides:** press Troubleshoot under a fault for step-by-step checks (mixture, misfires, VANOS/Valvetronic, cooling, fuel pressure, boost, charging, bus faults, wheel speed, steering angle and lock, O2 heaters, secondary air, airbags), with shortcuts to the readings that confirm each step.

**Downloads:** unzip and run Klemme15 (Klemme15.exe on Windows). The command-line tool is in the cli folder. EDIABAS mode (your own BMW files) is included in every build.

macOS: the app is unsigned, so right-click > Open the first time. Linux: chmod +x Klemme15, and add yourself to the dialout group for serial access.
