Early alpha. Read-only diagnostics: scan, faults, readings and live data. First real-car result: a 2006 E90 325i over D-CAN; most other models are tested against the built-in demo cars. Please report results from your car: see CONTRIBUTING.md.

**Official BMW meanings for every code:** in the Garage, download the optional bmw-unified-db fault database (13 MB, third-party) or point Klemme15 at your own ISTA/INPA files. See docs/FAULT-DATA.md.

**Readings:** every status value each module offers (oxygen sensors, fuel trims, VANOS, injectors, wheel speeds, battery sensor and more), computed by BMW's own scripts from your ISTA/INPA/EDIABAS files. Connect with "Use my own EDIABAS files" ticked. Try it on any demo car first.

**Downloads:** unzip and run Klemme15 (Klemme15.exe on Windows). The command-line tool is in the cli folder. EDIABAS mode (your own BMW files) is included in every build.

macOS: the app is unsigned, so right-click > Open the first time. Linux: chmod +x Klemme15, and add yourself to the dialout group for serial access.
